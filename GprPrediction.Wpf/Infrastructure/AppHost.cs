using GprPrediction.Wpf.ViewModels;
using GprPrediction.Wpf.Services;

namespace GprPrediction.Wpf.Infrastructure;

/// <summary>
/// 애플리케이션 전체에서 단 한 번만 생성되는 Composition Root입니다.
/// 서비스와 루트 ViewModel의 생성·수명·폐기를 이 형식에서만 관리합니다.
/// </summary>
public sealed class AppHost : IDisposable
{
    private static readonly Lazy<AppHost> LazyInstance =
        new(static () => new AppHost(), LazyThreadSafetyMode.ExecutionAndPublication);

    private bool disposed;

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

    public static AppHost Instance => LazyInstance.Value;

    public MainViewModel MainViewModel { get; }

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
