using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// 스캔 파일, 측선 시작점/방향점, 범위 파라미터를 조정하는 자료 입력 창
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class InputWindow : Window
{
    /// <summary>
    /// 지도 클릭으로 선택 중인 좌표 종류
    /// 허용 상태를 제한해 분기 기준의 일관성 확보
    /// </summary>
    private enum PickMode { None, StartPoint, DirectionPoint }

    private const double PreviewWidth = 800;
    private const double PreviewHeight = 580;
    private const double MinScale = 0.35;
    private const double MaxScale = 24;

    public static readonly DependencyProperty InverseMapScaleProperty =
        DependencyProperty.Register(
            nameof(InverseMapScale), typeof(double), typeof(InputWindow), new PropertyMetadata(1.0));

    public static readonly DependencyProperty SurveyLineStrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(SurveyLineStrokeThickness), typeof(double), typeof(InputWindow), new PropertyMetadata(3.4));

    private readonly ScaleTransform scaleTransform = new();
    private readonly TranslateTransform panTransform = new();
    private readonly DispatcherTimer mapBitmapRefreshTimer = new();
    private readonly MatrixTransform backgroundPreviewTransform = new();
    private PickMode currentPickMode = PickMode.None;
    private Point dragStart;
    private bool isPanning;
    private bool isPanArmed;
    private bool hasUserNavigated;
    private bool showLoadingOnRefresh;
    private int mapBitmapRefreshRequestVersion;
    private double committedScale = 1;
    private double committedPanX;
    private double committedPanY;
    private MainViewModel? subscribedViewModel;
    private bool isConsoleTrackingAlgorithm;
    private string lastConsoleAlgorithmMessage = string.Empty;

    /// <summary>
    /// InverseMapScale 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double InverseMapScale
    {
        get => (double)GetValue(InverseMapScaleProperty);
        private set => SetValue(InverseMapScaleProperty, value);
    }

    /// <summary>
    /// SurveyLineStrokeThickness 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double SurveyLineStrokeThickness
    {
        get => (double)GetValue(SurveyLineStrokeThicknessProperty);
        private set => SetValue(SurveyLineStrokeThicknessProperty, value);
    }

    /// <summary>
    /// 자료 입력 창을 초기화하고 맵 상호작용 및 타이머를 연결
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public InputWindow()
    {
        InitializeComponent();

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(scaleTransform);
        transformGroup.Children.Add(panTransform);
        MapCanvasRoot.RenderTransform = transformGroup;
        BackgroundImageLayer.RenderTransform = backgroundPreviewTransform;

        ConfigurePickButtons();

        mapBitmapRefreshTimer.Interval = TimeSpan.FromMilliseconds(120);
        mapBitmapRefreshTimer.Tick += MapBitmapRefreshTimer_Tick;

        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;

        Loaded += (_, _) =>
        {
            QueueMapViewReset(showLoading: true);
            RefreshPointCoordinateTexts();
            WriteCommandOutput("명령 입력 준비. 사용 가능한 명령은 help로 확인.");
        };

        SizeChanged += (_, _) =>
        {
            if (!hasUserNavigated)
            {
                ResetMapView(showLoading: true);
            }
            else
            {
                ScheduleMapBitmapRefresh(showLoading: true);
            }
        };
    }

    /// <summary>
    /// ExecuteCommand_Click 명령 실행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ExecuteCommand_Click(object sender, RoutedEventArgs e)
    {
        ExecuteConsoleCommand();
    }

    /// <summary>
    /// CommandInputTextBox_KeyDown 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CommandInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ExecuteConsoleCommand();
    }

    /// <summary>
    /// ExecuteConsoleCommand 명령 실행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ExecuteConsoleCommand()
    {
        var commandText = CommandInputTextBox.Text.Trim();
        if (commandText.Length == 0 || DataContext is not MainViewModel vm)
        {
            return;
        }

        CommandInputTextBox.Clear();
        WriteCommandOutput($"> {commandText}");

        try
        {
            var tokens = TokenizeCommand(commandText);
            if (tokens.Count == 0)
            {
                return;
            }

            var command = tokens[0].ToLowerInvariant();
            switch (command)
            {
                case "help":
                    WriteCommandOutput(
                        "show | clear | file <경로> | map <이름> | map browse\n" +
                        "start <X> <Y> | direction <X> <Y>\n" +
                        "range <X> <Z> | scale <X> <Z>\n" +
                        "threshold <값> | tda-threshold <값> | tda on|off\n" +
                        "run");
                    break;
                case "clear":
                    CommandOutputTextBox.Clear();
                    break;
                case "show":
                    WriteCommandOutput(
                        $"파일: {vm.ScanFilePath}\n맵: {vm.MapDwgPath}\n" +
                        $"시작점: {vm.StartPointX}, {vm.StartPointY}\n" +
                        $"방향점: {vm.DirectionPointX}, {vm.DirectionPointY}\n" +
                        $"범위 X/Z: {vm.ScanRangeX}/{vm.ScanRangeY}, Scale X/Z: {vm.XScale}/{vm.YScale}\n" +
                        $"신뢰도: {vm.Threshold}, TDA: {(vm.UseTda ? "on" : "off")}, TDA threshold: {vm.TdaThreshold}");
                    break;
                case "file":
                case "scan":
                    Require(tokens, 2, "file <스캔 파일 경로>");
                    var scanPath = System.IO.Path.GetFullPath(string.Join(" ", tokens.Skip(1)));
                    if (!File.Exists(scanPath))
                    {
                        throw new FileNotFoundException("스캔 파일을 찾을 수 없습니다.", scanPath);
                    }
                    vm.ScanFilePath = scanPath;
                    WriteCommandOutput($"스캔 파일 선택: {scanPath}");
                    break;
                case "map":
                    Require(tokens, 2, "map <목록 이름> 또는 map browse");
                    var mapName = string.Join(" ", tokens.Skip(1));
                    if (mapName.Equals("browse", StringComparison.OrdinalIgnoreCase))
                    {
                        vm.AddMapCommand.Execute(null);
                        WriteCommandOutput("맵 찾아보기 창을 열었습니다.");
                        break;
                    }
                    var map = vm.MapEntries.FirstOrDefault(item =>
                        item.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
                    if (map is null)
                    {
                        throw new ArgumentException($"맵 목록에서 '{mapName}'을 찾을 수 없습니다.");
                    }
                    vm.SelectMapCommand.Execute(map);
                    WriteCommandOutput($"맵 선택: {map.Name}");
                    break;
                case "start":
                    SetPair(tokens, "start <X> <Y>", (x, y) =>
                    {
                        vm.StartPointX = FormatNumber(x);
                        vm.StartPointY = FormatNumber(y);
                    });
                    WriteCommandOutput($"시작점: {vm.StartPointX}, {vm.StartPointY}");
                    break;
                case "direction":
                case "end":
                    SetPair(tokens, "direction <X> <Y>", (x, y) =>
                    {
                        vm.DirectionPointX = FormatNumber(x);
                        vm.DirectionPointY = FormatNumber(y);
                    });
                    WriteCommandOutput($"방향점: {vm.DirectionPointX}, {vm.DirectionPointY}");
                    break;
                case "range":
                    SetPair(tokens, "range <X> <Z>", (x, z) =>
                    {
                        vm.ScanRangeX = FormatNumber(x);
                        vm.ScanRangeY = FormatNumber(z);
                    });
                    WriteCommandOutput($"측정 범위 X/Z: {vm.ScanRangeX}, {vm.ScanRangeY}");
                    break;
                case "scale":
                    SetPair(tokens, "scale <X> <Z>", (x, z) =>
                    {
                        vm.XScale = FormatNumber(x);
                        vm.YScale = FormatNumber(z);
                    });
                    WriteCommandOutput($"Scale X/Z: {vm.XScale}, {vm.YScale}");
                    break;
                case "threshold":
                    vm.Threshold = FormatNumber(ParseSingle(tokens, "threshold <값>"));
                    WriteCommandOutput($"신뢰도 Threshold: {vm.Threshold}");
                    break;
                case "tda-threshold":
                    vm.TdaThreshold = FormatNumber(ParseSingle(tokens, "tda-threshold <값>"));
                    WriteCommandOutput($"TDA Threshold: {vm.TdaThreshold}");
                    break;
                case "tda":
                    Require(tokens, 2, "tda on 또는 tda off");
                    vm.UseTda = tokens[1].ToLowerInvariant() switch
                    {
                        "on" or "true" or "1" => true,
                        "off" or "false" or "0" => false,
                        _ => throw new ArgumentException("tda 값은 on 또는 off만 사용할 수 있습니다.")
                    };
                    WriteCommandOutput($"TDA 전처리: {(vm.UseTda ? "on" : "off")}");
                    break;
                case "run":
                case "analyze":
                    if (!vm.RunAlgorithmCommand.CanExecute(null))
                    {
                        WriteCommandOutput("분석을 시작할 수 없습니다. 입력값과 현재 실행 상태를 확인하세요.");
                        break;
                    }

                    isConsoleTrackingAlgorithm = true;
                    lastConsoleAlgorithmMessage = string.Empty;
                    WriteCommandOutput("현재 입력값으로 분석을 시작합니다.");
                    vm.RunAlgorithmCommand.Execute(null);
                    break;
                default:
                    WriteCommandOutput($"알 수 없는 명령: {tokens[0]}. help를 입력해 확인하세요.");
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteCommandOutput($"오류: {ex.Message}");
        }
    }

    /// <summary>
    /// WriteCommandOutput 데이터 기록
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void WriteCommandOutput(string message)
    {
        if (CommandOutputTextBox.Text.Length > 0)
        {
            CommandOutputTextBox.AppendText(Environment.NewLine);
        }

        CommandOutputTextBox.AppendText(message);
        CommandOutputTextBox.ScrollToEnd();
    }

    /// <summary>
    /// Require 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void Require(IReadOnlyList<string> tokens, int count, string usage)
    {
        if (tokens.Count < count)
        {
            throw new ArgumentException($"사용법: {usage}");
        }
    }

    /// <summary>
    /// ParseSingle 입력 구문 분석
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static double ParseSingle(IReadOnlyList<string> tokens, string usage)
    {
        Require(tokens, 2, usage);
        return ParseNumber(tokens[1]);
    }

    /// <summary>
    /// SetPair 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void SetPair(
        IReadOnlyList<string> tokens,
        string usage,
        Action<double, double> setter)
    {
        Require(tokens, 3, usage);
        setter(ParseNumber(tokens[1]), ParseNumber(tokens[2]));
    }

    /// <summary>
    /// ParseNumber 입력 구문 분석
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static double ParseNumber(string value)
    {
        if ((double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
             double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)) &&
            double.IsFinite(result))
        {
            return result;
        }

        throw new ArgumentException($"숫자 형식이 아닙니다: {value}");
    }

    /// <summary>
    /// FormatNumber 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string FormatNumber(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// TokenizeCommand 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static IReadOnlyList<string> TokenizeCommand(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            throw new ArgumentException("닫는 따옴표가 없습니다.");
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// 타이머와 이벤트 구독을 해제해 창 종료 후 리소스가 남지 않게
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        mapBitmapRefreshTimer.Stop();
        mapBitmapRefreshRequestVersion++;

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.HideTransientMapLoading();
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            subscribedViewModel = null;
        }
    }

    /// <summary>
    /// 시작점과 방향점 선택 버튼의 표시 내용을 공통 형식으로 구성
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ConfigurePickButtons()
    {
        BtnPickStart.Content = BuildPickButtonContent("시작점", (Brush)FindResource("DangerBrush"));
        BtnPickDirection.Content = BuildPickButtonContent("방향점", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D4AA")!));
    }

    /// <summary>
    /// 원형 마커와 라벨을 묶은 버튼 내용을 생성
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static object BuildPickButtonContent(string label, Brush accentBrush)
    {
        return new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Children =
            {
                new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = accentBrush,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new System.Windows.Controls.TextBlock
                {
                    Text = label,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            }
        };
    }

    /// <summary>
    /// ViewModel 교체 시 속성 변경 이벤트를 다시 연결
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            subscribedViewModel = null;
        }

        if (e.NewValue is MainViewModel vm)
        {
            subscribedViewModel = vm;
            subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
            RefreshPointCoordinateTexts();
            UpdateCursorCoordinateText(Mouse.GetPosition(MapSurface));
            QueueMapViewReset(showLoading: true);
        }
        else
        {
            ResetCursorCoordinateTexts();
            RefreshPointCoordinateTexts();
        }
    }

    /// <summary>
    /// 지도나 좌표 관련 속성 변경 시 입력 창 오버레이를 갱신
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isConsoleTrackingAlgorithm &&
            e.PropertyName == nameof(MainViewModel.AlgorithmRunMessage) &&
            sender is MainViewModel progressViewModel)
        {
            var message = progressViewModel.AlgorithmRunMessage.Trim();
            if (message.Length > 0 &&
                !message.Equals(lastConsoleAlgorithmMessage, StringComparison.Ordinal))
            {
                lastConsoleAlgorithmMessage = message;
                Dispatcher.BeginInvoke(
                    new Action(() => WriteCommandOutput(message)),
                    DispatcherPriority.Background);
            }
        }

        if (isConsoleTrackingAlgorithm &&
            e.PropertyName == nameof(MainViewModel.IsAlgorithmRunning) &&
            sender is MainViewModel completedViewModel &&
            !completedViewModel.IsAlgorithmRunning)
        {
            isConsoleTrackingAlgorithm = false;
            lastConsoleAlgorithmMessage = string.Empty;

            var result = string.IsNullOrWhiteSpace(completedViewModel.LastAlgorithmResultText)
                ? "분석이 종료되었습니다."
                : completedViewModel.LastAlgorithmResultText;
            Dispatcher.BeginInvoke(
                new Action(() => WriteCommandOutput(result)),
                DispatcherPriority.Background);
        }

        if (e.PropertyName is nameof(MainViewModel.StartPointX) or
            nameof(MainViewModel.StartPointY) or
            nameof(MainViewModel.DirectionPointX) or
            nameof(MainViewModel.DirectionPointY))
        {
            Dispatcher.Invoke(RefreshPointCoordinateTexts);
        }

        if (e.PropertyName is nameof(MainViewModel.MapDwgPath) or
            nameof(MainViewModel.IsMapTransformReady))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.PropertyName == nameof(MainViewModel.MapDwgPath))
                {
                    hasUserNavigated = false;
                }

                if (!hasUserNavigated)
                {
                    ResetMapView(showLoading: true);
                }
                else
                {
                    ScheduleMapBitmapRefresh(showLoading: true);
                }

                RefreshPointCoordinateTexts();
            }), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 레이아웃 완료 뒤 입력 지도 뷰와 비트맵 캐시를 맞춤
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void QueueMapViewReset(bool showLoading)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ResetMapView(showLoading);

            if (DataContext is MainViewModel { IsMapTransformReady: true } && MapSurface.ActualWidth > 1 && MapSurface.ActualHeight > 1)
            {
                ScheduleMapBitmapRefresh(showLoading);
            }
        }), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 사용자 정의 타이틀바의 최소화 명령을 처리
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// 사용자 정의 타이틀바의 최대화 또는 복원 명령을 처리
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MaximizeRestoreWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// 사용자 정의 타이틀바의 닫기 명령을 처리
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
        => Close();

    /// <summary>
    /// 지도 클릭으로 측정 시작점을 지정하는 모드로 전환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void BtnPickStart_Click(object sender, RoutedEventArgs e)
    {
        var newMode = currentPickMode == PickMode.StartPoint ? PickMode.None : PickMode.StartPoint;
        SetPickMode(newMode);
    }

    /// <summary>
    /// 지도 클릭으로 방향점을 지정하는 모드로 전환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void BtnPickDirection_Click(object sender, RoutedEventArgs e)
    {
        var newMode = currentPickMode == PickMode.DirectionPoint ? PickMode.None : PickMode.DirectionPoint;
        SetPickMode(newMode);
    }

    /// <summary>
    /// 자료 입력 지도 영역의 확대와 이동 상태를 초기화
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ResetMapView_Click(object sender, RoutedEventArgs e)
    {
        hasUserNavigated = false;
        ResetMapView(showLoading: true);
    }

    /// <summary>
    /// 점 선택 또는 패닝 시작 처리를 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MapSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsMapTransformReady)
        {
            return;
        }

        var surfacePosition = e.GetPosition(MapSurface);
        var canvasPosition = ConvertSurfaceToCanvas(surfacePosition);
        if (!IsInsideCanvas(canvasPosition))
        {
            return;
        }

        if (currentPickMode == PickMode.None)
        {
            isPanArmed = true;
            isPanning = false;
            dragStart = surfacePosition;
            MapSurface.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (TryApplyPickedPoint(vm, surfacePosition, canvasPosition))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 우클릭으로 현재 점 선택 모드를 취소
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MapSurface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (currentPickMode == PickMode.None || DataContext is not MainViewModel vm || !vm.IsMapTransformReady)
        {
            return;
        }

        var surfacePosition = e.GetPosition(MapSurface);
        var canvasPosition = ConvertSurfaceToCanvas(surfacePosition);
        if (TryApplyPickedPoint(vm, surfacePosition, canvasPosition))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 패닝 또는 점 선택의 종료 처리를 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MapSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        isPanning = false;
        isPanArmed = false;
        MapSurface.ReleaseMouseCapture();
    }

    /// <summary>
    /// 패닝을 적용하고 커서 좌표를 계속 갱신
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MapSurface_MouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(MapSurface);
        UpdateCursorCoordinateText(current);

        if (currentPickMode != PickMode.None)
        {
            return;
        }

        if (isPanArmed && !isPanning)
        {
            var delta = current - dragStart;
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
            {
                return;
            }

            isPanning = true;
        }

        if (!isPanning)
        {
            return;
        }

        panTransform.X += current.X - dragStart.X;
        panTransform.Y += current.Y - dragStart.Y;
        dragStart = current;
        hasUserNavigated = true;
        UpdateBitmapPreviewTransform();
        ScheduleMapBitmapRefresh();
    }

    /// <summary>
    /// 지도 영역을 벗어나면 커서와 드래그 상태를 정리
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MapSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!MapSurface.IsMouseCaptured)
        {
            isPanning = false;
            isPanArmed = false;
        }

        ResetCursorCoordinateTexts();
    }

    /// <summary>
    /// 마우스 위치 기준으로 자료 입력 지도 뷰를 확대 또는 축소
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MapSurface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (currentPickMode != PickMode.None)
        {
            return;
        }

        var cursor = e.GetPosition(MapSurface);
        var factor = e.Delta > 0 ? 1.4 : 1 / 1.4;
        var newScale = Math.Clamp(scaleTransform.ScaleX * factor, MinScale, MaxScale);

        var canvasX = (cursor.X - panTransform.X) / scaleTransform.ScaleX;
        var canvasY = (cursor.Y - panTransform.Y) / scaleTransform.ScaleY;

        scaleTransform.ScaleX = newScale;
        scaleTransform.ScaleY = newScale;
        panTransform.X = cursor.X - (canvasX * newScale);
        panTransform.Y = cursor.Y - (canvasY * newScale);
        UpdateOverlayScaleMetrics(newScale);

        hasUserNavigated = true;
        UpdateBitmapPreviewTransform();
        ScheduleMapBitmapRefresh(showLoading: true);
        UpdateCursorCoordinateText(cursor);
        e.Handled = true;
    }

    /// <summary>
    /// 현재 뷰포트 기준 배경 비트맵 재생성을 지연 예약
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ScheduleMapBitmapRefresh(bool showLoading = false)
    {
        showLoadingOnRefresh |= showLoading;
        mapBitmapRefreshRequestVersion++;

        if (showLoading && DataContext is MainViewModel viewModel)
        {
            viewModel.ShowTransientMapLoading("캐싱중...");
        }

        mapBitmapRefreshTimer.Stop();
        mapBitmapRefreshTimer.Start();
    }

    /// <summary>
    /// 예약된 비트맵 재생성 작업을 실제로 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private async void MapBitmapRefreshTimer_Tick(object? sender, EventArgs e)
    {
        mapBitmapRefreshTimer.Stop();
        var requestVersion = mapBitmapRefreshRequestVersion;

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var applied = await viewModel.RefreshMapBitmapForViewportAsync(
                scaleTransform.ScaleX,
                panTransform.X,
                panTransform.Y,
                MapSurface.ActualWidth,
                MapSurface.ActualHeight);
            if (!applied || requestVersion != mapBitmapRefreshRequestVersion)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            viewModel.ReportNonFatalError("입력 지도 비트맵 갱신", ex);
            return;
        }
        finally
        {
            if (showLoadingOnRefresh && requestVersion == mapBitmapRefreshRequestVersion)
            {
                viewModel.HideTransientMapLoading();
                showLoadingOnRefresh = false;
            }
        }

        committedScale = scaleTransform.ScaleX;
        committedPanX = panTransform.X;
        committedPanY = panTransform.Y;
        backgroundPreviewTransform.Matrix = Matrix.Identity;
    }

    /// <summary>
    /// 배경 비트맵 미리보기 변환을 현재 뷰 상태에 맞춤
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void UpdateBitmapPreviewTransform()
    {
        if (committedScale <= 0)
        {
            backgroundPreviewTransform.Matrix = Matrix.Identity;
            return;
        }

        var deltaScale = scaleTransform.ScaleX / committedScale;
        var matrix = Matrix.Identity;
        matrix.Scale(deltaScale, deltaScale);
        matrix.Translate(
            panTransform.X - (deltaScale * committedPanX),
            panTransform.Y - (deltaScale * committedPanY));
        backgroundPreviewTransform.Matrix = matrix;
    }

    /// <summary>
    /// 시작점/방향점 선택 모드와 버튼 강조 상태를 전환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SetPickMode(PickMode mode)
    {
        currentPickMode = mode;
        MapSurface.Cursor = mode != PickMode.None ? Cursors.Cross : Cursors.SizeAll;

        var accentBrush = (Brush)FindResource("AccentBrush");
        var dangerBrush = (Brush)FindResource("DangerBrush");
        var surfaceBrush = (Brush)FindResource("SurfaceAltBrush");

        BtnPickStart.Background = mode == PickMode.StartPoint ? dangerBrush : surfaceBrush;
        BtnPickDirection.Background = mode == PickMode.DirectionPoint ? accentBrush : surfaceBrush;

        if (mode == PickMode.None)
        {
            PickModeOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            PickModeOverlay.Visibility = Visibility.Visible;
            PickModeText.Text = mode == PickMode.StartPoint
                ? "지도를 클릭하여 시작점을 선택하세요. (다시 누르면 취소)"
                : "지도를 클릭하여 방향점을 선택하세요. (다시 누르면 취소)";
        }
    }

    /// <summary>
    /// 클릭한 지도 좌표를 시작점 또는 방향점 입력값으로 반영
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool TryApplyPickedPoint(MainViewModel vm, Point surfacePosition, Point canvasPosition)
    {
        if (!IsInsideCanvas(canvasPosition) || currentPickMode == PickMode.None)
        {
            return false;
        }

        // 클릭한 캔버스 좌표를 현재 맵 변환 기준의 실제 GIS 좌표로 되돌리기
        var (gisX, gisY) = vm.ConvertCanvasToGis(canvasPosition.X, canvasPosition.Y);
        var xStr = gisX.ToString("F4", CultureInfo.InvariantCulture);
        var yStr = gisY.ToString("F4", CultureInfo.InvariantCulture);

        if (currentPickMode == PickMode.StartPoint)
        {
            vm.StartPointX = xStr;
            vm.StartPointY = yStr;
        }
        else
        {
            vm.DirectionPointX = xStr;
            vm.DirectionPointY = yStr;
        }

        SetPickMode(PickMode.None);
        UpdateCursorCoordinateText(surfacePosition);
        return true;
    }

    /// <summary>
    /// 캔버스 전체 또는 측선 초점 기준으로 입력 지도 뷰를 재배치
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ResetMapView(bool showLoading = false)
    {
        if (MapSurface.ActualWidth <= 1 || MapSurface.ActualHeight <= 1)
        {
            return;
        }

        if (!TryFitToSurveyFocus())
        {
            FitToCanvasBounds();
        }

        committedScale = scaleTransform.ScaleX;
        committedPanX = panTransform.X;
        committedPanY = panTransform.Y;
        backgroundPreviewTransform.Matrix = Matrix.Identity;
        UpdateCursorCoordinateText(Mouse.GetPosition(MapSurface));

        if (showLoading)
        {
            ScheduleMapBitmapRefresh(showLoading: true);
        }
    }

    /// <summary>
    /// 측선 시작점과 방향점을 화면 중심으로 맞추는 배율과 위치를 계산
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool TryFitToSurveyFocus()
    {
        if (DataContext is not MainViewModel vm)
        {
            return false;
        }

        // 시작점과 방향점을 모두 보이게 감싸는 영역을 만든 뒤 여백을 포함해 확대 배율을 계산
        var start = new Point(vm.SurveyLineX1, vm.SurveyLineY1);
        var direction = new Point(vm.DirectionPreviewX, vm.DirectionPreviewY);
        if (!IsCanvasPointValid(start) || !IsCanvasPointValid(direction))
        {
            return false;
        }

        var minX = Math.Min(start.X, direction.X);
        var maxX = Math.Max(start.X, direction.X);
        var minY = Math.Min(start.Y, direction.Y);
        var maxY = Math.Max(start.Y, direction.Y);

        var focusWidth = Math.Max(maxX - minX, 80);
        var focusHeight = Math.Max(maxY - minY, 80);
        var padding = Math.Min(Math.Min(MapSurface.ActualWidth, MapSurface.ActualHeight) * 0.08, 64);
        var availableWidth = Math.Max(MapSurface.ActualWidth - (padding * 2), 80);
        var availableHeight = Math.Max(MapSurface.ActualHeight - (padding * 2), 80);
        var targetScale = Math.Clamp(Math.Min(availableWidth / focusWidth, availableHeight / focusHeight) * 1.12, MinScale, MaxScale);

        ApplyViewTransform(
            targetScale,
            (MapSurface.ActualWidth / 2) - (((minX + maxX) / 2) * targetScale),
            (MapSurface.ActualHeight / 2) - (((minY + maxY) / 2) * targetScale));
        return true;
    }

    /// <summary>
    /// 입력 지도 전체가 보이도록 기본 배율을 계산해 적용
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void FitToCanvasBounds()
    {
        var scale = Math.Clamp(
            Math.Min(MapSurface.ActualWidth / PreviewWidth, MapSurface.ActualHeight / PreviewHeight),
            MinScale,
            MaxScale);

        ApplyViewTransform(
            scale,
            (MapSurface.ActualWidth - (PreviewWidth * scale)) / 2,
            (MapSurface.ActualHeight - (PreviewHeight * scale)) / 2);
    }

    /// <summary>
    /// 확대/이동 Transform과 오버레이 보정값을 함께 반영
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ApplyViewTransform(double scale, double panX, double panY)
    {
        scaleTransform.ScaleX = scale;
        scaleTransform.ScaleY = scale;
        panTransform.X = panX;
        panTransform.Y = panY;
        UpdateOverlayScaleMetrics(scale);
    }

    /// <summary>
    /// 줌 수준에 따라 측선과 라벨 표시 두께를 조정
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void UpdateOverlayScaleMetrics(double scale)
    {
        var safeScale = scale <= 0 ? 1.0 : scale;
        InverseMapScale = 1.0 / safeScale;
        SurveyLineStrokeThickness = 3.4 / safeScale;
    }

    /// <summary>
    /// 화면 좌표를 내부 캔버스 좌표로 변환
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private Point ConvertSurfaceToCanvas(Point surfacePoint)
    {
        var scale = scaleTransform.ScaleX <= 0 ? 1 : scaleTransform.ScaleX;
        return new Point(
            (surfacePoint.X - panTransform.X) / scale,
            (surfacePoint.Y - panTransform.Y) / scale);
    }

    /// <summary>
    /// 좌표가 캔버스 범위 안에 있는지 검사
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsInsideCanvas(Point point)
        => point.X >= 0 && point.X <= PreviewWidth && point.Y >= 0 && point.Y <= PreviewHeight;

    /// <summary>
    /// 좌표가 렌더링 가능한 유효값인지 검사
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsCanvasPointValid(Point point)
        => !double.IsNaN(point.X) &&
           !double.IsNaN(point.Y) &&
           !double.IsInfinity(point.X) &&
           !double.IsInfinity(point.Y) &&
           IsInsideCanvas(point);

    /// <summary>
    /// 마우스 위치의 투영 좌표와 위경도 표시를 갱신
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void UpdateCursorCoordinateText(Point surfacePosition)
    {
        if (DataContext is not MainViewModel vm || !vm.IsMapTransformReady)
        {
            ResetCursorCoordinateTexts();
            return;
        }

        var canvasPosition = ConvertSurfaceToCanvas(surfacePosition);
        if (!IsInsideCanvas(canvasPosition))
        {
            ResetCursorCoordinateTexts();
            return;
        }

        var (gisX, gisY) = vm.ConvertCanvasToGis(canvasPosition.X, canvasPosition.Y);
        CursorCoordinateText.Text = $"커서  X : {gisX:F4}    Y : {gisY:F4}";
        CursorGeographicText.Text = FormatLatLonLine("커서", vm, gisX, gisY);
    }

    /// <summary>
    /// 커서 좌표 표시 문자열을 기본 상태로 초기화
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ResetCursorCoordinateTexts()
    {
        CursorCoordinateText.Text = "커서  X : -    Y : -";
        CursorGeographicText.Text = "커서  위도 : -    경도 : -";
    }

    /// <summary>
    /// 현재 시작점과 방향점의 좌표 문자열을 다시 계산
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RefreshPointCoordinateTexts()
    {
        if (DataContext is not MainViewModel vm)
        {
            StartCoordinateText.Text = "시작점  X : -    Y : -";
            StartGeographicText.Text = "시작점  위도 : -    경도 : -";
            DirectionCoordinateText.Text = "방향점  X : -    Y : -";
            DirectionGeographicText.Text = "방향점  위도 : -    경도 : -";
            return;
        }

        StartCoordinateText.Text = FormatProjectedLine("시작점", vm.StartPointX, vm.StartPointY);
        DirectionCoordinateText.Text = FormatProjectedLine("방향점", vm.DirectionPointX, vm.DirectionPointY);

        StartGeographicText.Text = TryParsePoint(vm.StartPointX, vm.StartPointY, out var startX, out var startY)
            ? FormatLatLonLine("시작점", vm, startX, startY)
            : "시작점  위도 : -    경도 : -";

        DirectionGeographicText.Text = TryParsePoint(vm.DirectionPointX, vm.DirectionPointY, out var directionX, out var directionY)
            ? FormatLatLonLine("방향점", vm, directionX, directionY)
            : "방향점  위도 : -    경도 : -";
    }

    /// <summary>
    /// 문자열 X/Y 좌표를 숫자로 안전하게 파싱
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryParsePoint(string xText, string yText, out double x, out double y)
    {
        x = 0;
        y = 0;
        return double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
               double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
               double.IsFinite(x) &&
               double.IsFinite(y);
    }

    /// <summary>
    /// 투영 좌표 한 줄을 화면 표시 형식으로 만들기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string FormatProjectedLine(string label, string xText, string yText)
        => TryParsePoint(xText, yText, out var x, out var y)
            ? $"{label}  X : {x:F4}    Y : {y:F4}"
            : $"{label}  X : -    Y : -";

    /// <summary>
    /// 투영 좌표를 위경도로 변환해 보조 문자열로 만들기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string FormatLatLonLine(string label, MainViewModel vm, double x, double y)
        => vm.TryConvertToLatLon(x, y, out var latitude, out var longitude)
            ? $"{label}  위도 : {latitude:F6}    경도 : {longitude:F6}"
            : $"{label}  위도 : -    경도 : -";
}
