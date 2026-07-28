using GprPrediction.Wpf.ViewModels;
using GprPrediction.Wpf.Services;

namespace GprPrediction.Wpf.Infrastructure;

/// <summary>
/// 애플리케이션 전체에서 단 한 번만 생성되는 Composition Root
/// 서비스와 루트 ViewModel의 생성·수명·폐기를 이 형식에서만 관리
/// </summary>
public sealed class AppHost : IDisposable
{
    private static readonly Lazy<AppHost> LazyInstance =
        new(static () => new AppHost(), LazyThreadSafetyMode.ExecutionAndPublication);

    private bool disposed;

    /// <summary>
    /// AppHost 인스턴스 초기화
    /// 필수 의존성과 초기 상태를 생성 시점에 확정
    /// </summary>
    private AppHost()
    {
        var algorithmRunner = new AlgorithmRunner();
        var resultReader = new PredictionResultReader();
        var savedResultReader = new SavedResultReader();
        var savedResultWriter = new SavedResultWriter();
        var sessionStateStore = new AppSessionStateStore();
        var windowService = new WpfWindowService();
        var userDialogService = new WpfUserDialogService();

        MainViewModel = new MainViewModel(
            algorithmRunner,
            resultReader,
            savedResultReader,
            savedResultWriter,
            sessionStateStore,
            windowService,
            userDialogService);
    }

    /// <summary>
    /// Instance 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public static AppHost Instance => LazyInstance.Value;

    /// <summary>
    /// MainViewModel 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public MainViewModel MainViewModel { get; }

    /// <summary>
    /// Dispose 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        MainViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
