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
using GprPrediction.Wpf.Models;
using GprPrediction.Wpf.Services;
using GprPrediction.Wpf.Windows;
using Microsoft.Win32;

namespace GprPrediction.Wpf.ViewModels;

/// <summary>
//
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

    private readonly AlgorithmRunner algorithmRunner = new();
    private readonly PredictionResultReader resultReader = new();
    private readonly SavedResultReader savedResultReader = new();
    private readonly SavedResultWriter savedResultWriter = new();
    private readonly AppSessionStateStore sessionStateStore = new();
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
//
    public MainViewModel()
    {
        BrowseScanFileCommand = new RelayCommand(_ => BrowseScanFileAsync());
        BrowseAlgorithmDirectoryCommand = new RelayCommand(_ => BrowseAlgorithmDirectoryAsync());
        BrowsePythonExecutableCommand = new RelayCommand(_ => BrowsePythonExecutableAsync());
        SelectMapCommand = new RelayCommand(SelectMapAsync);
        AddMapCommand = new RelayCommand(_ => AddMapAsync());
        BrowseResultCsvCommand = new RelayCommand(_ => BrowseResultCsvAsync());
        RunAlgorithmCommand = new RelayCommand(_ => RunAlgorithmAsync(), _ => CanRunAlgorithm());
        CancelAlgorithmCommand = new RelayCommand(_ => CancelAlgorithmAsync(), _ => IsAlgorithmRunning);
        ResetAnalysisCommand = new RelayCommand(_ =>
        {
            ResetAnalysisState();
            return Task.CompletedTask;
        }, _ => !IsAlgorithmRunning);
        OpenMapCommand = new RelayCommand(_ => OpenMapAsync());
        OpenPrintCommand = new RelayCommand(_ => OpenPrintAsync());
        OpenCommandCommand = new RelayCommand(_ => OpenCommandAsync());
        OpenInputCommand = new RelayCommand(_ => OpenInputAsync());
        OpenManualCommand = new RelayCommand(_ => OpenManualAsync());
        OpenResultFolderCommand = new RelayCommand(_ => OpenResultFolderAsync());
        SelectResultCommand = new RelayCommand(SelectResultAsync);

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
//
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
//
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
//
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
//
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
//
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
//
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
//
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

    public RelayCommand BrowseScanFileCommand { get; }

    public RelayCommand BrowseAlgorithmDirectoryCommand { get; }

    public RelayCommand BrowsePythonExecutableCommand { get; }

    public RelayCommand SelectMapCommand { get; }

    public RelayCommand AddMapCommand { get; }

    public RelayCommand BrowseResultCsvCommand { get; }

    public RelayCommand RunAlgorithmCommand { get; }

    public RelayCommand CancelAlgorithmCommand { get; }

    public RelayCommand ResetAnalysisCommand { get; }

    public RelayCommand OpenMapCommand { get; }

    public RelayCommand OpenPrintCommand { get; }

    public RelayCommand OpenCommandCommand { get; }

    public RelayCommand OpenInputCommand { get; }

    public RelayCommand OpenManualCommand { get; }

    public RelayCommand OpenResultFolderCommand { get; }

    public RelayCommand SelectResultCommand { get; }

    public ObservableCollection<PredictionResult> Results { get; private set; } = new();

    public ObservableCollection<MapPoint> MapPoints { get; private set; } = new();

    public ObservableCollection<SavedResultPoint> SavedResultPoints { get; private set; } = new();

    public PointCollection SavedResultPolylinePoints { get; private set; } = new();

    public ObservableCollection<PointCollection> SavedResultPolylineGroups { get; private set; } = [];

    public ObservableCollection<SavedResultLineSegment> SavedResultLineSegments { get; private set; } = [];

    public ObservableCollection<MapEntry> MapEntries { get; } = new();

    public double SurveyLineX1 { get; private set; }

    public double SurveyLineY1 { get; private set; }

    public double SurveyLineX2 { get; private set; }

    public double SurveyLineY2 { get; private set; }

    public double DirectionPreviewX { get; private set; }

    public double DirectionPreviewY { get; private set; }

    public ImageSource? MapBackgroundImage
    {
        get => mapBackgroundImage;
        private set => SetProperty(ref mapBackgroundImage, value);
    }

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

    public bool IsMapLoading
    {
        get => isMapLoading;
        private set => SetProperty(ref isMapLoading, value);
    }

    public string MapLoadingText
    {
        get => mapLoadingText;
        private set => SetProperty(ref mapLoadingText, value);
    }

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

    public string ScanFileDisplayName
        => string.IsNullOrWhiteSpace(scanFilePath)
            ? string.Empty
            : Path.GetFileName(scanFilePath);

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

    public string ScanRangeY
    {
        get => scanRangeY;
        set => SetProperty(ref scanRangeY, value);
    }

    public string XScale
    {
        get => xScale;
        set => SetProperty(ref xScale, value);
    }

    public string YScale
    {
        get => yScale;
        set => SetProperty(ref yScale, value);
    }

    public string Threshold
    {
        get => threshold;
        set => SetProperty(ref threshold, value);
    }

    public bool UseTda
    {
        get => useTda;
        set => SetProperty(ref useTda, value);
    }

    public string TdaThreshold
    {
        get => tdaThreshold;
        set => SetProperty(ref tdaThreshold, value);
    }

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

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string LogText
    {
        get => logText;
        set => SetProperty(ref logText, value);
    }

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

    public string AlgorithmRunMessage
    {
        get => algorithmRunMessage;
        private set => SetProperty(ref algorithmRunMessage, value);
    }

    /// <summary>
    /// 명령 창에서 원문 진행 로그를 누락 없이 표시하기 위한 최신 로그와 순번
    /// </summary>
    public string AlgorithmLogLine
    {
        get => algorithmLogLine;
        private set => SetProperty(ref algorithmLogLine, value);
    }

    public long AlgorithmLogSequence
    {
        get => algorithmLogSequence;
        private set => SetProperty(ref algorithmLogSequence, value);
    }

    public string LastAlgorithmResultText
    {
        get => lastAlgorithmResultText;
        private set => SetProperty(ref lastAlgorithmResultText, value);
    }

    public bool IsLastAlgorithmResultVisible
    {
        get => isLastAlgorithmResultVisible;
        private set => SetProperty(ref isLastAlgorithmResultVisible, value);
    }

    public string DigitalClockText
    {
        get => digitalClockText;
        set => SetProperty(ref digitalClockText, value);
    }

    public string TodayText
    {
        get => todayText;
        set => SetProperty(ref todayText, value);
    }

    public string BuildInfoText => buildInfoText;

    public string CoordinateReferenceText => CoordinateTransformService.CoordinateReferenceText;

    public string LoadedSavedResultText
    {
        get => loadedSavedResultText;
        private set => SetProperty(ref loadedSavedResultText, value);
    }

    public string AnalysisDistance
    {
        get => analysisDistance;
        set => SetProperty(ref analysisDistance, value);
    }

    public string AnalysisDepth
    {
        get => analysisDepth;
        set => SetProperty(ref analysisDepth, value);
    }

    public string AnalysisConfidence
    {
        get => analysisConfidence;
        set => SetProperty(ref analysisConfidence, value);
    }

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

    public bool HasAnalysisImage => analysisImage is not null;

    /// <summary>
//
    public void PersistSessionState()
    {
        if (!isSessionStateReady)
        {
            return;
        }

        FlushSessionState();
    }

    /// <summary>
//
    /// </summary>
    public void ShowTransientMapLoading(string text = "캐싱 중...")
    {
        transientMapLoadingText = string.IsNullOrWhiteSpace(text) ? "캐싱 중..." : text;
        isTransientMapLoading = true;
        RefreshMapLoadingOverlay();
    }

    /// <summary>
//
    public void HideTransientMapLoading()
    {
        isTransientMapLoading = false;
        transientMapLoadingText = string.Empty;
        RefreshMapLoadingOverlay();
    }

    /// <summary>
//
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
//
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
//
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
//
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
//
    /// </summary>
    public IReadOnlyList<string> GetOpenedSavedResultFiles()
        => openedSavedResultFiles;

    /// <summary>
//
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
//
    /// </summary>
    public string? GetPreferredResultCsvPath()
    {
        return FindExistingResultCsvCandidates().FirstOrDefault();
    }

    /// <summary>
//
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
//
    /// </summary>
    public async Task LoadMergedSavedResultsAsync(IEnumerable<string> senPaths)
    {
        var combined = new List<SavedResultPoint>();
        var loadedNames = new List<string>();
        var loadedPaths = new List<string>();

//
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
//
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
//
    public Task LoadMergedSelectionRowsAsync(IEnumerable<IReadOnlyList<SavedResultPoint>> rows)
    {
        var rowList = rows
            .Where(row => row.Count > 0)
            .Select(row => (IReadOnlyList<SavedResultPoint>)row.ToList())
            .ToList();

//
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
//
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
//
    /// </summary>
    private static int GetMapSortGroup(string path)
        => int.TryParse(Path.GetFileNameWithoutExtension(path), out _) ? 0 : 1;

    /// <summary>
//
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
//
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
//
    private Task BrowseScanFileAsync()
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(ScanFilePath) && File.Exists(ScanFilePath)
            ? Path.GetDirectoryName(ScanFilePath) ?? string.Empty
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (IsAlgorithmTransientPath(initialDirectory) || !Directory.Exists(initialDirectory))
        {
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var dialog = new OpenFileDialog
        {
            Filter = "GPR scan files (*.dzt;*.sgy;*.csv)|*.dzt;*.sgy;*.csv|All files (*.*)|*.*",
            InitialDirectory = initialDirectory,
            Title = "스캔 파일 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            ScanFilePath = dialog.FileName;
            ApplyRecommendedXScale(dialog.FileName);
            ApplyRecommendedThreshold(dialog.FileName);
        }

        return Task.CompletedTask;
    }

    /// <summary>
//
    /// </summary>
    private Task BrowseAlgorithmDirectoryAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "알고리즘 폴더 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            AlgorithmDirectory = dialog.FolderName;
        }

        return Task.CompletedTask;
    }

    /// <summary>
//
    /// </summary>
    private Task BrowsePythonExecutableAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Python executable (python.exe)|python.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Python 실행 파일 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            PythonExecutable = dialog.FileName;
        }

        return Task.CompletedTask;
    }

    /// <summary>
