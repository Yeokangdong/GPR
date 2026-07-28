using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GprPrediction.Wpf.Contracts;
using GprPrediction.Wpf.Models;
using GprPrediction.Wpf.Services;

namespace GprPrediction.Wpf.ViewModels;

/// <summary>
/// MainViewModel 관련 상태와 동작 관리
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaximumLogCharacters = 2 * 1024 * 1024;
    private const double PlotMargin = 40;
    private const double PlotWidth = 720;
    private const double PlotHeight = 500;
    private const double MapCanvasWidth = PlotWidth + (PlotMargin * 2);
    private const double MapCanvasHeight = PlotHeight + (PlotMargin * 2);
    private const double MaxPolylineJumpPixels = 120;
    private const int MinPointCountForJumpSplit = 8;
    private const int MaxRenderedPolylineCount = 25000;
    private const int MaxRenderedPointCount = 400000;
    private const string VendorFolderName = "HBC";
    private const string ProductFolderName = "GPR";

    private readonly AlgorithmRunner algorithmRunner;
    private readonly PredictionResultReader resultReader;
    private readonly SavedResultReader savedResultReader;
    private readonly SavedResultWriter savedResultWriter;
    private readonly AppSessionStateStore sessionStateStore;
    private readonly IWindowService windowService;
    private readonly IUserDialogService userDialogService;
    private readonly DispatcherTimer clockTimer = new();
    private readonly DispatcherTimer sessionSaveTimer = new();
    private CancellationTokenSource? algorithmRunCancellation;

    private string scanFilePath = string.Empty;
    private string algorithmDirectory = PythonRuntimeLocator.GetDefaultAlgorithmDirectory();
    private string pythonExecutable = PythonRuntimeLocator.GetDefaultPythonExecutable();
    private string scanRangeX = "20";
    private string scanRangeY = "3";
    private string xScale = "6";
    private string yScale = "1";
    private string threshold = "0.5";
    private bool useTda = true;
    private string tdaThreshold = "0.35";
    private string startPointX = "222035.5937";
    private string startPointY = "490429.4286";
    private string directionPointX = "222041.4817";
    private string directionPointY = "490405.8790";
    private string mapDwgPath = string.Empty;
    private IReadOnlyList<MapRenderFigure>? mapBackgroundFigures;
    private ImageSource? mapBackgroundImage;
    private ImageSource? analysisImage;
    private List<List<(double X, double Y)>>? dwgPolylineCache;
    private bool isMapLoading;
    private string mapLoadingText = string.Empty;
    private int mapLoadVersion;
    private int mapBitmapRenderVersion;
    private Task? mapBackgroundLoadTask;
    private bool isDwgMapLoading;
    private bool isTransientMapLoading;
    private string dwgMapLoadingText = string.Empty;
    private string transientMapLoadingText = string.Empty;
    private readonly string buildInfoText = CreateBuildInfoText();
    private IReadOnlyList<SavedResultPoint> loadedSavedResultPoints = [];
    private IReadOnlyList<SavedResultPoint> mergedPolylineSourcePoints = [];
    private IReadOnlyList<IReadOnlyList<SavedResultPoint>> mergedPolylineGroupsSourcePoints = [];
    private IReadOnlyList<string> openedSavedResultFiles = [];
    private List<string> addedMapPaths = [];
    private string currentResultCsvPath = string.Empty;
    private string currentAnalysisImagePath = string.Empty;
    private string currentInputInfoPath = string.Empty;
    private string currentSavedSenPath = string.Empty;
    private string currentSavedCsvPath = string.Empty;
    private string currentSavedAnalysisImagePath = string.Empty;
    private string currentSavedInputInfoPath = string.Empty;
    private bool currentRunTdaApplied;
    private string loadedSavedResultText = "불러온 저장 결과 없음";
    private int selectedResultIndex = 1;
    private bool isRestoringSessionState;
    private bool isSessionStateReady;

    private double transformScale = 1;
    private double transformMinX;
    private double transformMinY;
    private double transformOffsetX;
    private double transformOffsetY;
    private double transformContentHeight;

    private string statusText = "준비됨";
    private string logText = string.Empty;
    private bool isAlgorithmRunning;
    private string algorithmRunMessage = string.Empty;
    private string algorithmLogLine = string.Empty;
    private long algorithmLogSequence;
    private string lastAlgorithmResultText = string.Empty;
    private bool isLastAlgorithmResultVisible;
    private string digitalClockText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    private string todayText = DateTime.Now.ToString("yyyy.MM.dd(ddd)", new CultureInfo("ko-KR"));
    private bool disposed;
    private string analysisDistance = "0.00";
    private string analysisDepth = "0.00";
    private string analysisConfidence = "0.00";

    /// <summary>
    /// MainViewModel 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public MainViewModel(
        AlgorithmRunner algorithmRunner,
        PredictionResultReader resultReader,
        SavedResultReader savedResultReader,
        SavedResultWriter savedResultWriter,
        AppSessionStateStore sessionStateStore,
        IWindowService windowService,
        IUserDialogService userDialogService)
    {
        this.algorithmRunner = algorithmRunner ?? throw new ArgumentNullException(nameof(algorithmRunner));
        this.resultReader = resultReader ?? throw new ArgumentNullException(nameof(resultReader));
        this.savedResultReader = savedResultReader ?? throw new ArgumentNullException(nameof(savedResultReader));
        this.savedResultWriter = savedResultWriter ?? throw new ArgumentNullException(nameof(savedResultWriter));
        this.sessionStateStore = sessionStateStore ?? throw new ArgumentNullException(nameof(sessionStateStore));
        this.windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        this.userDialogService = userDialogService ?? throw new ArgumentNullException(nameof(userDialogService));

        RelayCommand Command(Func<object?, Task> execute, Predicate<object?>? canExecute = null) =>
            new(execute, canExecute, HandleCommandException);

        BrowseScanFileCommand = Command(_ => BrowseScanFileAsync());
        BrowseAlgorithmDirectoryCommand = Command(_ => BrowseAlgorithmDirectoryAsync());
        BrowsePythonExecutableCommand = Command(_ => BrowsePythonExecutableAsync());
        SelectMapCommand = Command(SelectMapAsync);
        AddMapCommand = Command(_ => AddMapAsync());
        BrowseResultCsvCommand = Command(_ => BrowseResultCsvAsync());
        RunAlgorithmCommand = Command(_ => RunAlgorithmAsync(), _ => CanRunAlgorithm());
        CancelAlgorithmCommand = Command(_ => CancelAlgorithmAsync(), _ => IsAlgorithmRunning);
        ResetAnalysisCommand = Command(_ =>
        {
            ResetAnalysisState();
            return Task.CompletedTask;
        }, _ => !IsAlgorithmRunning);
        OpenMapCommand = Command(_ => OpenMapAsync());
        OpenPrintCommand = Command(_ => OpenPrintAsync());
        OpenCommandCommand = Command(_ => OpenCommandAsync());
        OpenInputCommand = Command(_ => OpenInputAsync());
        OpenManualCommand = Command(_ => OpenManualAsync());
        OpenResultFolderCommand = Command(_ => OpenResultFolderAsync());
        SelectResultCommand = Command(SelectResultAsync);

        clockTimer.Interval = TimeSpan.FromSeconds(1);
        clockTimer.Tick += (_, _) =>
        {
            DigitalClockText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            TodayText = DateTime.Now.ToString("yyyy.MM.dd(ddd)", new CultureInfo("ko-KR"));
        };
        clockTimer.Start();

        sessionSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
        sessionSaveTimer.Tick += (_, _) => FlushSessionState();
        PropertyChanged += OnViewModelPropertyChanged;

        RestoreSessionScalars(sessionStateStore.Load());
        RefreshMapEntries();
        ClearStartupVisualResults();
        isSessionStateReady = true;
    }

    /// <summary>
    /// HandleCommandException 이벤트 처리
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void HandleCommandException(Exception exception)
    {
        AppendLog(exception.ToString());
        userDialogService.ShowMessage(
            exception.Message,
            "실행 오류",
            UserMessageKind.Warning);
    }

    /// <summary>
    /// OnViewModelPropertyChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!isSessionStateReady || isRestoringSessionState || string.IsNullOrWhiteSpace(e.PropertyName))
        {
            return;
        }

        if (e.PropertyName is nameof(MapDwgPath)
            or nameof(ScanFilePath)
            or nameof(AlgorithmDirectory)
            or nameof(PythonExecutable)
            or nameof(ScanRangeX)
            or nameof(ScanRangeY)
            or nameof(XScale)
            or nameof(YScale)
            or nameof(Threshold)
            or nameof(UseTda)
            or nameof(TdaThreshold)
            or nameof(StartPointX)
            or nameof(StartPointY)
            or nameof(DirectionPointX)
            or nameof(DirectionPointY))
        {
            ScheduleSessionStateSave();
        }
    }

    /// <summary>
    /// RestoreSessionScalars 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RestoreSessionScalars(AppSessionState? state)
    {
        if (state is null)
        {
            return;
        }

        isRestoringSessionState = true;

        try
        {
            scanFilePath = state.ScanFilePath ?? string.Empty;
            algorithmDirectory = string.IsNullOrWhiteSpace(state.AlgorithmDirectory)
                ? PythonRuntimeLocator.GetDefaultAlgorithmDirectory()
                : state.AlgorithmDirectory;
            pythonExecutable = string.IsNullOrWhiteSpace(state.PythonExecutable)
                ? PythonRuntimeLocator.GetDefaultPythonExecutable()
                : state.PythonExecutable;
            scanRangeX = string.IsNullOrWhiteSpace(state.ScanRangeX) ? scanRangeX : state.ScanRangeX;
            scanRangeY = string.IsNullOrWhiteSpace(state.ScanRangeY) ? scanRangeY : state.ScanRangeY;
            xScale = string.IsNullOrWhiteSpace(state.XScale) ? xScale : state.XScale;
            yScale = string.IsNullOrWhiteSpace(state.YScale) ? yScale : state.YScale;
            threshold = string.IsNullOrWhiteSpace(state.Threshold) ? threshold : state.Threshold;
            useTda = state.UseTda ?? useTda;
            tdaThreshold = string.IsNullOrWhiteSpace(state.TdaThreshold) ? tdaThreshold : state.TdaThreshold;
            startPointX = string.IsNullOrWhiteSpace(state.StartPointX) ? startPointX : state.StartPointX;
            startPointY = string.IsNullOrWhiteSpace(state.StartPointY) ? startPointY : state.StartPointY;
            directionPointX = string.IsNullOrWhiteSpace(state.DirectionPointX) ? directionPointX : state.DirectionPointX;
            directionPointY = string.IsNullOrWhiteSpace(state.DirectionPointY) ? directionPointY : state.DirectionPointY;
            mapDwgPath = state.SelectedMapPath ?? string.Empty;
            currentResultCsvPath = string.Empty;
            currentInputInfoPath = string.Empty;
            openedSavedResultFiles = [];
            addedMapPaths = state.AddedMapPaths?
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
            selectedResultIndex = state.SelectedResultIndex > 0 ? state.SelectedResultIndex : 1;
        }
        finally
        {
            isRestoringSessionState = false;
        }
    }

    /// <summary>
    /// ClearStartupVisualResults 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ClearStartupVisualResults()
    {
        Results.Clear();
        AnalysisImage = null;
        AnalysisDistance = "0.00";
        AnalysisDepth = "0.00";
        AnalysisConfidence = "0.00";
        currentResultCsvPath = string.Empty;
        currentAnalysisImagePath = string.Empty;
        currentInputInfoPath = string.Empty;
        loadedSavedResultPoints = [];
        mergedPolylineSourcePoints = [];
        mergedPolylineGroupsSourcePoints = [];
        openedSavedResultFiles = [];
        LoadedSavedResultText = "불러온 저장 결과 없음";
        StatusText = "준비됨";
        RecomputeMapPoints();
        RefreshSavedResultPointProjection();
    }

    /// <summary>
    /// ResetAnalysisState 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ResetAnalysisState()
    {
        if (IsAlgorithmRunning)
        {
            return;
        }

        Results.Clear();
        OnPropertyChanged(nameof(Results));
        AnalysisImage = null;
        AnalysisDistance = "0.00";
        AnalysisDepth = "0.00";
        AnalysisConfidence = "0.00";
        currentResultCsvPath = string.Empty;
        currentAnalysisImagePath = string.Empty;
        currentInputInfoPath = string.Empty;
        IsLastAlgorithmResultVisible = false;
        LastAlgorithmResultText = string.Empty;
        AlgorithmRunMessage = string.Empty;
        StatusText = "준비됨";
        RecomputeMapPoints();
        ScheduleSessionStateSave();
    }

    /// <summary>
    /// ScheduleSessionStateSave 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ScheduleSessionStateSave()
    {
        if (!isSessionStateReady || isRestoringSessionState)
        {
            return;
        }

        sessionSaveTimer.Stop();
        sessionSaveTimer.Start();
    }

    /// <summary>
    /// FlushSessionState 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void FlushSessionState()
    {
        sessionSaveTimer.Stop();

        if (!isSessionStateReady || isRestoringSessionState)
        {
            return;
        }

        try
        {
            sessionStateStore.Save(BuildSessionState());
        }
        catch (Exception ex)
        {
            AppendLog($"세션 상태 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// BuildSessionState 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private AppSessionState BuildSessionState()
    {
        return new AppSessionState
        {
            ScanFilePath = ScanFilePath,
            AlgorithmDirectory = AlgorithmDirectory,
            PythonExecutable = PythonExecutable,
            ScanRangeX = ScanRangeX,
            ScanRangeY = ScanRangeY,
            XScale = XScale,
            YScale = YScale,
            Threshold = Threshold,
            UseTda = UseTda,
            TdaThreshold = TdaThreshold,
            StartPointX = StartPointX,
            StartPointY = StartPointY,
            DirectionPointX = DirectionPointX,
            DirectionPointY = DirectionPointY,
            SelectedMapPath = MapDwgPath,
            AddedMapPaths = addedMapPaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CurrentResultCsvPath = currentResultCsvPath,
            OpenedSavedResultFiles = openedSavedResultFiles
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SelectedResultIndex = selectedResultIndex
        };
    }

    /// <summary>
    /// BrowseScanFileCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand BrowseScanFileCommand { get; }

    /// <summary>
    /// BrowseAlgorithmDirectoryCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand BrowseAlgorithmDirectoryCommand { get; }

    /// <summary>
    /// BrowsePythonExecutableCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand BrowsePythonExecutableCommand { get; }

    /// <summary>
    /// SelectMapCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand SelectMapCommand { get; }

    /// <summary>
    /// AddMapCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand AddMapCommand { get; }

    /// <summary>
    /// BrowseResultCsvCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand BrowseResultCsvCommand { get; }

    /// <summary>
    /// RunAlgorithmCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand RunAlgorithmCommand { get; }

    /// <summary>
    /// CancelAlgorithmCommand 상태 노출
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand CancelAlgorithmCommand { get; }

    /// <summary>
    /// ResetAnalysisCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand ResetAnalysisCommand { get; }

    /// <summary>
    /// OpenMapCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand OpenMapCommand { get; }

    /// <summary>
    /// OpenPrintCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand OpenPrintCommand { get; }

    /// <summary>
    /// OpenCommandCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand OpenCommandCommand { get; }

    /// <summary>
    /// OpenInputCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand OpenInputCommand { get; }

    /// <summary>
    /// OpenManualCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand OpenManualCommand { get; }

    /// <summary>
    /// OpenResultFolderCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand OpenResultFolderCommand { get; }

    /// <summary>
    /// SelectResultCommand 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public RelayCommand SelectResultCommand { get; }

    /// <summary>
    /// SuppressAlgorithmResultDialogs 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool SuppressAlgorithmResultDialogs { get; set; }

    /// <summary>
    /// Results 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<PredictionResult> Results { get; private set; } = new();

    /// <summary>
    /// MapPoints 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<MapPoint> MapPoints { get; private set; } = new();

    /// <summary>
    /// SavedResultPoints 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<SavedResultPoint> SavedResultPoints { get; private set; } = new();

    /// <summary>
    /// SavedResultPolylinePoints 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public PointCollection SavedResultPolylinePoints { get; private set; } = new();

    /// <summary>
    /// SavedResultPolylineGroups 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<PointCollection> SavedResultPolylineGroups { get; private set; } = [];

    /// <summary>
    /// SavedResultLineSegments 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<SavedResultLineSegment> SavedResultLineSegments { get; private set; } = [];

    /// <summary>
    /// MapEntries 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<MapEntry> MapEntries { get; } = new();

    /// <summary>
    /// SurveyLineX1 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double SurveyLineX1 { get; private set; }

    /// <summary>
    /// SurveyLineY1 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double SurveyLineY1 { get; private set; }

    /// <summary>
    /// SurveyLineX2 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double SurveyLineX2 { get; private set; }

    /// <summary>
    /// SurveyLineY2 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double SurveyLineY2 { get; private set; }

    /// <summary>
    /// DirectionPreviewX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double DirectionPreviewX { get; private set; }

    /// <summary>
    /// DirectionPreviewY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double DirectionPreviewY { get; private set; }

    /// <summary>
    /// MapBackgroundImage 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ImageSource? MapBackgroundImage
    {
        get => mapBackgroundImage;
        private set => SetProperty(ref mapBackgroundImage, value);
    }

    /// <summary>
    /// MapDwgPath 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string MapDwgPath
    {
        get => mapDwgPath;
        set
        {
            if (SetProperty(ref mapDwgPath, value))
            {
                mapBackgroundLoadTask = RebuildMapBackgroundAsync();
            }
        }
    }

    /// <summary>
    /// IsMapLoading 상태 노출
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsMapLoading
    {
        get => isMapLoading;
        private set => SetProperty(ref isMapLoading, value);
    }

    /// <summary>
    /// MapLoadingText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string MapLoadingText
    {
        get => mapLoadingText;
        private set => SetProperty(ref mapLoadingText, value);
    }

    /// <summary>
    /// ScanFilePath 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanFilePath
    {
        get => scanFilePath;
        set
        {
            if (SetProperty(ref scanFilePath, value))
            {
                OnPropertyChanged(nameof(ScanFileDisplayName));
                RunAlgorithmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// ScanFileDisplayName 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanFileDisplayName
        => string.IsNullOrWhiteSpace(scanFilePath)
            ? string.Empty
            : Path.GetFileName(scanFilePath);

    /// <summary>
    /// AlgorithmDirectory 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AlgorithmDirectory
    {
        get => algorithmDirectory;
        set
        {
            if (SetProperty(ref algorithmDirectory, value))
            {
                RunAlgorithmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// PythonExecutable 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string PythonExecutable
    {
        get => pythonExecutable;
        set
        {
            if (SetProperty(ref pythonExecutable, value))
            {
                RunAlgorithmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// ScanRangeX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanRangeX
    {
        get => scanRangeX;
        set
        {
            if (SetProperty(ref scanRangeX, value))
            {
                RecomputeMapPoints();
            }
        }
    }

    /// <summary>
    /// ScanRangeY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ScanRangeY
    {
        get => scanRangeY;
        set => SetProperty(ref scanRangeY, value);
    }

    /// <summary>
    /// XScale 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string XScale
    {
        get => xScale;
        set => SetProperty(ref xScale, value);
    }

    /// <summary>
    /// YScale 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string YScale
    {
        get => yScale;
        set => SetProperty(ref yScale, value);
    }

    /// <summary>
    /// Threshold 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string Threshold
    {
        get => threshold;
        set => SetProperty(ref threshold, value);
    }

    /// <summary>
    /// UseTda 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool UseTda
    {
        get => useTda;
        set => SetProperty(ref useTda, value);
    }

    /// <summary>
    /// TdaThreshold 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string TdaThreshold
    {
        get => tdaThreshold;
        set => SetProperty(ref tdaThreshold, value);
    }

    /// <summary>
    /// StartPointX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string StartPointX
    {
        get => startPointX;
        set
        {
            if (SetProperty(ref startPointX, value))
            {
                RecomputeMapPoints();
            }
        }
    }

    /// <summary>
    /// StartPointY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string StartPointY
    {
        get => startPointY;
        set
        {
            if (SetProperty(ref startPointY, value))
            {
                RecomputeMapPoints();
            }
        }
    }

    /// <summary>
    /// DirectionPointX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string DirectionPointX
    {
        get => directionPointX;
        set
        {
            if (SetProperty(ref directionPointX, value))
            {
                RecomputeMapPoints();
            }
        }
    }

    /// <summary>
    /// DirectionPointY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string DirectionPointY
    {
        get => directionPointY;
        set
        {
            if (SetProperty(ref directionPointY, value))
            {
                RecomputeMapPoints();
            }
        }
    }

    /// <summary>
    /// StatusText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    /// <summary>
    /// LogText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string LogText
    {
        get => logText;
        set => SetProperty(ref logText, value);
    }

    /// <summary>
    /// IsAlgorithmRunning 상태 노출
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsAlgorithmRunning
    {
        get => isAlgorithmRunning;
        private set
        {
            if (SetProperty(ref isAlgorithmRunning, value))
            {
                RunAlgorithmCommand.RaiseCanExecuteChanged();
                CancelAlgorithmCommand.RaiseCanExecuteChanged();
                ResetAnalysisCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// AlgorithmRunMessage 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AlgorithmRunMessage
    {
        get => algorithmRunMessage;
        private set => SetProperty(ref algorithmRunMessage, value);
    }

    /// <summary>
    /// 명령 창에서 원문 진행 로그를 누락 없이 표시하기 위한 최신 로그와 순번
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AlgorithmLogLine
    {
        get => algorithmLogLine;
        private set => SetProperty(ref algorithmLogLine, value);
    }

    /// <summary>
    /// AlgorithmLogSequence 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public long AlgorithmLogSequence
    {
        get => algorithmLogSequence;
        private set => SetProperty(ref algorithmLogSequence, value);
    }

    /// <summary>
    /// LastAlgorithmResultText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string LastAlgorithmResultText
    {
        get => lastAlgorithmResultText;
        private set => SetProperty(ref lastAlgorithmResultText, value);
    }

    /// <summary>
    /// IsLastAlgorithmResultVisible 상태 노출
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsLastAlgorithmResultVisible
    {
        get => isLastAlgorithmResultVisible;
        private set => SetProperty(ref isLastAlgorithmResultVisible, value);
    }

    /// <summary>
    /// DigitalClockText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string DigitalClockText
    {
        get => digitalClockText;
        set => SetProperty(ref digitalClockText, value);
    }

    /// <summary>
    /// TodayText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string TodayText
    {
        get => todayText;
        set => SetProperty(ref todayText, value);
    }

    /// <summary>
    /// BuildInfoText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string BuildInfoText => buildInfoText;

    /// <summary>
    /// CoordinateReferenceText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string CoordinateReferenceText => CoordinateTransformService.CoordinateReferenceText;

    /// <summary>
    /// LoadedSavedResultText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string LoadedSavedResultText
    {
        get => loadedSavedResultText;
        private set => SetProperty(ref loadedSavedResultText, value);
    }

    /// <summary>
    /// AnalysisDistance 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AnalysisDistance
    {
        get => analysisDistance;
        set => SetProperty(ref analysisDistance, value);
    }

    /// <summary>
    /// AnalysisDepth 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AnalysisDepth
    {
        get => analysisDepth;
        set => SetProperty(ref analysisDepth, value);
    }

    /// <summary>
    /// AnalysisConfidence 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string AnalysisConfidence
    {
        get => analysisConfidence;
        set => SetProperty(ref analysisConfidence, value);
    }

    /// <summary>
    /// AnalysisImage 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ImageSource? AnalysisImage
    {
        get => analysisImage;
        private set
        {
            if (SetProperty(ref analysisImage, value))
            {
                OnPropertyChanged(nameof(HasAnalysisImage));
            }
        }
    }

    /// <summary>
    /// HasAnalysisImage 상태 노출
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool HasAnalysisImage => analysisImage is not null;

    /// <summary>
    /// PersistSessionState 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void PersistSessionState()
    {
        if (!isSessionStateReady)
        {
            return;
        }

        FlushSessionState();
    }

    /// <summary>
    /// ShowTransientMapLoading 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void ShowTransientMapLoading(string text = "캐싱 중...")
    {
        transientMapLoadingText = string.IsNullOrWhiteSpace(text) ? "캐싱 중..." : text;
        isTransientMapLoading = true;
        RefreshMapLoadingOverlay();
    }

    /// <summary>
    /// HideTransientMapLoading 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void HideTransientMapLoading()
    {
        isTransientMapLoading = false;
        transientMapLoadingText = string.Empty;
        RefreshMapLoadingOverlay();
    }

    /// <summary>
    /// RefreshMapBitmapForViewportAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public async Task RefreshMapBitmapForViewportAsync(
        double zoomScale,
        double panX,
        double panY,
        double viewportWidth,
        double viewportHeight)
    {
        var figures = mapBackgroundFigures;
        if (figures is null || figures.Count == 0)
        {
            return;
        }

        if (viewportWidth <= 1 || viewportHeight <= 1)
        {
            return;
        }

        var renderVersion = ++mapBitmapRenderVersion;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (renderVersion != mapBitmapRenderVersion)
            {
                return;
            }

            MapBackgroundImage = BuildBackgroundBitmap(
                figures,
                zoomScale,
                panX,
                panY,
                viewportWidth,
                viewportHeight);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// RefreshMapLoadingOverlay 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RefreshMapLoadingOverlay()
    {
        IsMapLoading = isDwgMapLoading || isTransientMapLoading;
        MapLoadingText = isDwgMapLoading
            ? dwgMapLoadingText
            : isTransientMapLoading
                ? transientMapLoadingText
                : string.Empty;
    }

    /// <summary>
    /// CreateBuildInfoText 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string CreateBuildInfoText()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var version = assembly.GetName().Version;
        var versionText = version is null
            ? "v1.0.0"
            : $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        var buildDate = File.Exists(assembly.Location)
            ? File.GetLastWriteTime(assembly.Location)
            : DateTime.Now;

        return $"{versionText} | Build {buildDate:yyyy-MM-dd}";
    }

    /// <summary>
    /// GetBundledSavedResultFiles 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public IReadOnlyList<string> GetBundledSavedResultFiles()
    {
        var candidates = new List<string>();

        var userResultsDirectory = GetUserResultDirectory();
        if (Directory.Exists(userResultsDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(userResultsDirectory, "*.sen"));
        }

        var legacyUserResultsDirectory = GetLegacyUserResultDirectory();
        if (Directory.Exists(legacyUserResultsDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(legacyUserResultsDirectory, "*.sen"));
        }

        var runtimeResultsDirectory = Path.Combine(AppContext.BaseDirectory, "result");
        if (Directory.Exists(runtimeResultsDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(runtimeResultsDirectory, "*.sen"));
        }

        var sampleResultsDirectory = Path.Combine(AppContext.BaseDirectory, "sample-data", "results");
        if (Directory.Exists(sampleResultsDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(sampleResultsDirectory, "*.sen"));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// GetOpenedSavedResultFiles 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public IReadOnlyList<string> GetOpenedSavedResultFiles()
        => openedSavedResultFiles;

    /// <summary>
    /// GetPreferredAnalysisImagePath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string? GetPreferredAnalysisImagePath()
    {
        if (!string.IsNullOrWhiteSpace(currentAnalysisImagePath) && File.Exists(currentAnalysisImagePath))
        {
            return currentAnalysisImagePath;
        }

        var algorithmImage = Path.Combine(AlgorithmDirectory, "results", "data.jpg");
        if (File.Exists(algorithmImage))
        {
            return algorithmImage;
        }

        var bundledImage = Path.Combine(AppContext.BaseDirectory, "sample-data", "print", "data.jpg");
        return File.Exists(bundledImage) ? bundledImage : null;
    }

    /// <summary>
    /// GetPreferredResultCsvPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string? GetPreferredResultCsvPath()
    {
        return FindExistingResultCsvCandidates().FirstOrDefault();
    }

    /// <summary>
    /// FindExistingResultCsvCandidates 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private IEnumerable<string> FindExistingResultCsvCandidates()
    {
        var candidates = new List<string>();

        void AddCandidate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (File.Exists(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(path);
            }
        }

        AddCandidate(currentResultCsvPath);
        AddCandidate(Path.Combine(AlgorithmDirectory, "results", "prediction_results.csv"));
        AddCandidate(Path.Combine(AlgorithmDirectory, "print", "prediction_results.csv"));
        AddCandidate(Path.Combine(AppContext.BaseDirectory, "print", "prediction_results.csv"));
        AddCandidate(Path.Combine(AppContext.BaseDirectory, "sample-data", "print", "prediction_results.csv"));

        if (Directory.Exists(AlgorithmDirectory))
        {
            foreach (var csv in Directory
                         .EnumerateFiles(AlgorithmDirectory, "prediction_results.csv", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                AddCandidate(csv);
            }
        }

        return candidates;
    }

    /// <summary>
    /// LoadMergedSavedResultsAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public async Task LoadMergedSavedResultsAsync(IEnumerable<string> senPaths)
    {
        var combined = new List<SavedResultPoint>();
        var loadedNames = new List<string>();
        var loadedPaths = new List<string>();

        // 손상된 SEN 하나가 전체 병합을 중단하지 않도록 파일별 읽기 격리
        foreach (var senPath in senPaths.Where(File.Exists))
        {
            IReadOnlyList<SavedResultPoint> points;
            try
            {
                points = await savedResultReader.ReadAsync(senPath, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppendLog($"SEN load failed: {Path.GetFileName(senPath)} - {ex.Message}");
                continue;
            }

            if (points.Count == 0)
            {
                continue;
            }

            combined.AddRange(points);
            loadedNames.Add(Path.GetFileNameWithoutExtension(senPath));
            loadedPaths.Add(senPath);
        }

        loadedSavedResultPoints = combined;
        mergedPolylineSourcePoints = [];
        mergedPolylineGroupsSourcePoints = [];
        openedSavedResultFiles = loadedPaths;
        LoadedSavedResultText = loadedNames.Count == 0
            ? "불러온 저장 결과 없음"
            : string.Join(", ", loadedNames);
        RefreshSavedResultPointProjection();
        StatusText = combined.Count == 0
            ? "SEN 결과를 찾지 못했습니다."
            : $"저장 결과 로드 완료: {combined.Count}개";
        ScheduleSessionStateSave();
    }

    /// <summary>
    /// LoadSelectedSavedResultPointsAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public Task LoadSelectedSavedResultPointsAsync(IEnumerable<SavedResultPoint> points)
    {
        var selectedPoints = points.ToList();
        loadedSavedResultPoints = selectedPoints;
        mergedPolylineSourcePoints = selectedPoints;
        mergedPolylineGroupsSourcePoints = selectedPoints.Count == 0 ? [] : [selectedPoints];

        var sourceSummary = selectedPoints
            .Select(point => point.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LoadedSavedResultText = selectedPoints.Count == 0
            ? "선택된 병합 결과 없음"
            : $"병합 점 {selectedPoints.Count}개 / {string.Join(", ", sourceSummary)}";

        RefreshSavedResultPointProjection();
        StatusText = selectedPoints.Count == 0
            ? "병합 점이 선택되지 않았습니다."
            : $"병합 점 로드 완료: {selectedPoints.Count}개";
        ScheduleSessionStateSave();
        return Task.CompletedTask;
    }

    /// <summary>
    /// LoadMergedSelectionRowsAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public Task LoadMergedSelectionRowsAsync(IEnumerable<IReadOnlyList<SavedResultPoint>> rows)
    {
        var rowList = rows
            .Where(row => row.Count > 0)
            .Select(row => (IReadOnlyList<SavedResultPoint>)row.ToList())
            .ToList();

        // 선택 행의 그룹 경계를 보존해 서로 다른 측선을 개별 폴리라인으로 유지
        mergedPolylineGroupsSourcePoints = rowList;
        loadedSavedResultPoints = rowList
            .SelectMany(row => row)
            .DistinctBy(point => $"{point.SourceName}|{point.Label}|{point.X:0.000000}|{point.Y:0.000000}")
            .ToList();
        mergedPolylineSourcePoints = rowList.FirstOrDefault() ?? [];

        var fileSummary = rowList
            .SelectMany(row => row)
            .Select(point => point.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LoadedSavedResultText = rowList.Count == 0
            ? "선택된 병합 결과 없음"
            : $"병합 행 {rowList.Count}개 / {string.Join(", ", fileSummary)}";

        RefreshSavedResultPointProjection();
        StatusText = rowList.Count == 0
            ? "병합 행이 선택되지 않았습니다."
            : $"병합 행 로드 완료: {rowList.Count}개";
        ScheduleSessionStateSave();
        return Task.CompletedTask;
    }

    /// <summary>
    /// RefreshMapEntries 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RefreshMapEntries()
    {
        var mapsDirectory = Path.Combine(AppContext.BaseDirectory, "maps");
        var selectedPath = MapEntries.FirstOrDefault(entry => entry.IsSelected)?.FilePath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            selectedPath = mapDwgPath;
        }

        MapEntries.Clear();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(mapsDirectory))
        {
            var dwgFiles = Directory
                .EnumerateFiles(mapsDirectory, "*.dwg")
                .OrderBy(GetMapSortGroup)
                .ThenBy(GetMapSortNumber)
                .ThenBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);

            foreach (var dwgFile in dwgFiles)
            {
                seenPaths.Add(dwgFile);
                MapEntries.Add(new MapEntry
                {
                    Name = Path.GetFileNameWithoutExtension(dwgFile),
                    FilePath = dwgFile
                });
            }
        }

        foreach (var addedMapPath in addedMapPaths.Where(File.Exists))
        {
            if (!seenPaths.Add(addedMapPath))
            {
                continue;
            }

            MapEntries.Add(new MapEntry
            {
                Name = Path.GetFileNameWithoutExtension(addedMapPath),
                FilePath = addedMapPath
            });
        }

        var selectedEntry = MapEntries.FirstOrDefault(entry =>
            string.Equals(entry.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));

        if (selectedEntry is not null)
        {
            SelectMap(selectedEntry);
        }
        else if (MapEntries.Count > 0)
        {
            SelectMap(MapEntries[0]);
        }
        else
        {
            mapBackgroundLoadTask = RebuildMapBackgroundAsync();
        }
    }

    /// <summary>
    /// GetMapSortGroup 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static int GetMapSortGroup(string path)
        => int.TryParse(Path.GetFileNameWithoutExtension(path), out _) ? 0 : 1;

    /// <summary>
    /// IsBundledMapPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsBundledMapPath(string path)
    {
        var mapsDirectory = Path.Combine(AppContext.BaseDirectory, "maps");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(mapsDirectory))
        {
            return false;
        }

        try
        {
            var normalizedMapsDirectory = Path.GetFullPath(mapsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedMapsDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// GetMapSortNumber 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static int GetMapSortNumber(string path)
    {
        if (int.TryParse(Path.GetFileNameWithoutExtension(path), out var fileNumber))
        {
            return fileNumber;
        }

        var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"\d+");
        return match.Success && int.TryParse(match.Value, out var embeddedNumber)
            ? embeddedNumber
            : int.MaxValue;
    }

    /// <summary>
    /// BrowseScanFileAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task BrowseScanFileAsync()
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(ScanFilePath) && File.Exists(ScanFilePath)
            ? Path.GetDirectoryName(ScanFilePath) ?? string.Empty
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (IsAlgorithmTransientPath(initialDirectory) || !Directory.Exists(initialDirectory))
        {
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var selectedPath = userDialogService.SelectScanFile(initialDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            ScanFilePath = selectedPath;
            ApplyRecommendedXScale(selectedPath);
            ApplyRecommendedThreshold(selectedPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// BrowseAlgorithmDirectoryAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task BrowseAlgorithmDirectoryAsync()
    {
        var selectedDirectory = userDialogService.SelectAlgorithmDirectory();
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            AlgorithmDirectory = selectedDirectory;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// BrowsePythonExecutableAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task BrowsePythonExecutableAsync()
    {
        var selectedPath = userDialogService.SelectPythonExecutable();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            PythonExecutable = selectedPath;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// SelectMapAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task SelectMapAsync(object? parameter)
    {
        if (parameter is MapEntry entry)
        {
            SelectMap(entry);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// AddMapAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task AddMapAsync()
    {
        var selectedPath = userDialogService.SelectMapFile();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            if (!addedMapPaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
            {
                addedMapPaths.Add(selectedPath);
            }

            var entry = new MapEntry
            {
                Name = Path.GetFileNameWithoutExtension(selectedPath),
                FilePath = selectedPath
            };

            MapEntries.Add(entry);
            SelectMap(entry);
            ScheduleSessionStateSave();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// SelectMap 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SelectMap(MapEntry entry)
    {
        foreach (var existing in MapEntries)
        {
            existing.IsSelected = ReferenceEquals(existing, entry);
        }

        if (!IsBundledMapPath(entry.FilePath) &&
            File.Exists(entry.FilePath) &&
            !addedMapPaths.Contains(entry.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            addedMapPaths.Add(entry.FilePath);
        }

        MapDwgPath = entry.FilePath;
        ScheduleSessionStateSave();
    }

    /// <summary>
    /// BrowseResultCsvAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task BrowseResultCsvAsync()
    {
        var resultDirectory = GetPreferredResultDirectory();
        var selectedPaths = userDialogService.SelectResultFiles(resultDirectory);
        if (selectedPaths.Count > 0)
        {
            var hasCsv = selectedPaths.Any(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase));
            if (!hasCsv)
            {
                await LoadMergedSavedResultsAsync(selectedPaths);
                AppendLog($"저장 결과 SEN: {string.Join(", ", selectedPaths.Select(Path.GetFileName))}");
            }
            else
            {
                if (selectedPaths.Count > 1)
                {
                    userDialogService.ShowMessage(
                        "CSV는 한 번에 하나만 열 수 있습니다. 첫 번째 CSV를 불러옵니다.",
                        "결과 파일 선택",
                        UserMessageKind.Information);
                }

                var csvPath = selectedPaths.First(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase));
                await LoadResultsAsync(csvPath);
            }
        }
    }

    /// <summary>
    /// RunAlgorithmAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task RunAlgorithmAsync()
    {
        if (!TryBuildRequest(out var request))
        {
            return;
        }

        using var runCancellation = new CancellationTokenSource();
        algorithmRunCancellation = runCancellation;

        try
        {
            StatusText = "준비됨";
            AppendLog($"실행 시작: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            IsAlgorithmRunning = true;
            Results.Clear();
            OnPropertyChanged(nameof(Results));
            AnalysisImage = null;
            AnalysisDistance = "--";
            AnalysisDepth = "--";
            AnalysisConfidence = "--";
            currentResultCsvPath = string.Empty;
            currentAnalysisImagePath = string.Empty;
            currentInputInfoPath = string.Empty;
            ClearCurrentRunReport();
            RecomputeMapPoints();
            AlgorithmRunMessage = "GPR 데이터를 분석하고 있습니다...";
            IsLastAlgorithmResultVisible = false;
            StatusText = "준비됨";

            AlgorithmRunMessage = "1/7단계: 입력값 확인 중...";

            var runStartedUtc = DateTime.UtcNow;
            var progress = new Progress<string>(PublishAlgorithmProgress);
            var result = await algorithmRunner.RunAsync(request, runCancellation.Token, progress);
            currentInputInfoPath = result.InputInfoPath;
            currentRunTdaApplied = result.TdaApplied;
            AppendLog($"Algorithm work folder: {result.AlgorithmDirectory}");
            AppendLog($"Input info: {result.InputInfoPath}");
            AppendLog(result.StandardOutput);
            if (result.ExitCode != 0)
            {
                var failureDetail = string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? result.StandardError
                    : result.StandardOutput;
                StatusText = "준비됨";
                AppendLog("알고리즘 실행 실패");
                LastAlgorithmResultText = "분석 실패";
                IsLastAlgorithmResultVisible = true;
                IsAlgorithmRunning = false;
                AlgorithmRunMessage = string.Empty;
                ShowAlgorithmResultDialog(
                    string.IsNullOrWhiteSpace(failureDetail) ? "알고리즘 실행 중 오류가 발생했습니다." : failureDetail.Trim(),
                    "알고리즘 실행 오류",
                    UserMessageKind.Warning);
                return;
            }
            AppendLog($"최종 exit code: {result.ExitCode}, TDA 적용: {(result.TdaApplied ? "예" : "아니오")}");

            var resultCsvPath = File.Exists(result.ResultCsvPath) &&
                File.GetLastWriteTimeUtc(result.ResultCsvPath) >= runStartedUtc.AddSeconds(-2)
                    ? result.ResultCsvPath
                    : null;

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(resultCsvPath) && File.Exists(resultCsvPath))
            {
                AlgorithmRunMessage = "7/7단계: 결과 CSV 읽는 중...";
                await LoadResultsAsync(resultCsvPath);
                AlgorithmRunMessage = "7/7단계: 분석 이미지 불러오는 중...";
                LoadAnalysisImage(result.ResultImagePath);
                AlgorithmRunMessage = "7/7단계: 결과 파일을 저장하는 중...";
                SaveCurrentResultsAsSen();
                StatusText = $"준비됨";
                LastAlgorithmResultText = $"분석 완료 · 결과 {Results.Count}건";
                IsLastAlgorithmResultVisible = true;
                IsAlgorithmRunning = false;
                AlgorithmRunMessage = string.Empty;
                ShowAlgorithmResultDialog(
                    $"분석이 완료되었습니다.\n결과 {Results.Count}건을 불러왔습니다.",
                    "분석 완료",
                    UserMessageKind.Information);
                StatusText = $"준비됨";
            }
            else
            {
                Results.Clear();
                OnPropertyChanged(nameof(Results));
                AnalysisImage = null;
                AnalysisDistance = "--";
                AnalysisDepth = "--";
                AnalysisConfidence = "--";
                currentResultCsvPath = string.Empty;
                currentAnalysisImagePath = string.Empty;
                RecomputeMapPoints();
                StatusText = "탐지 결과 없음";
                LastAlgorithmResultText = "분석 완료 · 탐지 결과 없음";
                IsLastAlgorithmResultVisible = true;
                IsAlgorithmRunning = false;
                AlgorithmRunMessage = string.Empty;
                AppendLog("탐지 결과 CSV가 생성되지 않았습니다. 이전 결과를 표시하지 않고 화면을 비웠습니다.");
                ShowAlgorithmResultDialog(
                    "분석은 완료되었지만 탐지된 결과가 없습니다.\n이전 결과를 표시하지 않도록 화면을 비웠습니다.\n\n입력 파일, Threshold, 모델 설정을 확인해주세요.",
                    "탐지 결과 없음",
                    UserMessageKind.Information);
                StatusText = "준비됨";
                AppendLog("결과 CSV를 찾지 못했습니다.");
            }

            if (IsLastAlgorithmResultVisible && LastAlgorithmResultText.Contains("결과 없음", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = LastAlgorithmResultText;
            }

            if (!IsLastAlgorithmResultVisible)
            {
                LastAlgorithmResultText = "분석 완료 · 결과 없음";
                IsLastAlgorithmResultVisible = true;
                IsAlgorithmRunning = false;
                AlgorithmRunMessage = string.Empty;
            }

            if (IsLastAlgorithmResultVisible && !IsAlgorithmRunning)
            {
                StatusText = LastAlgorithmResultText;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "준비됨";
            LastAlgorithmResultText = "분석 취소";
            IsLastAlgorithmResultVisible = true;
            IsAlgorithmRunning = false;
            AlgorithmRunMessage = string.Empty;
            AppendLog("분석이 사용자 요청으로 취소되었습니다.");
            ShowAlgorithmResultDialog("분석을 취소했습니다.", "분석 취소", UserMessageKind.Information);
        }
        catch (Exception ex)
        {
            StatusText = "준비됨";
            LastAlgorithmResultText = "분석 오류";
            IsLastAlgorithmResultVisible = true;
            IsAlgorithmRunning = false;
            AlgorithmRunMessage = string.Empty;
            AppendLog(ex.ToString());
            ShowAlgorithmResultDialog(ex.Message, "알고리즘 실행 오류", UserMessageKind.Warning);
        }
        finally
        {
            if (ReferenceEquals(algorithmRunCancellation, runCancellation))
            {
                algorithmRunCancellation = null;
            }

            if (IsAlgorithmRunning)
            {
                IsAlgorithmRunning = false;
                AlgorithmRunMessage = string.Empty;
            }
        }
    }

    /// <summary>
    /// ShowAlgorithmResultDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ShowAlgorithmResultDialog(
        string message,
        string caption,
        UserMessageKind kind)
    {
        if (SuppressAlgorithmResultDialogs)
        {
            SuppressAlgorithmResultDialogs = false;
            return;
        }

        userDialogService.ShowMessage(message, caption, kind);
    }

    /// <summary>
    /// CancelAlgorithmAsync 실행 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task CancelAlgorithmAsync()
    {
        if (!IsAlgorithmRunning)
        {
            return Task.CompletedTask;
        }

        AlgorithmRunMessage = "분석을 취소하는 중입니다...";
        algorithmRunCancellation?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// NormalizeAlgorithmProgress 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private string NormalizeAlgorithmProgress(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return AlgorithmRunMessage;
        }

        if (message.StartsWith("1/7", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAlgorithmStageMessage("1/7단계", message, "입력값 및 실행 환경 확인 중...");
        }

        if (message.StartsWith("2/7", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAlgorithmStageMessage("2/7단계", message, "작업 폴더 준비 및 정리 중...");
        }

        if (message.StartsWith("3/7", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAlgorithmStageMessage("3/7단계", message, "입력 파일과 설정 파일 준비 중...");
        }

        if (message.StartsWith("4/7", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAlgorithmStageMessage("4/7단계 main_1.py", message, "AGC 전처리 및 이미지 생성 중...");
        }

        if (message.StartsWith("5/7", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAlgorithmStageMessage("5/7단계 tda.jl", message, "TDA 분석 중...");
        }

        if (message.StartsWith("6/7", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAlgorithmStageMessage("6/7단계 main_2.py", message, "객체 예측 및 좌표 변환 중...");
        }

        return message;
    }

    /// <summary>
    /// PublishAlgorithmProgress 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void PublishAlgorithmProgress(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        AlgorithmLogLine = message.TrimEnd();
        AlgorithmLogSequence++;
        AlgorithmRunMessage = NormalizeAlgorithmProgress(message);
    }

    /// <summary>
    /// FormatAlgorithmStageMessage 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string FormatAlgorithmStageMessage(string stageName, string message, string fallbackDetail)
    {
        var colonIndex = message.IndexOf(':');
        var detail = colonIndex >= 0 && colonIndex + 1 < message.Length
            ? message[(colonIndex + 1)..].Trim()
            : fallbackDetail;

        if (string.IsNullOrWhiteSpace(detail) || detail.Contains('?'))
        {
            detail = fallbackDetail;
        }

        return $"{stageName}: {detail}";
    }

    /// <summary>
    /// LoadAnalysisImage 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void LoadAnalysisImage(string? preferredImagePath = null)
    {
        if (LoadAnalysisImageFromPath(preferredImagePath))
        {
            currentAnalysisImagePath = preferredImagePath ?? string.Empty;
            return;
        }

        var imagePath = Path.Combine(AlgorithmDirectory, "results", "data.jpg");
        if (LoadAnalysisImageFromPath(imagePath))
        {
            currentAnalysisImagePath = imagePath;
            return;
        }

        var bundledImage = Path.Combine(AppContext.BaseDirectory, "sample-data", "print", "data.jpg");
        if (LoadAnalysisImageFromPath(bundledImage))
        {
            currentAnalysisImagePath = bundledImage;
        }
    }

    /// <summary>
    /// LoadResultsAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task LoadResultsAsync(string csvPath, int? preferredResultIndex = null)
    {
        Results = await resultReader.ReadAsync(csvPath, CancellationToken.None);
        OnPropertyChanged(nameof(Results));
        currentResultCsvPath = csvPath;
        RecomputeMapPoints();
        SelectResult(preferredResultIndex ?? 1);
        StatusText = $"준비됨";
        AppendLog($"결과 CSV: {csvPath}");
        ScheduleSessionStateSave();
    }

    /// <summary>
    /// LoadAnalysisImageFromPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool LoadAnalysisImageFromPath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return false;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            AnalysisImage = bitmap;
            return true;
        }
        catch (Exception ex)
        {
            AnalysisImage = null;
            AppendLog($"분석 이미지 로드 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SaveCurrentResultsAsSen 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SaveCurrentResultsAsSen()
    {
        if (!TryParseDouble(StartPointX, out var startX) ||
            !TryParseDouble(StartPointY, out var startY) ||
            !TryParseDouble(DirectionPointX, out var directionX) ||
            !TryParseDouble(DirectionPointY, out var directionY) ||
            Results.Count == 0)
        {
            return;
        }

        try
        {
            var resultDirectory = GetUserResultDirectory();
            var senPath = savedResultWriter.Write(
                resultDirectory,
                Results,
                startX,
                startY,
                directionX,
                directionY);

            var outputBaseName = Path.GetFileNameWithoutExtension(senPath);
            currentSavedSenPath = senPath;
            AppendLog($"Result folder: {resultDirectory}");
            currentSavedCsvPath = CopyCurrentOutputFile(
                currentResultCsvPath,
                resultDirectory,
                $"{outputBaseName}_prediction_results.csv",
                "CSV");

            var imageExtension = Path.GetExtension(currentAnalysisImagePath);
            if (string.IsNullOrWhiteSpace(imageExtension))
            {
                imageExtension = ".jpg";
            }

            currentSavedAnalysisImagePath = CopyCurrentOutputFile(
                currentAnalysisImagePath,
                resultDirectory,
                $"{outputBaseName}_analysis{imageExtension}",
                "image");

            currentSavedInputInfoPath = CopyCurrentOutputFile(
                currentInputInfoPath,
                resultDirectory,
                $"{outputBaseName}_input_info.txt",
                "input info");

            AppendLog($"저장 결과 SEN: {senPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"SEN 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// CopyCurrentOutputFile 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private string CopyCurrentOutputFile(string sourcePath, string resultDirectory, string outputFileName, string label)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            AppendLog($"{label} save skipped: source file was not found.");
            return string.Empty;
        }

        try
        {
            Directory.CreateDirectory(resultDirectory);
            var destinationPath = Path.Combine(resultDirectory, outputFileName);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            AppendLog($"Saved result {label}: {destinationPath}");
            return destinationPath;
        }
        catch (Exception ex)
        {
            AppendLog($"{label} save failed: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// BuildLastAnalysisReport 결과 구성
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public IReadOnlyList<string> BuildLastAnalysisReport()
    {
        var lines = new List<string>
        {
            string.Empty,
            "=== 분석 결과 상세 보고 ===",
            "1. 결과 산출 과정",
            "   원본 스캔 파일 -> main_1.py 전처리 -> data.jpg 생성"
        };

        if (currentRunTdaApplied)
        {
            lines.Add("   -> tda.jl 전처리 -> data.png 생성 -> main_2.py TDA 모델 분석");
        }
        else
        {
            lines.Add("   -> main_2.py 일반 모델 분석");
            if (UseTda)
            {
                lines.Add("   참고: TDA 사용을 요청했지만 이번 실행에서는 TDA가 적용되지 않아 일반 모델 경로로 처리");
            }
        }

        lines.Add("   -> 예측 CSV 읽기 -> 거리/심도/신뢰도 변환 -> 결과 파일 저장");
        lines.Add(string.Empty);
        lines.Add("2. 분석 입력");
        lines.Add($"   스캔 파일: {DisplayPath(ScanFilePath)}");
        lines.Add($"   측정 범위 X: {ScanRangeX} m");
        lines.Add($"   측정 범위 Z: {ScanRangeY} m");
        lines.Add($"   X Scale: {XScale}");
        lines.Add($"   Y Scale: {YScale}");
        lines.Add($"   신뢰도 Threshold: {Threshold}");
        lines.Add($"   TDA 요청: {(UseTda ? "사용" : "사용 안 함")}");
        lines.Add($"   TDA Threshold: {(UseTda ? TdaThreshold : "해당 없음")}");
        lines.Add($"   실제 분석 분기: {(currentRunTdaApplied ? "TDA 모델" : "일반 모델")}");
        lines.Add(string.Empty);
        lines.Add($"3. 최종 결과: 총 {Results.Count}건");

        if (Results.Count == 0)
        {
            lines.Add("   신뢰도 기준을 통과한 결과 없음");
        }
        else
        {
            foreach (var result in Results)
            {
                lines.Add(
                    $"   #{result.Index:00} (원본 #{result.SourceIndex:00}) " +
                    $"거리 {result.DistanceMeters:0.00} m / 심도 {result.DepthMeters:0.00} m / " +
                    $"신뢰도 {result.ConfidenceRatio * 100:0.00}%");
            }
        }

        lines.Add(string.Empty);
        lines.Add("4. 알고리즘 생성 파일");
        AddReportPath(lines, "입력 정보", currentInputInfoPath);
        AddReportPath(lines, "원본 결과 CSV", currentResultCsvPath);
        AddReportPath(lines, "원본 분석 이미지", currentAnalysisImagePath);
        lines.Add(string.Empty);
        lines.Add("5. 결과 폴더에 저장된 파일");
        AddReportPath(lines, "SEN 결과", currentSavedSenPath);
        AddReportPath(lines, "CSV 결과", currentSavedCsvPath);
        AddReportPath(lines, "분석 이미지", currentSavedAnalysisImagePath);
        AddReportPath(lines, "입력 정보 사본", currentSavedInputInfoPath);

        var resultFolder = FirstExistingDirectory(
            currentSavedSenPath,
            currentSavedCsvPath,
            currentSavedAnalysisImagePath,
            currentSavedInputInfoPath);
        lines.Add($"   결과 폴더: {(string.IsNullOrWhiteSpace(resultFolder) ? "저장된 결과 폴더 없음" : resultFolder)}");
        lines.Add("=== 상세 보고서 끝 ===");
        return lines;
    }

    /// <summary>
    /// ClearCurrentRunReport 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ClearCurrentRunReport()
    {
        currentSavedSenPath = string.Empty;
        currentSavedCsvPath = string.Empty;
        currentSavedAnalysisImagePath = string.Empty;
        currentSavedInputInfoPath = string.Empty;
        currentRunTdaApplied = false;
    }

    /// <summary>
    /// AddReportPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void AddReportPath(ICollection<string> lines, string label, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            lines.Add($"   {label}: 생성되지 않음");
            return;
        }

        var fullPath = DisplayPath(path);
        lines.Add($"   {label}: {fullPath} [{(File.Exists(fullPath) ? "확인됨" : "파일 없음")}]");
    }

    /// <summary>
    /// DisplayPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string DisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "없음";
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// FirstExistingDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string FirstExistingDirectory(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(DisplayPath(path));
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// RebuildMapBackgroundAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task RebuildMapBackgroundAsync()
    {
        var currentMapPath = MapDwgPath;
        var loadVersion = ++mapLoadVersion;
        var hasMapFile = !string.IsNullOrWhiteSpace(currentMapPath) && File.Exists(currentMapPath);

        if (hasMapFile)
        {
            isDwgMapLoading = true;
            dwgMapLoadingText = "캐싱 중...";
            RefreshMapLoadingOverlay();
            StatusText = $"준비됨";
            await Task.Yield();
        }

        List<List<(double X, double Y)>>? loadedPolylines = null;

        if (hasMapFile)
        {
            try
            {
                // DWG 파싱을 작업 스레드로 분리해 UI 렌더링 정지 방지
                loadedPolylines = await Task.Run(() => DwgMapLoader.LoadPolylines(currentMapPath));
            }
            catch (Exception ex)
            {
                if (loadVersion == mapLoadVersion)
                {
                    dwgPolylineCache = null;
                    mapBackgroundFigures = null;
                    MapBackgroundImage = null;
                    OnPropertyChanged(nameof(IsMapTransformReady));
                    isDwgMapLoading = false;
                    dwgMapLoadingText = string.Empty;
                    RefreshMapLoadingOverlay();
                    StatusText = "준비됨";
                }

                AppendLog($"DWG 배경 지도 로드 실패: {ex.Message}");
                return;
            }
        }

        if (loadVersion != mapLoadVersion)
        {
            return;
        }

        dwgPolylineCache = loadedPolylines;
        OnPropertyChanged(nameof(IsMapTransformReady));

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var haveBounds = false;

        var dwgBounds = dwgPolylineCache is null ? null : DwgMapLoader.GetBounds(dwgPolylineCache);
        if (dwgBounds.HasValue)
        {
            minX = dwgBounds.Value.MinX;
            minY = dwgBounds.Value.MinY;
            maxX = dwgBounds.Value.MaxX;
            maxY = dwgBounds.Value.MaxY;
            haveBounds = true;
        }

        if (TryParseDouble(StartPointX, out var startX) &&
            TryParseDouble(StartPointY, out var startY) &&
            TryParseDouble(DirectionPointX, out var directionX) &&
            TryParseDouble(DirectionPointY, out var directionY) &&
            TryParseDouble(ScanRangeX, out var parsedScanRangeX))
        {
            var (endX, endY) = SurveyLineProjector.ProjectAlongLine(startX, startY, directionX, directionY, parsedScanRangeX);
            var lineMinX = Math.Min(startX, endX);
            var lineMaxX = Math.Max(startX, endX);
            var lineMinY = Math.Min(startY, endY);
            var lineMaxY = Math.Max(startY, endY);

            var includeLine = true;
            if (dwgBounds.HasValue)
            {
                var dwgDiagonal = Math.Sqrt(Math.Pow(maxX - minX, 2) + Math.Pow(maxY - minY, 2));
                var margin = Math.Max(dwgDiagonal * 0.5, 1.0);
                includeLine = lineMaxX >= minX - margin && lineMinX <= maxX + margin &&
                              lineMaxY >= minY - margin && lineMinY <= maxY + margin;
            }

            if (includeLine)
            {
                minX = Math.Min(minX, lineMinX);
                maxX = Math.Max(maxX, lineMaxX);
                minY = Math.Min(minY, lineMinY);
                maxY = Math.Max(maxY, lineMaxY);
                haveBounds = true;
            }
        }

        if (!haveBounds)
        {
            mapBackgroundFigures = null;
            MapBackgroundImage = null;
            OnPropertyChanged(nameof(IsMapTransformReady));
            RecomputeMapPoints();
            StatusText = hasMapFile
                ? "표시할 지도 범위를 찾지 못했습니다."
                : "이 파일에 맞는 측선을 표시합니다.";
            isDwgMapLoading = false;
            dwgMapLoadingText = string.Empty;
            RefreshMapLoadingOverlay();
            return;
        }

        var spanX = Math.Max(maxX - minX, 1e-6);
        var spanY = Math.Max(maxY - minY, 1e-6);
        var scale = Math.Min(PlotWidth / spanX, PlotHeight / spanY);
        var contentWidth = spanX * scale;
        var contentHeight = spanY * scale;

        transformScale = scale;
        transformMinX = minX;
        transformMinY = minY;
        transformOffsetX = PlotMargin + (PlotWidth - contentWidth) / 2.0;
        transformOffsetY = PlotMargin + (PlotHeight - contentHeight) / 2.0;
        transformContentHeight = contentHeight;

        var figures = await Task.Run(() => BuildBackgroundFigures(
            dwgPolylineCache,
            transformOffsetX,
            transformMinX,
            transformScale,
            transformOffsetY,
            transformMinY,
            transformContentHeight));

        if (loadVersion != mapLoadVersion)
        {
            return;
        }

        mapBackgroundFigures = figures;
        MapBackgroundImage = BuildBackgroundBitmap(
            figures,
            1.0,
            0,
            0,
            MapCanvasWidth,
            MapCanvasHeight);
        RecomputeMapPoints();

        StatusText = hasMapFile
            ? $"지도 로드 완료: {Path.GetFileNameWithoutExtension(currentMapPath)}"
            : "준비됨";

        isDwgMapLoading = false;
        dwgMapLoadingText = string.Empty;
        RefreshMapLoadingOverlay();
    }

    /// <summary>
    /// EnsureMapBackgroundReadyAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task EnsureMapBackgroundReadyAsync()
    {
        if (mapBackgroundLoadTask is { IsCompleted: false })
        {
            await mapBackgroundLoadTask;
        }

        if (mapBackgroundFigures is not null &&
            MapBackgroundImage is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(MapDwgPath) || !File.Exists(MapDwgPath))
        {
            return;
        }

        mapBackgroundLoadTask = RebuildMapBackgroundAsync();
        await mapBackgroundLoadTask;
    }

    /// <summary>
    /// BuildBackgroundFigures 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static IReadOnlyList<MapRenderFigure>? BuildBackgroundFigures(
        List<List<(double X, double Y)>>? polylines,
        double offsetX,
        double minX,
        double scale,
        double offsetY,
        double minY,
        double contentHeight)
    {
        if (polylines is null || polylines.Count == 0)
        {
            return null;
        }

        var figures = new List<MapRenderFigure>();
        var renderedPolylineCount = 0;
        var renderedPointCount = 0;

        foreach (var polyline in polylines)
        {
            if (renderedPolylineCount >= MaxRenderedPolylineCount ||
                renderedPointCount >= MaxRenderedPointCount)
            {
                break;
            }

            if (polyline.Count < 2)
            {
                continue;
            }

            if (renderedPointCount + polyline.Count > MaxRenderedPointCount)
            {
                continue;
            }

            var renderableFigures = BuildRenderableFigures(
                polyline,
                offsetX,
                minX,
                scale,
                offsetY,
                minY,
                contentHeight);

            if (renderableFigures.Count == 0)
            {
                continue;
            }

            foreach (var points in renderableFigures)
            {
                if (renderedPolylineCount >= MaxRenderedPolylineCount ||
                    renderedPointCount >= MaxRenderedPointCount)
                {
                    break;
                }

                if (points.Count < 2)
                {
                    continue;
                }

                if (renderedPointCount + points.Count > MaxRenderedPointCount)
                {
                    continue;
                }

                figures.Add(new MapRenderFigure(points, GetBounds(points)));
                renderedPolylineCount++;
                renderedPointCount += points.Count;
            }
        }

        return figures;
    }

    /// <summary>
    /// GetBounds 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static Rect GetBounds(IReadOnlyList<Point> points)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.X > maxX) maxX = point.X;
            if (point.Y > maxY) maxY = point.Y;
        }

        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    /// <summary>
    /// BuildBackgroundBitmap 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static ImageSource? BuildBackgroundBitmap(
        IReadOnlyList<MapRenderFigure>? figures,
        double zoomScale,
        double panX,
        double panY,
        double viewportWidth,
        double viewportHeight)
    {
        if (figures is null || figures.Count == 0)
        {
            return null;
        }

        var normalizedScale = Math.Max(zoomScale, 0.05);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(viewportWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(viewportHeight));
        var inverseScale = 1.0 / normalizedScale;
        var visibleBaseRect = new Rect(
            (-panX) * inverseScale,
            (-panY) * inverseScale,
            viewportWidth * inverseScale,
            viewportHeight * inverseScale);
        visibleBaseRect.Inflate(4 * inverseScale, 4 * inverseScale);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushClip(new RectangleGeometry(new Rect(0, 0, viewportWidth, viewportHeight)));
            // 현재 확대·이동 행렬을 먼저 적용해 화면 좌표와 지도 도형 일치
            context.PushTransform(new MatrixTransform(normalizedScale, 0, 0, normalizedScale, panX, panY));

            var pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#727A86")), 0.5)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat,
                LineJoin = PenLineJoin.Miter
            };
            pen.Freeze();

            foreach (var figure in figures)
            {
                if (figure.Points.Count < 2 || !figure.Bounds.IntersectsWith(visibleBaseRect))
                {
                    continue;
                }

                for (var i = 1; i < figure.Points.Count; i++)
                {
                    context.DrawLine(pen, figure.Points[i - 1], figure.Points[i]);
                }
            }

            context.Pop();
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// BuildRenderableFigures 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static List<List<Point>> BuildRenderableFigures(
        List<(double X, double Y)> polyline,
        double offsetX,
        double minX,
        double scale,
        double offsetY,
        double minY,
        double contentHeight)
    {
        var projectedPoints = new List<Point>(polyline.Count);
        for (var i = 0; i < polyline.Count; i++)
        {
            var (x, y) = polyline[i];
            var screenX = offsetX + (x - minX) * scale;
            var screenY = offsetY + contentHeight - (y - minY) * scale;
            if (double.IsNaN(screenX) || double.IsInfinity(screenX) ||
                double.IsNaN(screenY) || double.IsInfinity(screenY))
            {
                continue;
            }

            projectedPoints.Add(new Point(screenX, screenY));
        }

        projectedPoints = RemoveConsecutiveDuplicates(projectedPoints);
        if (projectedPoints.Count < 2)
        {
            return [];
        }

        if (projectedPoints.Count < MinPointCountForJumpSplit)
        {
            return [projectedPoints];
        }

        var figures = new List<List<Point>>();
        var currentFigure = new List<Point> { projectedPoints[0] };

        for (var i = 1; i < projectedPoints.Count; i++)
        {
            var previous = projectedPoints[i - 1];
            var current = projectedPoints[i];
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            var segmentLength = Math.Sqrt(dx * dx + dy * dy);

            if (segmentLength > MaxPolylineJumpPixels)
            {
                if (currentFigure.Count >= 2)
                {
                    figures.Add(currentFigure);
                }

                currentFigure = [current];
                continue;
            }

            currentFigure.Add(current);
        }

        if (currentFigure.Count >= 2)
        {
            figures.Add(currentFigure);
        }

        return figures;
    }

    /// <summary>
    /// RemoveConsecutiveDuplicates 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static List<Point> RemoveConsecutiveDuplicates(List<Point> points)
    {
        if (points.Count < 2)
        {
            return points;
        }

        var filtered = new List<Point>(points.Count) { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            var previous = filtered[^1];
            var current = points[i];
            if (Math.Abs(previous.X - current.X) < 0.001 &&
                Math.Abs(previous.Y - current.Y) < 0.001)
            {
                continue;
            }

            filtered.Add(current);
        }

        return filtered;
    }

    /// <summary>
    /// ToScreenX 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private double ToScreenX(double x) => transformOffsetX + (x - transformMinX) * transformScale;

    /// <summary>
    /// ToScreenY 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private double ToScreenY(double y) => transformOffsetY + transformContentHeight - (y - transformMinY) * transformScale;

    /// <summary>
    /// IsMapTransformReady 상태 노출
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsMapTransformReady => dwgPolylineCache is { Count: > 0 };

    /// <summary>
    /// public 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public (double GisX, double GisY) ConvertCanvasToGis(double canvasX, double canvasY)
    {
        if (transformScale <= 0) return (0, 0);
        var gisX = transformMinX + (canvasX - transformOffsetX) / transformScale;
        var gisY = transformMinY + (transformContentHeight - (canvasY - transformOffsetY)) / transformScale;
        return (gisX, gisY);
    }

    /// <summary>
    /// GetPreferredResultDirectory 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string GetPreferredResultDirectory()
    {
        var userResultDirectory = GetUserResultDirectory();
        if (Directory.Exists(userResultDirectory))
        {
            return userResultDirectory;
        }

        var legacyUserResultDirectory = GetLegacyUserResultDirectory();
        if (Directory.Exists(legacyUserResultDirectory))
        {
            return legacyUserResultDirectory;
        }

        var runtimeResultDirectory = Path.Combine(AppContext.BaseDirectory, "result");
        if (Directory.Exists(runtimeResultDirectory))
        {
            return runtimeResultDirectory;
        }

        var sampleResultDirectory = Path.Combine(AppContext.BaseDirectory, "sample-data", "results");
        return Directory.Exists(sampleResultDirectory) ? sampleResultDirectory : string.Empty;
    }

    /// <summary>
    /// GetUserResultDirectory 데이터 조회
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string GetUserResultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName,
            "result");

    /// <summary>
    /// GetLegacyUserResultDirectory 데이터 조회
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string GetLegacyUserResultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolderName,
            ProductFolderName,
            "result");

    /// <summary>
    /// TryConvertToLatLon 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public bool TryConvertToLatLon(double gisX, double gisY, out double latitude, out double longitude)
        => CoordinateTransformService.TryConvertProjectedToWgs84(gisX, gisY, out latitude, out longitude);

    /// <summary>
    /// RecomputeMapPoints 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RecomputeMapPoints()
    {
        if (!TryParseDouble(StartPointX, out var startX) ||
            !TryParseDouble(StartPointY, out var startY) ||
            !TryParseDouble(DirectionPointX, out var directionX) ||
            !TryParseDouble(DirectionPointY, out var directionY) ||
            !TryParseDouble(ScanRangeX, out var parsedScanRangeX))
        {
            return;
        }

        var (endX, endY) = SurveyLineProjector.ProjectAlongLine(startX, startY, directionX, directionY, parsedScanRangeX);

        var points = new ObservableCollection<MapPoint>();
        // 각 탐지 거리를 측선 방향 좌표로 투영해 지도 표시점 생성
        foreach (var result in Results)
        {
            var (x, y) = SurveyLineProjector.ProjectAlongLine(startX, startY, directionX, directionY, result.DistanceMeters);
            points.Add(new MapPoint
            {
                Index = result.Index,
                X = x,
                Y = y,
                DepthMeters = result.DepthMeters,
                ConfidenceRatio = result.ConfidenceRatio,
                ScreenX = ToScreenX(x),
                ScreenY = ToScreenY(y)
            });
        }

        SurveyLineX1 = ToScreenX(startX);
        SurveyLineY1 = ToScreenY(startY);
        SurveyLineX2 = ToScreenX(endX);
        SurveyLineY2 = ToScreenY(endY);
        DirectionPreviewX = ToScreenX(directionX);
        DirectionPreviewY = ToScreenY(directionY);
        OnPropertyChanged(nameof(SurveyLineX1));
        OnPropertyChanged(nameof(SurveyLineY1));
        OnPropertyChanged(nameof(SurveyLineX2));
        OnPropertyChanged(nameof(SurveyLineY2));
        OnPropertyChanged(nameof(DirectionPreviewX));
        OnPropertyChanged(nameof(DirectionPreviewY));

        MapPoints = points;
        OnPropertyChanged(nameof(MapPoints));
        RefreshSavedResultPointProjection();
    }

    /// <summary>
    /// RefreshSavedResultPointProjection 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RefreshSavedResultPointProjection()
    {
        var projectedPoints = loadedSavedResultPoints
            .Select(point => new SavedResultProjection
            {
                Key = BuildSavedResultPointKey(point.SourceName, point.Label, point.X, point.Y),
                SourceName = point.SourceName,
                Label = point.Label,
                X = point.X,
                Y = point.Y,
                DepthMeters = point.DepthMeters,
                ScreenX = ToScreenX(point.X),
                ScreenY = ToScreenY(point.Y)
            })
            .ToList();

        // 겹치는 저장 결과 라벨의 오프셋을 미리 계산해 가독성 확보
        var labelOffsets = BuildSavedResultLabelOffsets(projectedPoints);

        var points = projectedPoints
            .Select(point => new SavedResultPoint
            {
                SourceName = point.SourceName,
                Label = point.Label,
                X = point.X,
                Y = point.Y,
                DepthMeters = point.DepthMeters,
                ScreenX = point.ScreenX,
                ScreenY = point.ScreenY,
                LabelOffsetX = labelOffsets.TryGetValue(point.Key, out var offset)
                    ? offset.OffsetX
                    : 12,
                LabelOffsetY = labelOffsets.TryGetValue(point.Key, out offset)
                    ? offset.OffsetY
                    : -8
            });

        SavedResultPoints = new ObservableCollection<SavedResultPoint>(points);
        OnPropertyChanged(nameof(SavedResultPoints));

        SavedResultPolylinePoints = new PointCollection(
            mergedPolylineSourcePoints.Select(point => new Point(ToScreenX(point.X), ToScreenY(point.Y))));
        OnPropertyChanged(nameof(SavedResultPolylinePoints));

        SavedResultPolylineGroups = new ObservableCollection<PointCollection>(
            mergedPolylineGroupsSourcePoints.Select(group =>
                new PointCollection(group.Select(point => new Point(ToScreenX(point.X), ToScreenY(point.Y))))));
        OnPropertyChanged(nameof(SavedResultPolylineGroups));

        SavedResultLineSegments = new ObservableCollection<SavedResultLineSegment>(
            mergedPolylineGroupsSourcePoints.SelectMany(group =>
                group.Zip(group.Skip(1), (from, to) => new SavedResultLineSegment
                {
                    X1 = ToScreenX(from.X),
                    Y1 = ToScreenY(from.Y),
                    X2 = ToScreenX(to.X),
                    Y2 = ToScreenY(to.Y)
                })));
        OnPropertyChanged(nameof(SavedResultLineSegments));
    }

    /// <summary>
    /// BuildSavedResultLabelOffsets 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static Dictionary<string, (double OffsetX, double OffsetY)> BuildSavedResultLabelOffsets(IReadOnlyList<SavedResultProjection> projectedPoints)
    {
        var entries = projectedPoints
            .OrderBy(point => point.ScreenX)
            .ThenBy(point => point.ScreenY)
            .ToList();

        var clusters = new List<List<SavedResultProjection>>();
        foreach (var entry in entries)
        {
            var cluster = clusters.FirstOrDefault(group =>
                group.Any(existing =>
                    Math.Abs(existing.ScreenX - entry.ScreenX) <= 28 &&
                    Math.Abs(existing.ScreenY - entry.ScreenY) <= 28));

            if (cluster is null)
            {
                clusters.Add([entry]);
            }
            else
            {
                cluster.Add(entry);
            }
        }

        var result = new Dictionary<string, (double OffsetX, double OffsetY)>(StringComparer.Ordinal);
        foreach (var cluster in clusters)
        {
            if (cluster.Count == 1)
            {
                result[cluster[0].Key] = (12, -10);
                continue;
            }

            var centerX = cluster.Average(point => point.ScreenX);
            var orderedCluster = cluster
                .OrderBy(point => point.ScreenY)
                .ThenBy(point => point.Label, StringComparer.Ordinal)
                .ToList();

            var isRightSideCluster = centerX >= 400;
            var offsetX = isRightSideCluster ? -58d : 16d;
            var lineGap = 13d;
            var startOffsetY = -((orderedCluster.Count - 1) * lineGap) / 2d;

            for (var index = 0; index < orderedCluster.Count; index++)
            {
                var offsetY = startOffsetY + (index * lineGap);
                result[orderedCluster[index].Key] = (offsetX, offsetY);
            }
        }

        return result;
    }

    /// <summary>
    /// BuildSavedResultPointKey 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string BuildSavedResultPointKey(string sourceName, string label, double x, double y)
        => $"{sourceName}|{label}|{x:0.000000}|{y:0.000000}";

    /// <summary>
    /// SavedResultProjection 관련 상태와 동작 관리
    /// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
    /// </summary>
    private sealed class SavedResultProjection
    {
        /// <summary>
        /// Key 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public string Key { get; init; } = string.Empty;
        /// <summary>
        /// SourceName 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public string SourceName { get; init; } = string.Empty;
        /// <summary>
        /// Label 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public string Label { get; init; } = string.Empty;
        /// <summary>
        /// X 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public double X { get; init; }
        /// <summary>
        /// Y 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public double Y { get; init; }
        /// <summary>
        /// DepthMeters 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public double DepthMeters { get; init; }
        /// <summary>
        /// ScreenX 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public double ScreenX { get; init; }
        /// <summary>
        /// ScreenY 값 제공
        /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
        /// </summary>
        public double ScreenY { get; init; }
    }

    /// <summary>
    /// SelectResultAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task SelectResultAsync(object? parameter)
    {
        if (parameter is not null && int.TryParse(parameter.ToString(), out var index))
        {
            SelectResult(index);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// SelectResult 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SelectResult(int index)
    {
        var result = Results.FirstOrDefault(item => item.Index == index);
        if (result is null)
        {
            selectedResultIndex = 0;
            AnalysisDistance = "0.00";
            AnalysisDepth = "0.00";
            AnalysisConfidence = "0.00";
            ScheduleSessionStateSave();
            return;
        }

        selectedResultIndex = result.Index;
        AnalysisDistance = result.Distance;
        AnalysisDepth = result.Depth;
        AnalysisConfidence = result.Confidence;
        ScheduleSessionStateSave();
    }

    /// <summary>
    /// OpenMapAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task OpenMapAsync()
    {
        RefreshMapEntries();
        await EnsureMapBackgroundReadyAsync();
        windowService.ShowMapDialog(this);
    }

    /// <summary>
    /// OpenPrintAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task OpenPrintAsync()
    {
        windowService.ShowPrintDialog(this);
        return Task.CompletedTask;
    }

    /// <summary>
    /// OpenInputAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async Task OpenInputAsync()
    {
        RefreshMapEntries();
        await EnsureMapBackgroundReadyAsync();
        windowService.ShowInputDialog(this);
    }

    /// <summary>
    /// OpenCommandAsync 대상 열기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task OpenCommandAsync()
    {
        RefreshMapEntries();
        windowService.ShowCommand(this);
        return Task.CompletedTask;
    }

    /// <summary>
    /// OpenManualAsync 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task OpenManualAsync()
    {
        var manualPath = Path.Combine(AppContext.BaseDirectory, "manual", "manual.pdf");
        if (!File.Exists(manualPath))
        {
            userDialogService.ShowMessage(
                "manual/manual.pdf file was not found.",
                "Manual",
                UserMessageKind.Information);
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = manualPath,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// OpenResultFolderAsync 대상 열기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Task OpenResultFolderAsync()
    {
        var resultDirectory = GetUserResultDirectory();
        Directory.CreateDirectory(resultDirectory);

        Process.Start(new ProcessStartInfo
        {
            FileName = resultDirectory,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// CanRunAlgorithm 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool CanRunAlgorithm()
        => !string.IsNullOrWhiteSpace(ScanFilePath) &&
           !string.IsNullOrWhiteSpace(AlgorithmDirectory) &&
           !string.IsNullOrWhiteSpace(PythonExecutable) &&
           !IsAlgorithmRunning;

    /// <summary>
    /// TryBuildRequest 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool TryBuildRequest(out AlgorithmRunRequest request)
    {
        request = default!;
        if (!TryResolveScanFilePath(out var resolvedScanFilePath))
        {
            userDialogService.ShowMessage(
                "스캔 파일을 찾을 수 없습니다. 원본 DZT/SGY/CSV 파일을 다시 선택해주세요.",
                "입력 확인",
                UserMessageKind.Information);
            return false;
        }

        if (IsAlgorithmTransientPath(resolvedScanFilePath))
        {
            userDialogService.ShowMessage(
                "알고리즘 작업 폴더의 data/results 파일은 실행 중 정리될 수 있습니다. 카카오톡 받은 파일, 문서, 바탕화면 등 원본 위치의 스캔 파일을 선택해주세요.",
                "입력 확인",
                UserMessageKind.Information);
            return false;
        }

        ApplyRecommendedXScale(resolvedScanFilePath);
        ApplyRecommendedThreshold(resolvedScanFilePath);

        if (!TryParseDouble(ScanRangeX, out var parsedScanRangeX) ||
            !TryParseDouble(ScanRangeY, out var parsedScanRangeY) ||
            !TryParseDouble(XScale, out var parsedXScale) ||
            !TryParseDouble(YScale, out var parsedYScale) ||
            !TryParseDouble(Threshold, out var parsedThreshold) ||
            !TryParseDouble(TdaThreshold, out var parsedTdaThreshold))
        {
            userDialogService.ShowMessage(
                "스캔 범위, 스케일, 신뢰도 값을 숫자로 입력해야 합니다.",
                "입력 확인",
                UserMessageKind.Information);
            return false;
        }

        if (parsedScanRangeX <= 0 ||
            parsedScanRangeY <= 0 ||
            parsedXScale <= 0 ||
            parsedYScale <= 0 ||
            parsedThreshold is < 0 or > 1 ||
            parsedTdaThreshold is < 0 or > 1)
        {
            userDialogService.ShowMessage(
                "측정 범위와 스케일은 0보다 커야 하고, Threshold는 0~1 사이여야 합니다.",
                "입력 확인",
                UserMessageKind.Information);
            return false;
        }

        if (!Directory.Exists(AlgorithmDirectory))
        {
            userDialogService.ShowMessage(
                "알고리즘 폴더를 찾을 수 없습니다.",
                "입력 확인",
                UserMessageKind.Information);
            return false;
        }

        if (!TryParseDouble(StartPointX, out var startX) ||
            !TryParseDouble(StartPointY, out var startY) ||
            !TryParseDouble(DirectionPointX, out var directionX) ||
            !TryParseDouble(DirectionPointY, out var directionY) ||
            Math.Abs(directionX - startX) < 1e-9 && Math.Abs(directionY - startY) < 1e-9)
        {
            userDialogService.ShowMessage(
                "측선 시작점과 방향점은 유효한 숫자이며 서로 다른 좌표여야 합니다.",
                "입력 확인",
                UserMessageKind.Information);
            return false;
        }

        request = new AlgorithmRunRequest(
            resolvedScanFilePath,
            AlgorithmDirectory,
            PythonExecutable,
            parsedScanRangeX,
            parsedScanRangeY,
            parsedXScale,
            parsedYScale,
            parsedThreshold,
            UseTda,
            parsedTdaThreshold,
            null);

        return true;
    }

    /// <summary>
    /// TryResolveScanFilePath 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool TryResolveScanFilePath(out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(ScanFilePath))
        {
            return false;
        }

        if (File.Exists(ScanFilePath) && !IsAlgorithmTransientPath(ScanFilePath))
        {
            resolvedPath = ScanFilePath;
            ApplyRecommendedXScale(resolvedPath);
            ApplyRecommendedThreshold(resolvedPath);
            return true;
        }

        var fileName = Path.GetFileName(ScanFilePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var candidate = EnumerateScanFileSearchDirectories(ScanFilePath)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        resolvedPath = candidate;
        if (!resolvedPath.Equals(ScanFilePath, StringComparison.OrdinalIgnoreCase))
        {
            ScanFilePath = resolvedPath;
            AppendLog($"스캔 파일 경로 자동 복구: {resolvedPath}");
        }

        ApplyRecommendedXScale(resolvedPath);
        ApplyRecommendedThreshold(resolvedPath);

        return true;
    }

    /// <summary>
    /// ApplyRecommendedXScale 설정 반영
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ApplyRecommendedXScale(string scanFilePath)
    {
        if (!TryResolveRecommendedXScale(scanFilePath, out var recommendedXScale))
        {
            return;
        }

        var nextValue = recommendedXScale.ToString(CultureInfo.InvariantCulture);
        if (string.Equals(XScale, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        XScale = nextValue;
        AppendLog($"X Scale 자동 보정: {Path.GetFileName(scanFilePath)} -> {nextValue}");
    }

    /// <summary>
    /// ApplyRecommendedThreshold 설정 반영
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ApplyRecommendedThreshold(string scanFilePath)
    {
        var nextValue = ResolveRecommendedThreshold(scanFilePath);
        if (string.Equals(Threshold, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        Threshold = nextValue;
        AppendLog($"Threshold auto adjust: {Path.GetFileName(scanFilePath)} -> {nextValue}");
    }

    /// <summary>
    /// ResolveRecommendedThreshold 실행 값 결정
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ResolveRecommendedThreshold(string scanFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(scanFilePath).ToUpperInvariant();
        var isHSeries = Regex.IsMatch(name, @"(^|[^A-Z0-9])H[-_ ]?\d+[A-Z]?(?:[-_ ]\d+)?(?:\s*\(?500\)?)?([^A-Z0-9]|$)");
        return isHSeries ? "0.35" : "0.5";
    }

    /// <summary>
    /// TryResolveRecommendedXScale 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryResolveRecommendedXScale(string scanFilePath, out double xScale)
    {
        var name = Path.GetFileNameWithoutExtension(scanFilePath).ToUpperInvariant();
        var isAb = ContainsToken(name, "AB");
        var isCd = ContainsToken(name, "CD");
        var isG = ContainsSeriesToken(name, "G");
        var isH = ContainsSeriesToken(name, "H");

        if (isAb && isH)
        {
            xScale = 26;
            return true;
        }

        if (isAb && isG)
        {
            xScale = 6;
            return true;
        }

        if (isCd && isH)
        {
            xScale = 4;
            return true;
        }

        if (isCd && isG)
        {
            xScale = 13;
            return true;
        }

        if (Regex.IsMatch(name, @"(^|[^A-Z0-9])H[-_ ]?\d+(?:[-_ ]\d+)?\s*\(?500\)?([^A-Z0-9]|$)"))
        {
            xScale = 26;
            return true;
        }

        if (Regex.IsMatch(name, @"(^|[^A-Z0-9])H[-_ ]?\d+[A-Z]?([^A-Z0-9]|$)"))
        {
            xScale = 4;
            return true;
        }

        xScale = 0;
        return false;
    }

    /// <summary>
    /// ContainsToken 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool ContainsToken(string name, string token)
    {
        return Regex.IsMatch(name, $@"(^|[^A-Z0-9]){Regex.Escape(token)}([^A-Z0-9]|$)");
    }

    /// <summary>
    /// ContainsSeriesToken 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool ContainsSeriesToken(string name, string token)
    {
        return Regex.IsMatch(name, $@"(^|[^A-Z0-9]){Regex.Escape(token)}([0-9]|$|[^A-Z0-9])");
    }

    /// <summary>
    /// EnumerateScanFileSearchDirectories 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static IEnumerable<string> EnumerateScanFileSearchDirectories(string previousPath)
    {
        var directories = new List<string>();
        AddDirectory(Path.GetDirectoryName(previousPath));

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        AddDirectory(Path.Combine(documents, "카카오톡 받은 파일"));
        AddDirectory(documents);
        AddDirectory(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

        return directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        void AddDirectory(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !IsAlgorithmTransientPath(path))
            {
                directories.Add(path);
            }
        }
    }

    /// <summary>
    /// IsAlgorithmTransientPath 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsAlgorithmTransientPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalized.Contains($"{Path.DirectorySeparatorChar}algorithm{Path.DirectorySeparatorChar}data", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{Path.DirectorySeparatorChar}algorithm-work{Path.DirectorySeparatorChar}data", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{Path.DirectorySeparatorChar}algorithm{Path.DirectorySeparatorChar}results", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"{Path.DirectorySeparatorChar}algorithm-work{Path.DirectorySeparatorChar}results", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// TryParseDouble 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryParseDouble(string value, out double result)
    {
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                     double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        return parsed && double.IsFinite(result);
    }

    /// <summary>
    /// AppendLog 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void AppendLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var builder = new StringBuilder(LogText);
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine(message.TrimEnd());
        if (builder.Length > MaximumLogCharacters)
        {
            var removeLength = builder.Length - MaximumLogCharacters;
            var nextLine = builder.ToString().IndexOf('\n', removeLength);
            builder.Remove(0, nextLine >= 0 ? nextLine + 1 : removeLength);
        }
        LogText = builder.ToString();
    }

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
        clockTimer.Stop();
        sessionSaveTimer.Stop();
        PropertyChanged -= OnViewModelPropertyChanged;
        algorithmRunCancellation?.Cancel();
        algorithmRunCancellation?.Dispose();
        algorithmRunCancellation = null;
        GC.SuppressFinalize(this);
    }
}

    /// <summary>
    /// MapRenderFigure 관련 상태와 동작 관리
    /// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
    /// </summary>
    internal sealed record MapRenderFigure(
    IReadOnlyList<Point> Points,
    Rect Bounds);


