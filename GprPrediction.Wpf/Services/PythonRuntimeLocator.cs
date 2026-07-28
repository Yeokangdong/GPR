using System.IO;

namespace GprPrediction.Wpf.Services;

/// <summary>
/// 번들 Python 위치와 알고리즘 폴더 기본 경로를 찾아주는 도우미
/// </summary>
public static class PythonRuntimeLocator
{
    private const string VendorFolderName = "HBC";
    private const string ProductFolderName = "GPR";
    public const string BundledPythonRelativePath = "runtime\\python\\python.exe";
    public const string BundledPythonRootRelativePath = "runtime";
    public const string BundledPythonMarkerFileName = "python-current.txt";
    public const string BundledAlgorithmRelativePath = "algorithm";

    /// <summary>
    /// 기본 Python 실행 경로를 반환하며, 번들이 없으면 시스템 python 명령으로 fallback
    /// </summary>
    public static string GetDefaultPythonExecutable()
    {
        var bundledPython = GetBundledPythonPath();
        return File.Exists(bundledPython) ? bundledPython : "python";
    }

    /// <summary>
    /// 설정값, 번들 경로, 시스템 명령 순서로 실제 사용할 Python 실행 경로를 결정
    /// </summary>
    public static string Resolve(string configuredPythonExecutable)
    {
        var bundledPython = GetBundledPythonPath();
        if (!string.IsNullOrWhiteSpace(configuredPythonExecutable)
            && File.Exists(configuredPythonExecutable)
            && !IsLegacyBundledPythonPath(configuredPythonExecutable))
        {
            return configuredPythonExecutable;
        }

        if (File.Exists(bundledPython))
        {
            return bundledPython;
        }

        var legacyBundledPython = GetLegacyBundledPythonPath();
        if (File.Exists(legacyBundledPython))
        {
            return legacyBundledPython;
        }

        return string.IsNullOrWhiteSpace(configuredPythonExecutable) ? "python" : configuredPythonExecutable;
    }

    /// <summary>
    /// 앱 설치/빌드 폴더에 같이 들어있던 예전 Python 경로인지 확인
    /// </summary>
    private static bool IsLegacyBundledPythonPath(string pythonExecutable)
    {
        var legacyBundledPython = GetLegacyBundledPythonPath();
        try
        {
            return Path.GetFullPath(pythonExecutable)
                .Equals(Path.GetFullPath(legacyBundledPython), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 앱 기준 상대 경로에서 번들 Python 실행 파일의 절대 경로를 계산
    /// </summary>
    public static string GetBundledPythonPath()
    {
        var runtimeRoot = GetBundledPythonRootDirectory();
        var markerPath = Path.Combine(runtimeRoot, BundledPythonMarkerFileName);
        if (File.Exists(markerPath))
        {
            try
            {
                var markerInfo = new FileInfo(markerPath);
                var relativeDirectory = markerInfo.Length is > 0 and <= 512
                    ? File.ReadAllText(markerPath).Trim()
                    : string.Empty;
                if (IsSafeRelativeDirectory(relativeDirectory))
                {
                    var candidate = Path.GetFullPath(Path.Combine(runtimeRoot, relativeDirectory, "python.exe"));
                    if (IsPathInside(candidate, runtimeRoot) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
            }
        }

        string? versionedCandidate = null;
        try
        {
            versionedCandidate = Directory.Exists(runtimeRoot)
                ? Directory
                    .EnumerateDirectories(runtimeRoot, "python-*", SearchOption.TopDirectoryOnly)
                    .Select(directory => Path.Combine(directory, "python.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .FirstOrDefault()
                : null;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (!string.IsNullOrWhiteSpace(versionedCandidate))
        {
            return versionedCandidate;
        }

        return GetLegacyBundledPythonPath();
    }

    /// <summary>
    /// 사용자별 쓰기 가능한 번들 Python 런타임 루트 폴더 경로를 반환
    /// </summary>
    public static string GetBundledPythonRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolderName,
            ProductFolderName,
            BundledPythonRootRelativePath);
    }

    /// <summary>
    /// 기존 앱 설치 폴더 기준의 예전 Python 경로를 반환
    /// </summary>
    private static string GetLegacyBundledPythonPath()
    {
        return Path.Combine(AppContext.BaseDirectory, BundledPythonRelativePath);
    }

    private static bool IsSafeRelativeDirectory(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !Path.IsPathRooted(value) &&
        value.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
        !value.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => segment is "." or "..");

    private static bool IsPathInside(string candidate, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 기본 알고리즘 폴더가 존재하면 그 경로를, 없으면 빈 문자열을 반환
    /// </summary>
    public static string GetDefaultAlgorithmDirectory()
    {
        var bundledAlgorithm = Path.Combine(AppContext.BaseDirectory, BundledAlgorithmRelativePath);
        return Directory.Exists(bundledAlgorithm) ? bundledAlgorithm : string.Empty;
    }
}
