using System.Diagnostics;
using System.IO;

namespace GprPrediction.Setup;

/// <summary>
/// Visual Studio에서 설치 프로젝트를 시작했을 때 생성된 MSI를 열어주는 진입점
/// </summary>
internal static class Program
{
    /// <summary>
    /// 솔루션 루트를 찾아 MSI 파일을 실행하거나 탐색기에서 선택
    /// </summary>
    private static int Main()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("솔루션 루트를 찾지 못했습니다.");
            return 1;
        }

        var installerPath = Path.Combine(
            repoRoot,
            "installer",
            "bin",
            "x64",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "GPR-Setup-x64.msi");

        if (File.Exists(installerPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            return 0;
        }

        var installerDirectory = Path.GetDirectoryName(installerPath);
        if (!string.IsNullOrWhiteSpace(installerDirectory) && Directory.Exists(installerDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = installerDirectory,
                UseShellExecute = true
            });
        }

        Console.Error.WriteLine($"MSI 파일을 찾지 못했습니다: {installerPath}");
        return 1;
    }

    /// <summary>
    /// 현재 실행 위치에서 상위 폴더를 거슬러 올라가며 솔루션 파일을 탐색
    /// </summary>
    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GprPredictionSuite.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
