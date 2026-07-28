using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// Julia 준비 상태 점검 결과를 담는 값 객체
/// </summary>
public sealed record JuliaCheckResult(bool IsFound, string? ExecutablePath, Version? Version);

/// <summary>
/// Julia 1.10 portable zip을 다운로드해 앱 내부 런타임 폴더에 자동 설치
/// tda.jl 실행용 Julia를 준비하지만, 설치 실패가 곧 앱 전체 실패를 뜻하지는 않도록 설계
/// </summary>
public static class JuliaProvisioner
{
    private const string DownloadUrl =
        "https://julialang-s3.julialang.org/bin/winnt/x64/1.10/julia-1.10.6-win64.zip";
    private static readonly string[] TdaPackages = ["Images", "Ripserer", "FileIO", "Plots"];

    /// <summary>
    /// 번들 Julia 실행 파일을 찾고, 실제 실행 가능 여부까지 확인
    /// </summary>
    public static async Task<JuliaCheckResult> CheckAsync(CancellationToken ct)
    {
        // 번들 위치만 확인. 시스템 PATH/공용 설치 위치는 여기서 검사 안 함
        var path = JuliaRuntimeLocator.GetBundledJuliaExecutable();
        if (path is null)
        {
            return new JuliaCheckResult(false, null, null);
        }

        var version = await TryGetVersionAsync(path, ct);
        return new JuliaCheckResult(version is not null, path, version);
    }

    /// <summary>
    /// julia --version 출력을 읽어 실제 실행 가능 여부와 버전을 확인
    /// </summary>
    public static async Task<Version?> TryGetVersionAsync(string juliaExecutable, CancellationToken ct)
    {
        try
        {
            var si = new ProcessStartInfo
            {
                FileName = juliaExecutable,
                // startup-file을 끄면 첫 실행 지연과 사용자 환경 영향이 줄어듦
                Arguments = "--startup-file=no --version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(si);
            if (p is null)
            {
                return null;
            }

            // stdout/stderr를 동시에 읽어 교착 상태 회피
            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = p.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var output = await stdoutTask + await stderrTask;
            var m = Regex.Match(output, @"julia version (\d+)\.(\d+)\.(\d+)", RegexOptions.IgnoreCase);
            return m.Success
                ? new Version(int.Parse(m.Groups[1].Value),
                              int.Parse(m.Groups[2].Value),
                              int.Parse(m.Groups[3].Value))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> CheckTdaPackagesAsync(string juliaExecutable, CancellationToken ct)
    {
        var usingExpression = string.Join("; ", TdaPackages.Select(packageName => $"using {packageName}"));
        var result = await RunJuliaAsync(
            juliaExecutable,
            BuildJuliaEvalArguments(usingExpression),
            TimeSpan.FromSeconds(90),
            ct);

        return result.ExitCode == 0;
    }

    public static async Task EnsureTdaPackagesAsync(
        string juliaExecutable,
        IProgress<(string Message, double Percent)>? progress,
        CancellationToken ct)
    {
        if (await CheckTdaPackagesAsync(juliaExecutable, ct))
        {
            return;
        }

        progress?.Report(("Julia TDA 패키지 설치 준비 중...", -1));

        var packageList = string.Join(", ", TdaPackages.Select(packageName => $"\"{packageName}\""));
        var installScript =
            "import Pkg; " +
            $"pkgs = [{packageList}]; " +
            "for pkg in pkgs; println(\"Installing \" * pkg); Pkg.add(pkg); end; " +
            "Pkg.precompile()";

        var installResult = await RunJuliaAsync(
            juliaExecutable,
            BuildJuliaEvalArguments(installScript),
            TimeSpan.FromMinutes(20),
            ct,
            progress);

        if (installResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Julia TDA 패키지 설치 실패: " +
                ShortenProcessOutput(installResult.StandardOutput + Environment.NewLine + installResult.StandardError));
        }

        if (!await CheckTdaPackagesAsync(juliaExecutable, ct))
        {
            throw new InvalidOperationException("Julia TDA 패키지 설치 후 로드 확인에 실패했습니다.");
        }
    }

    /// <summary>
    /// Julia portable zip을 내려받아 runtime/julia 폴더에 압축 해제
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        IProgress<(string Message, double Percent)>? progress,
        CancellationToken ct)
    {
        var runtimeDir = JuliaRuntimeLocator.GetBundledJuliaBaseDirectory();
        Directory.CreateDirectory(runtimeDir);

        // Defender 실시간 감시 충돌을 줄이기 위해 TEMP에 받았다가 완료 후 옮김
        var tempZip = Path.Combine(Path.GetTempPath(), $"julia-{Guid.NewGuid():N}.zip");
        var finalZip = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "julia-1.10.6-win64.zip");
        try
        {
            progress?.Report(("Julia 1.10 다운로드 준비 중...", -1));

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var src = await response.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tempZip))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloaded += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = downloaded * 100.0 / totalBytes;
                        var mb = downloaded / 1_048_576.0;
                        var totalMb = totalBytes / 1_048_576.0;
                        progress?.Report(($"Julia 다운로드 중... {mb:0.0} / {totalMb:0.0} MB", pct));
                    }
                    else
                    {
                        var mb = downloaded / 1_048_576.0;
                        progress?.Report(($"Julia 다운로드 중... {mb:0.0} MB", -1));
                    }
                }
            }

            progress?.Report(("압축 해제 중... 잠시만 기다려 주세요.", -1));
            ZipFile.ExtractToDirectory(tempZip, runtimeDir, overwriteFiles: true);

            // 완료된 zip은 Downloads 폴더로 옮겨 재사용 가능 상태로 유지
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
        }
    }

    private static async Task<JuliaProcessResult> RunJuliaAsync(
        string juliaExecutable,
        string arguments,
        TimeSpan timeout,
        CancellationToken ct,
        IProgress<(string Message, double Percent)>? progress = null)
    {
        var si = new ProcessStartInfo
        {
            FileName = juliaExecutable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(si);
        if (process is null)
        {
            throw new InvalidOperationException($"Julia 프로세스를 시작할 수 없습니다. {juliaExecutable}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        progress?.Report(("Julia TDA 패키지 확인 중...", -1));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new JuliaProcessResult(
                124,
                await TryReadProcessOutputAsync(stdoutTask),
                await TryReadProcessOutputAsync(stderrTask) +
                Environment.NewLine +
                $"Julia command timed out after {timeout.TotalMinutes:0.#} minutes.");
        }

        return new JuliaProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string BuildJuliaEvalArguments(string expression)
    {
        var escapedExpression = expression.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"--startup-file=no --history-file=no -e \"{escapedExpression}\"";
    }

    private static string ShortenProcessOutput(string output)
    {
        output = output.Trim();
        return output.Length <= 2000 ? output : output[^2000..];
    }

    private static async Task<string> TryReadProcessOutputAsync(Task<string> outputTask)
    {
        try
        {
            return await outputTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record JuliaProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
