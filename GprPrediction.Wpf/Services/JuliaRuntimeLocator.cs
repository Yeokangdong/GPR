using System.IO;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// Julia 실행 파일을 번들 폴더, PATH, 일반 설치 위치 순서로 탐색
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public static class JuliaRuntimeLocator
{
    private const string VendorFolderName = "HBC";
    private const string ProductFolderName = "GPR";
    public const string BundledJuliaBaseRelativePath = @"runtime\julia";

    /// <summary>
    /// runtime/julia 아래에서 버전 폴더를 찾아 번들 julia.exe 경로를 반환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static string? GetBundledJuliaExecutable()
    {
        var userBaseDir = GetBundledJuliaBaseDirectory();
        var executable = FindBundledJuliaExecutable(userBaseDir);
        if (executable is not null)
        {
            return executable;
        }

        var legacyBaseDir = Path.Combine(AppContext.BaseDirectory, BundledJuliaBaseRelativePath);
        return FindBundledJuliaExecutable(legacyBaseDir);
    }

    /// <summary>
    /// 설정값, 번들, PATH, 일반 설치 위치 순서로 실제 사용할 Julia 실행 파일을 찾기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static string? Resolve(string? configuredJuliaExecutable)
    {
        if (!string.IsNullOrWhiteSpace(configuredJuliaExecutable) && File.Exists(configuredJuliaExecutable))
        {
            return configuredJuliaExecutable;
        }

        return GetBundledJuliaExecutable() ?? FindOnEnvironmentPath() ?? FindInCommonInstallLocations();
    }

    /// <summary>
    /// PATH 환경 변수에 등록된 julia.exe를 검색
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string? FindOnEnvironmentPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var normalizedDirectory = directory.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(normalizedDirectory))
            {
                continue;
            }

            var candidate = Path.Combine(normalizedDirectory, "julia.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 사용자가 자주 설치하는 대표 위치를 순회해 Julia 설치본을 찾기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string? FindInCommonInstallLocations()
    {
        var searchRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".julia", "juliaup"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"C:\"
        };

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string? match;
            try
            {
                match = Directory.EnumerateDirectories(root, "Julia-*", SearchOption.TopDirectoryOnly)
                    .Select(dir => Path.Combine(dir, "bin", "julia.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .FirstOrDefault();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// 사용자별 쓰기 가능한 Julia 런타임 루트 폴더 경로를 반환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static string GetBundledJuliaBaseDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolderName,
            ProductFolderName,
            BundledJuliaBaseRelativePath);
    }

    /// <summary>
    /// 지정한 루트 폴더 아래에서 사용 가능한 julia.exe를 탐색
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string? FindBundledJuliaExecutable(string baseDir)
    {
        if (!Directory.Exists(baseDir))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateDirectories(baseDir, "julia-*", SearchOption.TopDirectoryOnly)
                .Select(d => Path.Combine(d, "bin", "julia.exe"))
                .Where(File.Exists)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