//
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
//
    /// </summary>
    private Task AddMapAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DWG files (*.dwg)|*.dwg|All files (*.*)|*.*",
            Title = "배경 지도 DWG 추가"
        };

        if (dialog.ShowDialog() == true)
        {
            if (!addedMapPaths.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase))
            {
                addedMapPaths.Add(dialog.FileName);
            }

            var entry = new MapEntry
            {
                Name = Path.GetFileNameWithoutExtension(dialog.FileName),
                FilePath = dialog.FileName
            };

            MapEntries.Add(entry);
            SelectMap(entry);
            ScheduleSessionStateSave();
        }

        return Task.CompletedTask;
    }

    /// <summary>
//
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
//
    /// </summary>
    private async Task BrowseResultCsvAsync()
    {
        var resultDirectory = GetPreferredResultDirectory();
        var dialog = new OpenFileDialog
        {
            Filter = "결과 파일 (*.sen;*.csv)|*.sen;*.csv|저장 결과 (*.sen)|*.sen|결과 CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "결과 파일 선택",
            InitialDirectory = resultDirectory,
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPaths = dialog.FileNames;
            var hasCsv = selectedPaths.Any(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase));
            if (!hasCsv)
            {
                await LoadMergedSavedResultsAsync(selectedPaths);
                AppendLog($"저장 결과 SEN: {string.Join(", ", selectedPaths.Select(Path.GetFileName))}");
            }
            else
            {
                if (selectedPaths.Length > 1)
                {
                    CustomMessageBox.Show("CSV는 한 번에 하나만 열 수 있습니다. 첫 번째 CSV를 불러옵니다.", "결과 파일 선택", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                var csvPath = selectedPaths.First(path => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase));
                await LoadResultsAsync(csvPath);
            }
        }
    }

    /// <summary>
