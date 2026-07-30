using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// Python 준비 상태 점검 결과를 담는 값 객체
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed record PythonCheckResult(bool IsValid, string ExecutablePath, Version? Version);

/// <summary>
/// 번들 Python 3.11 런타임을 점검하고, 없으면 embeddable zip을 내려받아 runtime/python 아래에 준비
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public static class PythonProvisioner
{
    private const int RequiredMajor = 3;
    private const int RequiredMinor = 11;
    private const string DownloadUrl = "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly string[] RequiredImportModules = ["obspy", "pandas", "torch", "cv2", "numpy", "PIL", "readgssi"];
    private static readonly string[] PinnedAlgorithmPackages =
    [
        "numpy==1.26.4",
        "pandas==2.2.2",
        "opencv-python==4.10.0.84",
        "Pillow==10.4.0",
        "scipy==1.13.1",
        "torch==2.3.1",
        "torchvision==0.18.1",
        "matplotlib==3.9.2",
        "seaborn==0.13.2",
        "ultralytics==8.2.103",
        "tqdm==4.66.5",
        "PyYAML==6.0.2",
        "requests==2.32.3",
        "psutil==6.0.0",
        "thop==0.1.1.post2209072238",
        "gitpython==3.1.43",
        "setuptools==75.1.0",
        "packaging==24.1",
        "obspy==1.4.1",
        "readgssi==0.0.22"
    ];

    /// <summary>
    /// 번들 Python 실행 파일이 존재하는지와 버전이 요구 조건을 만족하는지 검사
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static async Task<PythonCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        // 번들 위치만 확인. 시스템 PATH는 여기서 검사 안 함
        var candidate = PythonRuntimeLocator.GetBundledPythonPath();
        if (!File.Exists(candidate))
        {
            return new PythonCheckResult(false, candidate, null);
        }

        var version = await TryGetVersionAsync(candidate, cancellationToken);
        var isValid = version is not null && version.Major == RequiredMajor && version.Minor == RequiredMinor;
        return new PythonCheckResult(isValid, candidate, version);
    }

    /// <summary>
    /// python --version 결과를 읽어 Version 객체로 파싱
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static async Task<Version?> TryGetVersionAsync(string pythonExecutable, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutable);
        if (!File.Exists(pythonExecutable))
        {
            return null;
        }

        Process? versionProcess = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ConfigurePythonProcessEnvironment(startInfo, pythonExecutable);

            versionProcess = Process.Start(startInfo);
            if (versionProcess is null)
            {
                return null;
            }

            // stdout/stderr를 함께 읽어 프로세스 교착 회피
            var stdOutTask = versionProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = versionProcess.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await versionProcess.WaitForExitAsync(timeoutCts.Token);

            var output = (await stdOutTask) + (await stdErrTask);
            var match = Regex.Match(output, @"Python\s+(\d+)\.(\d+)\.(\d+)");
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build))
            {
                return null;
            }

            return new Version(major, minor, build);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcess(versionProcess);
            throw;
        }
        catch (OperationCanceledException)
        {
            KillProcess(versionProcess);
            return null;
        }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        finally
        {
            versionProcess?.Dispose();
        }
    }

    /// <summary>
    /// Python embeddable zip을 내려받아 runtime/python 폴더에 압축 해제
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        IProgress<(string Message, double Percent)>? progress,
        CancellationToken cancellationToken)
    {
        var runtimeRootDir = PythonRuntimeLocator.GetBundledPythonRootDirectory();
        Directory.CreateDirectory(runtimeRootDir);
        var installFolderName = $"python-3.11.9-{DateTime.Now:yyyyMMddHHmmss}";
        var runtimeDir = Path.Combine(runtimeRootDir, installFolderName);
        var stagingDir = Path.Combine(Path.GetTempPath(), $"python-install-{Guid.NewGuid():N}");
        var installationCompleted = false;

        // Defender 충돌을 줄이기 위해 TEMP에 내려받고 완료 후 Downloads로 옮김
        var tempZip = Path.Combine(Path.GetTempPath(), $"python-embed-{Guid.NewGuid():N}.zip");
        var finalZip = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "python-3.11.9-embed-amd64.zip");
        try
        {
            progress?.Report(("Python 3.11 다운로드 준비 중...", -1));
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            using var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var dst = File.Create(tempZip))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await src.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloaded += bytesRead;

                    // 총 크기를 알면 퍼센트와 MB를 함께, 아니면 다운로드 누적량만 표시
                    if (totalBytes > 0)
                    {
                        var pct = downloaded * 100.0 / totalBytes;
                        var mb = downloaded / 1_048_576.0;
                        var totalMb = totalBytes / 1_048_576.0;
                        progress?.Report(($"Python 다운로드 중... {mb:0.0} / {totalMb:0.0} MB", pct));
                    }
                    else
                    {
                        var mb = downloaded / 1_048_576.0;
                        progress?.Report(($"Python 다운로드 중... {mb:0.0} MB", -1));
                    }
                }

                await dst.FlushAsync(cancellationToken);
                if (downloaded <= 0 || totalBytes > 0 && downloaded != totalBytes)
                {
                    throw new InvalidDataException(
                        $"Python 다운로드 크기가 올바르지 않습니다. 수신 {downloaded}, 예상 {totalBytes}");
                }
            }

            progress?.Report(("압축 해제 중...", -1));
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(tempZip, stagingDir, overwriteFiles: true);

            progress?.Report(("설치 파일 반영 중...", -1));
            Directory.CreateDirectory(runtimeDir);
            CopyDirectory(stagingDir, runtimeDir, cancellationToken);
            if (!File.Exists(Path.Combine(runtimeDir, "python.exe")))
            {
                throw new InvalidDataException("압축을 푼 Python 실행 파일을 찾지 못했습니다.");
            }

            WriteCurrentPythonMarker(runtimeRootDir, installFolderName);
            installationCompleted = true;

            // 완료된 zip은 Downloads 폴더로 옮겨 다시 활용 가능 상태로 유지
            var downloadsDirectory = Path.GetDirectoryName(finalZip);
            if (!string.IsNullOrWhiteSpace(downloadsDirectory))
            {
                Directory.CreateDirectory(downloadsDirectory);
            }
            if (File.Exists(finalZip))
            {
                try { File.Delete(finalZip); } catch { }
            }

            try { File.Move(tempZip, finalZip); } catch { }
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                try { File.Delete(tempZip); } catch { }
            }

            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { }
            }

            if (!installationCompleted && Directory.Exists(runtimeDir))
            {
                try { Directory.Delete(runtimeDir, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// 알고리즘 실행에 필요한 Python 패키지를 확인하고 누락 시 자동 설치
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static async Task EnsureAlgorithmDependenciesAsync(
        string pythonExecutable,
        string algorithmDirectory,
        IProgress<(string Message, double Percent)>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmDirectory);
        if (!File.Exists(pythonExecutable))
        {
            throw new FileNotFoundException("Python 실행 파일을 찾을 수 없습니다.", pythonExecutable);
        }
        if (!Directory.Exists(algorithmDirectory))
        {
            throw new DirectoryNotFoundException($"알고리즘 폴더를 찾을 수 없습니다. {algorithmDirectory}");
        }

        PrepareEmbeddablePythonPathFile(pythonExecutable);
        var moduleCheck = await CheckRequiredModulesAsync(pythonExecutable, cancellationToken);
        if (moduleCheck.Success)
        {
            progress?.Report(("Python 패키지 확인 완료", 100));
            return;
        }

        progress?.Report(("Python pip 확인 중...", -1));
        if (!await RunPythonAsync(pythonExecutable, "-m pip --version", cancellationToken))
        {
            await InstallPipAsync(pythonExecutable, progress, cancellationToken);
        }

        progress?.Report(("Python 패키지 설치 중... 시간이 걸릴 수 있습니다.", -1));
        var installArgs = "-m pip install --upgrade --no-cache-dir --no-warn-script-location " +
            string.Join(' ', PinnedAlgorithmPackages);

        var installResult = await RunPythonDetailedAsync(pythonExecutable, installArgs, cancellationToken);
        moduleCheck = await CheckRequiredModulesAsync(pythonExecutable, cancellationToken);
        if (!installResult.Success || !moduleCheck.Success)
        {
            var detail = !installResult.Success
                ? installResult.GetShortError()
                : moduleCheck.GetShortError();
            throw new InvalidOperationException($"Python 알고리즘 패키지 설치 또는 확인에 실패했습니다. {detail}");
        }

        progress?.Report(("Python 패키지 설치 완료", 100));
    }

    /// <summary>
    /// embeddable Python의 격리 경로 파일에 site-packages 사용 설정 추가
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void PrepareEmbeddablePythonPathFile(string pythonExecutable)
    {
        var pythonDirectory = Path.GetDirectoryName(pythonExecutable);
        if (string.IsNullOrWhiteSpace(pythonDirectory) || !Directory.Exists(pythonDirectory))
        {
            return;
        }

        var pthPath = Directory.EnumerateFiles(pythonDirectory, "python*._pth").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(pthPath))
        {
            return;
        }

        var lines = File.ReadAllLines(pthPath).ToList();
        var changed = false;
        changed |= AddLineIfMissing(lines, "Lib");
        changed |= AddLineIfMissing(lines, @"Lib\site-packages");
        if (!lines.Any(line => line.Trim().Equals("import site", StringComparison.OrdinalIgnoreCase)))
        {
            lines.RemoveAll(line => line.Trim().Equals("#import site", StringComparison.OrdinalIgnoreCase));
            lines.Add("import site");
            changed = true;
        }

        if (changed)
        {
            File.WriteAllLines(pthPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    /// <summary>
    /// 줄 목록에 값이 없을 때만 추가
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool AddLineIfMissing(List<string> lines, string value)
    {
        if (lines.Any(line => line.Trim().Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        lines.Add(value);
        return true;
    }

    /// <summary>
    /// 알고리즘 필수 모듈이 현재 Python에서 import 가능한지 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static Task<PythonCommandResult> CheckRequiredModulesAsync(string pythonExecutable, CancellationToken cancellationToken)
    {
        // 배포 패키지 버전과 실제 import 가능 여부를 함께 검사
        // 다른 버전이 이미 설치된 경우 고정 패키지 재설치를 건너뛰는 문제 방지
        var expectedVersions = string.Join(
            ", ",
            PinnedAlgorithmPackages.Select(package =>
            {
                var parts = package.Split("==", 2, StringSplitOptions.TrimEntries);
                return $"'{parts[0]}': '{parts[1]}'";
            }));
        var importChecks = string.Join("; ", RequiredImportModules.Select(module =>
            $"print('checking {module}'); import {module}; print('ok {module}')"));
        var script =
            "import importlib.metadata as metadata; " +
            "from packaging.version import Version; " +
            $"expected = {{{expectedVersions}}}; " +
            "mismatches = [" +
            "f'{name}=={metadata.version(name)} (expected {version})' " +
            "for name, version in expected.items() " +
            "if Version(metadata.version(name)) != Version(version)]; " +
            "assert not mismatches, 'package version mismatch: ' + ', '.join(mismatches); " +
            importChecks;
        return RunPythonDetailedAsync(pythonExecutable, $"-c \"{script}\"", cancellationToken);
    }

    /// <summary>
    /// pip이 없을 때 공식 bootstrap 스크립트를 받아 설치
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task InstallPipAsync(
        string pythonExecutable,
        IProgress<(string Message, double Percent)>? progress,
        CancellationToken cancellationToken)
    {
        var getPipPath = Path.Combine(Path.GetTempPath(), $"get-pip-{Guid.NewGuid():N}.py");
        try
        {
            progress?.Report(("pip 설치 파일 다운로드 중...", -1));
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var script = await httpClient.GetStringAsync(GetPipUrl, cancellationToken);
            await File.WriteAllTextAsync(getPipPath, script, Utf8NoBom, cancellationToken);

            progress?.Report(("pip 설치 중...", -1));
            if (!await RunPythonAsync(pythonExecutable, $"\"{getPipPath}\" --no-warn-script-location", cancellationToken))
            {
                throw new InvalidOperationException("pip 설치에 실패했습니다.");
            }
        }
        finally
        {
            if (File.Exists(getPipPath))
            {
                try { File.Delete(getPipPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Python 명령을 실행하고 종료 코드 성공 여부만 반환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task<bool> RunPythonAsync(string pythonExecutable, string arguments, CancellationToken cancellationToken)
        => (await RunPythonDetailedAsync(pythonExecutable, arguments, cancellationToken)).Success;

    /// <summary>
    /// Python 명령을 실행하고 종료 코드와 출력 내용을 함께 반환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static async Task<PythonCommandResult> RunPythonDetailedAsync(string pythonExecutable, string arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutable);
        ArgumentNullException.ThrowIfNull(arguments);
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ConfigurePythonProcessEnvironment(startInfo, pythonExecutable);

            process = Process.Start(startInfo);
            if (process is null)
            {
                return new PythonCommandResult(-1, string.Empty, "Python 프로세스를 시작하지 못했습니다.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(30));
            await process.WaitForExitAsync(timeoutCts.Token);
            await Task.WhenAll(outputTask, errorTask);
            return new PythonCommandResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            return new PythonCommandResult(124, string.Empty, "Python 명령이 30분 제한시간을 초과했습니다.");
        }
        catch (Exception ex)
        {
            return new PythonCommandResult(-1, string.Empty, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// ConfigurePythonProcessEnvironment 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void ConfigurePythonProcessEnvironment(ProcessStartInfo startInfo, string pythonExecutable)
    {
        var pythonDirectory = Path.GetDirectoryName(pythonExecutable);
        if (string.IsNullOrWhiteSpace(pythonDirectory))
        {
            return;
        }

        var dllDirectory = Path.Combine(pythonDirectory, "DLLs");
        var sitePackagesDirectory = Path.Combine(pythonDirectory, "Lib", "site-packages");
        var currentPath = startInfo.EnvironmentVariables.ContainsKey("PATH")
            ? startInfo.EnvironmentVariables["PATH"]
            : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var pathParts = new[]
        {
            pythonDirectory,
            dllDirectory,
            sitePackagesDirectory
        }.Where(Directory.Exists);

        startInfo.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator, pathParts) +
            Path.PathSeparator +
            currentPath;
        startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
        startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
    }

    /// <summary>
    /// 임시 폴더에 압축 해제한 런타임 파일을 재시도하며 실제 런타임 폴더로 복사
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDir, relativePath));
        }

        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDir, sourceFilePath);
            var destinationFilePath = Path.Combine(destinationDir, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        }
    }

    /// <summary>
    /// 현재 사용할 Python 버전 폴더 이름을 marker 파일로 기록
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void WriteCurrentPythonMarker(string runtimeRootDir, string installFolderName)
    {
        if (string.IsNullOrWhiteSpace(installFolderName) ||
            installFolderName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException("Python 설치 폴더 이름이 올바르지 않습니다.", nameof(installFolderName));
        }

        var markerPath = Path.Combine(runtimeRootDir, PythonRuntimeLocator.BundledPythonMarkerFileName);
        var tempMarkerPath = $"{markerPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempMarkerPath, installFolderName, Utf8NoBom);
        File.Move(tempMarkerPath, markerPath, overwrite: true);
    }

    /// <summary>
    /// KillProcess 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void KillProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// Python 명령 실행 결과와 사용자에게 보여줄 짧은 오류 문구
    /// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
    /// </summary>
    private sealed record PythonCommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        /// <summary>
        /// Success 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public bool Success => ExitCode == 0;

        /// <summary>
        /// GetShortError 데이터 조회
        /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
        /// </summary>
        public string GetShortError()
        {
            var text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            var lines = text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            var meaningfulLine = lines
                .LastOrDefault(line =>
                    !line.StartsWith("Traceback ", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("File \"", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("~", StringComparison.OrdinalIgnoreCase));
            meaningfulLine ??= lines.LastOrDefault();

            return string.IsNullOrWhiteSpace(meaningfulLine)
                ? $"exit code: {ExitCode}"
                : meaningfulLine.Length > 220 ? meaningfulLine[..220] : meaningfulLine;
        }
    }
}
