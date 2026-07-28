using GprPrediction.Wpf.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using GprPrediction.Wpf.Services;

namespace GprPrediction.Wpf;

// Component status helpers

/// <summary>
//
/// </summary>
public enum ComponentState { Pending, Checking, Ok, Warning, Fail }

/// <summary>
//
/// </summary>
public sealed class ComponentStatus : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private ComponentState _state = ComponentState.Pending;
    private string _versionText = "";
    private string _statusText = "대기 중";

    private static readonly SolidColorBrush BrushOk       = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush BrushWarn     = new(Color.FromRgb(0xFF, 0xA0, 0x00));
    private static readonly SolidColorBrush BrushFail     = new(Color.FromRgb(0xFF, 0x54, 0x70));
    private static readonly SolidColorBrush BrushChecking = new(Color.FromRgb(0x4F, 0x8C, 0xFF));
    private static readonly SolidColorBrush BrushPending  = new(Color.FromRgb(0x8A, 0x93, 0xA6));

    public required string Name { get; init; }

    public ComponentState State
    {
        get => _state;
        set { _state = value; Fire(); Fire(nameof(Icon)); Fire(nameof(IconBrush)); }
    }

    public string VersionText
    {
        get => _versionText;
        set { _versionText = value; Fire(); Fire(nameof(VersionVisible)); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Fire(); }
    }

    public string Icon => _state switch
    {
        ComponentState.Ok => "O",
        ComponentState.Warning => "!",
        ComponentState.Fail => "X",
        ComponentState.Checking => "...",
        _ => "-"
    };

    public SolidColorBrush IconBrush => _state switch
    {
        ComponentState.Ok       => BrushOk,
        ComponentState.Warning  => BrushWarn,
        ComponentState.Fail     => BrushFail,
        ComponentState.Checking => BrushChecking,
        _                       => BrushPending
    };

    public Visibility VersionVisible =>
        string.IsNullOrEmpty(_versionText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
//
    /// </summary>
    public void Reset()
    {
        VersionText = "";
        StatusText = "대기 중";
        State = ComponentState.Pending;
    }

    /// <summary>
//
    /// </summary>
    private void Fire([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Launcher window

/// <summary>
//
/// </summary>
public partial class LauncherWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _statusText = "시스템 확인 중...";
    private double _downloadProgress;
    private bool _isIndeterminate = true;
    private string _downloadLabel = "";
    private bool _downloading;
    private bool _hasError;
    private bool _juliaFailed;
    private CancellationTokenSource _cts = new();
    private Task _runTask = Task.CompletedTask;
    private bool _retrying;
    private bool _proceeding;

    public List<ComponentStatus> Components { get; } =
    [
        new() { Name = ".NET 8 Runtime" },
        new() { Name = "Python 3.11"    },
        new() { Name = "Julia 1.10"     }
    ];

    private ComponentStatus DotNetComp => Components[0];
    private ComponentStatus PythonComp => Components[1];
    private ComponentStatus JuliaComp  => Components[2];

    /// <summary>
//
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set
        {
            _downloadProgress = value;
            Notify();
            Notify(nameof(DownloadPercentText));
            Notify(nameof(DownloadDetailsVisibility));
        }
    }

    public bool IsIndeterminateProgress
    {
        get => _isIndeterminate;
        private set
        {
            _isIndeterminate = value;
            Notify();
            Notify(nameof(DownloadPercentText));
            Notify(nameof(DownloadDetailsVisibility));
        }
    }

    public string DownloadLabel
    {
        get => _downloadLabel;
        private set
        {
            _downloadLabel = value;
            Notify();
            Notify(nameof(DownloadDetailsVisibility));
        }
    }

//
    public string DownloadPercentText =>
        (_isIndeterminate || _downloadProgress <= 0) ? "" : $"{_downloadProgress:0}%";

    public Visibility DownloadDetailsVisibility =>
        string.IsNullOrWhiteSpace(DownloadLabel) &&
        string.IsNullOrWhiteSpace(DownloadPercentText)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility ProgressRowVisibility  => _downloading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorButtonsVisibility => _hasError    ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SkipJuliaVisibility    => _juliaFailed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
//
    /// </summary>
    public LauncherWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { _runTask = RunAsync(_cts.Token); };
        Closed += (_, _) =>
        {
            _cts.Cancel();
            _cts.Dispose();
        };
    }

//

    /// <summary>
//
    /// </summary>
    private async Task RunAsync(CancellationToken ct)
    {
        ResetAll();

//
        DotNetComp.State = ComponentState.Checking;
        DotNetComp.StatusText = "확인 중";
        StatusText = ".NET Runtime 확인 중...";
        await Task.Delay(200, ct);

        var v = Environment.Version;
        DotNetComp.VersionText = $"v{v.Major}.{v.Minor}.{v.Build}";
        DotNetComp.StatusText  = "정상";
        DotNetComp.State       = ComponentState.Ok;

        // 2. Python 3.11
        PythonComp.State = ComponentState.Checking;
        PythonComp.StatusText = "확인 중";
        StatusText = "Python 버전 확인 중...";

        var py = await PythonProvisioner.CheckAsync(ct);
        if (!py.IsValid)
        {
            PythonComp.StatusText = py.Version is null ? "미설치" : "버전 오류";
            PythonComp.State = ComponentState.Warning;
            StatusText = "Python 3.11 다운로드 중... (~27 MB)";
            BeginDownload();
            try
            {
                await PythonProvisioner.DownloadAndInstallAsync(MakeProgress(PythonComp), ct);
                py = await PythonProvisioner.CheckAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EndDownload();
                PythonComp.StatusText = "실패";
                PythonComp.State = ComponentState.Fail;
                StatusText = $"Python 다운로드 실패: {ShortError(ex)}";
                SetError(julia: false);
                return;
            }
            EndDownload();
        }

        if (py.IsValid)
        {
            PythonComp.VersionText = $"v{py.Version}";
            PythonComp.StatusText = "패키지 확인 중";
            PythonComp.State = ComponentState.Checking;
            StatusText = "Python 알고리즘 패키지 확인 중...";

            try
            {
                await PythonProvisioner.EnsureAlgorithmDependenciesAsync(
                    py.ExecutablePath,
                    PythonRuntimeLocator.GetDefaultAlgorithmDirectory(),
                    MakeProgress(PythonComp),
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PythonComp.StatusText = "실패";
                EndDownload();
                PythonComp.State = ComponentState.Fail;
                StatusText = $"Python 패키지 준비 실패: {ShortError(ex)}";
                SetError(julia: false);
                return;
            }

            PythonComp.StatusText  = "정상";
            EndDownload();
            PythonComp.State       = ComponentState.Ok;
        }
        else
        {
            PythonComp.StatusText = "실패";
            PythonComp.State      = ComponentState.Fail;
            StatusText = "Python 3.11 준비에 실패했습니다.";
            SetError(julia: false);
            return;
        }

        // 3. Julia 1.10
        JuliaComp.State = ComponentState.Checking;
        JuliaComp.StatusText = "확인 중";
        StatusText = "Julia 버전 확인 중...";

        var julia = await JuliaProvisioner.CheckAsync(ct);
        if (!julia.IsFound)
        {
            JuliaComp.StatusText = "미설치";
            JuliaComp.State      = ComponentState.Warning;
            StatusText = "Julia 1.10 다운로드 중... (~150 MB)";
            BeginDownload();
            try
            {
                await JuliaProvisioner.DownloadAndInstallAsync(MakeProgress(JuliaComp), ct);
                julia = await JuliaProvisioner.CheckAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EndDownload();
                JuliaComp.StatusText = "경고";
                JuliaComp.State      = ComponentState.Warning;
                StatusText = $"Julia 다운로드 실패: {ShortError(ex)}";
                SetError(julia: true);
                return;
            }
            EndDownload();
        }

        if (julia.IsFound)
        {
            JuliaComp.VersionText = julia.Version is not null ? $"v{julia.Version}" : "";
            JuliaComp.StatusText = "TDA 확인 중";
            JuliaComp.State = ComponentState.Checking;
            StatusText = "Julia TDA 패키지 확인 중...";
            BeginDownload();
            try
            {
                await JuliaProvisioner.EnsureTdaPackagesAsync(julia.ExecutablePath!, MakeProgress(JuliaComp), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EndDownload();
                JuliaComp.StatusText = "실패";
                JuliaComp.State = ComponentState.Fail;
                StatusText = $"Julia TDA 패키지 준비 실패: {ShortError(ex)}";
                SetError(julia: true);
                return;
            }

            EndDownload();
            JuliaComp.StatusText  = "정상";
            JuliaComp.State       = ComponentState.Ok;
        }
        else
        {
//
//
            if (JuliaRuntimeLocator.GetBundledJuliaExecutable() is not null)
            {
                JuliaComp.StatusText = "경고";
                JuliaComp.State      = ComponentState.Warning;
            }
            else
            {
                JuliaComp.StatusText = "경고";
                JuliaComp.State      = ComponentState.Warning;
                SetError(julia: true);
                return;
            }
        }

        StatusText = "준비 완료. 잠시 후 실행합니다...";
        await Task.Delay(600, ct);
        ProceedToMain();
    }

    // Helper methods

    /// <summary>
//
    /// </summary>
    private Progress<(string Message, double Percent)> MakeProgress(ComponentStatus comp)
        => new(x =>
        {
//
            comp.StatusText = x.Percent >= 0 ? $"{x.Percent:0}%" : "다운로드 중...";
            if (!_downloading)
            {
                _downloading = true;
                Notify(nameof(ProgressRowVisibility));
            }

            DownloadLabel = x.Message;
            if (x.Percent < 0) { IsIndeterminateProgress = true; }
            else               { IsIndeterminateProgress = false; DownloadProgress = Math.Clamp(x.Percent, 0, 100); }
        });

    /// <summary>
//
    /// </summary>
    private void BeginDownload()
    {
        _downloading = true;
        DownloadProgress = 0;
        IsIndeterminateProgress = true;
        Notify(nameof(ProgressRowVisibility));
    }

    /// <summary>
//
    /// </summary>
    private void EndDownload()
    {
        _downloading = false;
        DownloadLabel = "";
        DownloadProgress = 0;
        IsIndeterminateProgress = true;
        Notify(nameof(ProgressRowVisibility));
    }

    /// <summary>
//
    /// </summary>
    private void SetError(bool julia)
    {
        _hasError    = true;
        _juliaFailed = julia;
        Notify(nameof(ErrorButtonsVisibility));
        Notify(nameof(SkipJuliaVisibility));
    }

    /// <summary>
//
    /// </summary>
    private void ResetAll()
    {
        _hasError = _juliaFailed = _downloading = false;
        DownloadLabel = "";
        DownloadProgress = 0;
        foreach (var c in Components) c.Reset();
        Notify(nameof(ErrorButtonsVisibility));
        Notify(nameof(SkipJuliaVisibility));
        Notify(nameof(ProgressRowVisibility));
    }

//
    /// <summary>
//
    /// </summary>
    private static string ShortError(Exception ex)
    {
        var msg = ex.Message;
//
        var pathStart = msg.IndexOfAny(['\'', '/']);
        if (pathStart >= 0)
        {
            var pathEnd = msg.IndexOf('\'', pathStart + 1);
            if (pathEnd < 0) pathEnd = msg.Length;
            var path = msg[pathStart..pathEnd];
            var fileName = System.IO.Path.GetFileName(path.TrimEnd('\''));
            if (!string.IsNullOrEmpty(fileName))
                msg = msg[..pathStart] + $"'{fileName}'" + (pathEnd < msg.Length ? msg[pathEnd..] : "");
        }
        return msg.Length > 120 ? msg[..120] + "..." : msg;
    }

    /// <summary>
//
    /// </summary>
    private void ProceedToMain()
    {
        if (_proceeding)
        {
            return;
        }

        _proceeding = true;
        try
        {
            var app = Application.Current;
            if (app is null)
            {
                return;
            }

            var previousShutdownMode = app.ShutdownMode;
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                var main = new MainWindow();
                app.MainWindow = main;
                main.Show();
                Hide();
                Close();
            }
            finally
            {
                app.ShutdownMode = previousShutdownMode;
            }
        }
        catch (Exception ex)
        {
            _proceeding = false;
            StatusText = $"메인 화면 실행 실패: {ShortError(ex)}";
            SetError(julia: false);

            CustomMessageBox.Show(
                $"메인 화면을 열지 못했습니다.\n\n{ex}",
                "GPR 시작 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Button handlers

    /// <summary>
//
    /// </summary>
    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_retrying)
        {
            return;
        }

        _retrying = true;
        var previousCts = _cts;
        previousCts.Cancel();
        try
        {
            try { await _runTask; } catch (OperationCanceledException) { }
            previousCts.Dispose();
            _cts = new CancellationTokenSource();
            _runTask = RunAsync(_cts.Token);
        }
        finally
        {
            _retrying = false;
        }
    }

    /// <summary>
//
    /// </summary>
    private void SkipJulia_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        JuliaComp.StatusText = "건너뜀";
        JuliaComp.State = ComponentState.Warning;
        ProceedToMain();
    }

    /// <summary>
//
    /// </summary>
    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Application.Current.Shutdown();
    }

    /// <summary>
//
    /// </summary>
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