//
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
                CustomMessageBox.Show(
                    string.IsNullOrWhiteSpace(failureDetail) ? "알고리즘 실행 중 오류가 발생했습니다." : failureDetail.Trim(),
                    "알고리즘 실행 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
                CustomMessageBox.Show(
                    $"분석이 완료되었습니다.\n결과 {Results.Count}건을 불러왔습니다.",
                    "분석 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                CustomMessageBox.Show(
                    "분석은 완료되었지만 탐지된 결과가 없습니다.\n이전 결과를 표시하지 않도록 화면을 비웠습니다.\n\n입력 파일, Threshold, 모델 설정을 확인해주세요.",
                    "탐지 결과 없음",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
            CustomMessageBox.Show("분석을 취소했습니다.", "분석 취소", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = "준비됨";
            LastAlgorithmResultText = "분석 오류";
            IsLastAlgorithmResultVisible = true;
            IsAlgorithmRunning = false;
            AlgorithmRunMessage = string.Empty;
            AppendLog(ex.ToString());
            CustomMessageBox.Show(ex.Message, "알고리즘 실행 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
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
//
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
//
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
//
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
//
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
//
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

    private void ClearCurrentRunReport()
    {
        currentSavedSenPath = string.Empty;
        currentSavedCsvPath = string.Empty;
        currentSavedAnalysisImagePath = string.Empty;
        currentSavedInputInfoPath = string.Empty;
        currentRunTdaApplied = false;
    }

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
//
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
//
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
//
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
//
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
//
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
//
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
//
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
//
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
//
    private double ToScreenX(double x) => transformOffsetX + (x - transformMinX) * transformScale;

    /// <summary>
//
    private double ToScreenY(double y) => transformOffsetY + transformContentHeight - (y - transformMinY) * transformScale;

    public bool IsMapTransformReady => dwgPolylineCache is { Count: > 0 };

    /// <summary>
//
    public (double GisX, double GisY) ConvertCanvasToGis(double canvasX, double canvasY)
    {
        if (transformScale <= 0) return (0, 0);
        var gisX = transformMinX + (canvasX - transformOffsetX) / transformScale;
        var gisY = transformMinY + (transformContentHeight - (canvasY - transformOffsetY)) / transformScale;
        return (gisX, gisY);
    }

    /// <summary>
//
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

    private static string GetUserResultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName,
            "result");

    private static string GetLegacyUserResultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolderName,
            ProductFolderName,
            "result");

    /// <summary>
//
    /// </summary>
    public bool TryConvertToLatLon(double gisX, double gisY, out double latitude, out double longitude)
        => CoordinateTransformService.TryConvertProjectedToWgs84(gisX, gisY, out latitude, out longitude);

    /// <summary>
//
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
//
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
//
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

//
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
//
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
//
    /// </summary>
    private static string BuildSavedResultPointKey(string sourceName, string label, double x, double y)
        => $"{sourceName}|{label}|{x:0.000000}|{y:0.000000}";

    /// <summary>
//
    /// </summary>
    private sealed class SavedResultProjection
    {
        public string Key { get; init; } = string.Empty;
        public string SourceName { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public double X { get; init; }
        public double Y { get; init; }
        public double DepthMeters { get; init; }
        public double ScreenX { get; init; }
        public double ScreenY { get; init; }
    }

    /// <summary>
//
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
//
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
//
    /// </summary>
    private async Task OpenMapAsync()
    {
        RefreshMapEntries();
        await EnsureMapBackgroundReadyAsync();

        var window = new MapViewWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = this
        };

        window.ShowDialog();
    }

    /// <summary>
//
    /// </summary>
    private Task OpenPrintAsync()
    {
        var window = new PrintWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = this
        };

        window.ShowDialog();
        return Task.CompletedTask;
    }

    /// <summary>
//
    /// </summary>
    private async Task OpenInputAsync()
    {
        RefreshMapEntries();
        await EnsureMapBackgroundReadyAsync();

        var window = new InputWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = this
        };

        window.ShowDialog();
    }

    private Task OpenCommandAsync()
    {
        RefreshMapEntries();

        var window = new CommandWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = this
        };

        window.Show();
        return Task.CompletedTask;
    }

    /// <summary>
