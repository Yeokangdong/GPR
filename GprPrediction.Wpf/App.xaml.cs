using System.Configuration;
using System.Data;
using System.Windows;
using GprPrediction.Wpf.Infrastructure;
using GprPrediction.Wpf.Services;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf;

/// <summary>
/// WPF 애플리케이션의 시작점을 제공하고 첫 화면을 런처로 지정
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 애플리케이션 시작 시 런처 창을 열어 실행 환경 점검과 초기 진입 흐름을 시작
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Instance.LoadAndApply();
        _ = AppHost.Instance;

        // 메인 화면보다 먼저 런처를 띄워 Python/Julia 준비 상태를 확인
        var launcher = new LauncherWindow();
        MainWindow = launcher;
        launcher.Show();
    }

    /// <summary>
    /// 앱 종료 직전 메인 ViewModel의 세션 상태를 한 번 더 저장
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        AppHost.Instance.MainViewModel.PersistSessionState();
        AppHost.Instance.Dispose();

        base.OnExit(e);
    }
}
