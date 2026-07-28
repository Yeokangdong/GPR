using System.Configuration;
using System.Data;
using System.Windows;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf;

/// <summary>
/// WPF 애플리케이션의 시작점을 제공하고 첫 화면을 런처로 지정
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 애플리케이션 시작 시 런처 창을 열어 실행 환경 점검과 초기 진입 흐름을 시작
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 메인 화면보다 먼저 런처를 띄워 Python/Julia 준비 상태를 확인
        var launcher = new LauncherWindow();
        MainWindow = launcher;
        launcher.Show();
    }

    /// <summary>
    /// 앱 종료 직전 메인 ViewModel의 세션 상태를 한 번 더 저장
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        if (Current?.MainWindow?.DataContext is MainViewModel mainWindowViewModel)
        {
            mainWindowViewModel.PersistSessionState();
        }
        else
        {
            foreach (Window window in Windows)
            {
                if (window.DataContext is MainViewModel viewModel)
                {
                    viewModel.PersistSessionState();
                    break;
                }
            }
        }

        base.OnExit(e);
    }
}