//
    /// </summary>
    private Task OpenManualAsync()
    {
        var manualPath = Path.Combine(AppContext.BaseDirectory, "manual", "manual.pdf");
        if (!File.Exists(manualPath))
        {
            CustomMessageBox.Show("manual/manual.pdf file was not found.", "Manual", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = manualPath,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

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
//
    private bool CanRunAlgorithm()
        => !string.IsNullOrWhiteSpace(ScanFilePath) &&
           !string.IsNullOrWhiteSpace(AlgorithmDirectory) &&
           !string.IsNullOrWhiteSpace(PythonExecutable) &&
           !IsAlgorithmRunning;

    /// <summary>
//
    /// </summary>
    private bool TryBuildRequest(out AlgorithmRunRequest request)
    {
        request = default!;
        if (!TryResolveScanFilePath(out var resolvedScanFilePath))
        {
            CustomMessageBox.Show("스캔 파일을 찾을 수 없습니다. 원본 DZT/SGY/CSV 파일을 다시 선택해주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (IsAlgorithmTransientPath(resolvedScanFilePath))
        {
            CustomMessageBox.Show("알고리즘 작업 폴더의 data/results 파일은 실행 중 정리될 수 있습니다. 카카오톡 받은 파일, 문서, 바탕화면 등 원본 위치의 스캔 파일을 선택해주세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information);
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
            CustomMessageBox.Show("스캔 범위, 스케일, 신뢰도 값을 숫자로 입력해야 합니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (parsedScanRangeX <= 0 ||
            parsedScanRangeY <= 0 ||
            parsedXScale <= 0 ||
            parsedYScale <= 0 ||
            parsedThreshold is < 0 or > 1 ||
            parsedTdaThreshold is < 0 or > 1)
        {
            CustomMessageBox.Show(
                "측정 범위와 스케일은 0보다 커야 하고, Threshold는 0~1 사이여야 합니다.",
                "입력 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (!Directory.Exists(AlgorithmDirectory))
        {
            CustomMessageBox.Show("알고리즘 폴더를 찾을 수 없습니다.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!TryParseDouble(StartPointX, out var startX) ||
            !TryParseDouble(StartPointY, out var startY) ||
            !TryParseDouble(DirectionPointX, out var directionX) ||
            !TryParseDouble(DirectionPointY, out var directionY) ||
            Math.Abs(directionX - startX) < 1e-9 && Math.Abs(directionY - startY) < 1e-9)
        {
            CustomMessageBox.Show(
                "측선 시작점과 방향점은 유효한 숫자이며 서로 다른 좌표여야 합니다.",
                "입력 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    private static string ResolveRecommendedThreshold(string scanFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(scanFilePath).ToUpperInvariant();
        var isHSeries = Regex.IsMatch(name, @"(^|[^A-Z0-9])H[-_ ]?\d+[A-Z]?(?:[-_ ]\d+)?(?:\s*\(?500\)?)?([^A-Z0-9]|$)");
        return isHSeries ? "0.35" : "0.5";
    }

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

    private static bool ContainsToken(string name, string token)
    {
        return Regex.IsMatch(name, $@"(^|[^A-Z0-9]){Regex.Escape(token)}([^A-Z0-9]|$)");
    }

    private static bool ContainsSeriesToken(string name, string token)
    {
        return Regex.IsMatch(name, $@"(^|[^A-Z0-9]){Regex.Escape(token)}([0-9]|$|[^A-Z0-9])");
    }

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
//
    /// </summary>
    private static bool TryParseDouble(string value, out double result)
    {
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                     double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        return parsed && double.IsFinite(result);
    }

    /// <summary>
//
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
//
    /// </summary>
    internal sealed record MapRenderFigure(
    IReadOnlyList<Point> Points,
    Rect Bounds);


