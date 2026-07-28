using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.ComponentModel;
using GprPrediction.Wpf.Models;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf;

/// <summary>
/// 메인 분석 화면을 표시하고 Top View와 Front View의 상호작용 및 렌더링을 담당
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 개별 뷰 캔버스의 확대, 이동, 드래그 상태를 묶어 관리
    /// </summary>
    private sealed class ViewInteractionState
    {
        public required FrameworkElement Surface { get; init; }
        public required Canvas Canvas { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required TranslateTransform Pan { get; init; }
        public Point DragStart { get; set; }
        public bool IsPanning { get; set; }
    }

    private static readonly Color[] RankColors =
    [
        Color.FromRgb(0xFF, 0x5B, 0x8A),
        Color.FromRgb(0xFF, 0x9B, 0x54),
        Color.FromRgb(0xFF, 0xD2, 0x54),
        Color.FromRgb(0x61, 0xE8, 0x7B),
        Color.FromRgb(0x66, 0xC2, 0xFF),
        Color.FromRgb(0xD1, 0x8B, 0xFF),
    ];

    private readonly Dictionary<FrameworkElement, ViewInteractionState> viewStates = [];
    private MainViewModel? subscribedViewModel;

    /// <summary>
    /// 메인 화면과 ViewModel을 초기화하고 보조 뷰 상호작용을 연결
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        DataContext = new MainViewModel();
        InitializeViewInteraction(TopViewSurface, TopViewCanvas);
        InitializeViewInteraction(FrontViewSurface, FrontViewCanvas);
    }

    /// <summary>
    /// 확대와 패닝에 필요한 RenderTransform과 상태 객체를 준비
    /// </summary>
    private void InitializeViewInteraction(FrameworkElement surface, Canvas canvas)
    {
        var scale = new ScaleTransform(1, 1);
        var pan = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(pan);
        canvas.RenderTransform = group;

        viewStates[surface] = new ViewInteractionState
        {
            Surface = surface,
            Canvas = canvas,
            Scale = scale,
            Pan = pan
        };
    }

    /// <summary>
    /// 결과나 측선 정보 변경 시 보조 뷰를 다시 그리도록 ViewModel 이벤트를 구독
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        subscribedViewModel = e.NewValue as MainViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Results)
            or nameof(MainViewModel.ScanRangeX)
            or nameof(MainViewModel.ScanRangeY)
            or nameof(MainViewModel.StartPointX)
            or nameof(MainViewModel.StartPointY)
            or nameof(MainViewModel.DirectionPointX)
            or nameof(MainViewModel.DirectionPointY))
        {
            Dispatcher.BeginInvoke(RedrawViews);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            subscribedViewModel.Dispose();
            subscribedViewModel = null;
        }
    }

    /// <summary>
    /// 사용자 정의 타이틀바의 최소화 명령을 처리
    /// </summary>
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// 사용자 정의 타이틀바의 최대화 또는 복원 명령을 처리
    /// </summary>
    private void MaximizeRestoreWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// 사용자 정의 타이틀바의 닫기 명령을 처리
    /// </summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
        => Close();

    /// <summary>
    /// 캔버스 크기가 바뀌면 보조 뷰를 현재 크기에 맞춰 다시 그리기
    /// </summary>
    private void ViewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RedrawViews();

    /// <summary>
    /// 드래그 패닝 시작 지점을 기록하고 마우스를 캡처
    /// </summary>
    private void ViewSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement surface || !viewStates.TryGetValue(surface, out var state))
        {
            return;
        }

        state.IsPanning = true;
        state.DragStart = e.GetPosition(surface);
        surface.CaptureMouse();
    }

    /// <summary>
    /// 드래그 중인 거리만큼 뷰를 평행 이동
    /// </summary>
    private void ViewSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement surface || !viewStates.TryGetValue(surface, out var state) || !state.IsPanning)
        {
            return;
        }

        var current = e.GetPosition(surface);
        state.Pan.X += current.X - state.DragStart.X;
        state.Pan.Y += current.Y - state.DragStart.Y;
        state.DragStart = current;
    }

    /// <summary>
    /// 패닝 드래그를 종료하고 마우스 캡처를 해제
    /// </summary>
    private void ViewSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement surface || !viewStates.TryGetValue(surface, out var state))
        {
            return;
        }

        state.IsPanning = false;
        surface.ReleaseMouseCapture();
    }

    /// <summary>
    /// 뷰 영역을 벗어나면 패닝 상태를 정리
    /// </summary>
    private void ViewSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement surface || !viewStates.TryGetValue(surface, out var state))
        {
            return;
        }

        state.IsPanning = false;
        surface.ReleaseMouseCapture();
    }

    /// <summary>
    /// 마우스 위치를 기준으로 Top/Front 뷰를 확대 또는 축소
    /// </summary>
    private void ViewSurface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement surface || !viewStates.TryGetValue(surface, out var state))
        {
            return;
        }

        var cursor = e.GetPosition(surface);
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(state.Scale.ScaleX * factor, 0.7, 6.5);

        var canvasX = (cursor.X - state.Pan.X) / state.Scale.ScaleX;
        var canvasY = (cursor.Y - state.Pan.Y) / state.Scale.ScaleY;

        state.Scale.ScaleX = newScale;
        state.Scale.ScaleY = newScale;
        state.Pan.X = cursor.X - (canvasX * newScale);
        state.Pan.Y = cursor.Y - (canvasY * newScale);
        e.Handled = true;
    }

    /// <summary>
    /// Top View의 확대와 이동 상태를 초기화
    /// </summary>
    private void ResetTopView_Click(object sender, RoutedEventArgs e)
        => ResetViewState(TopViewSurface);

    /// <summary>
    /// Front View의 확대와 이동 상태를 초기화
    /// </summary>
    private void ResetFrontView_Click(object sender, RoutedEventArgs e)
        => ResetViewState(FrontViewSurface);

    /// <summary>
    /// 지정한 뷰의 패닝/줌 상태를 기본값으로 되돌리기
    /// </summary>
    private void ResetViewState(FrameworkElement surface)
    {
        if (!viewStates.TryGetValue(surface, out var state))
        {
            return;
        }

        state.IsPanning = false;
        state.Surface.ReleaseMouseCapture();
        state.Scale.ScaleX = 1;
        state.Scale.ScaleY = 1;
        state.Pan.X = 0;
        state.Pan.Y = 0;
    }

    /// <summary>
    /// Top View와 Front View를 현재 결과 기준으로 다시 렌더링
    /// </summary>
    private void RedrawViews()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        RedrawTopViewWithSurveyAngle(vm);
        RedrawFrontView(vm);
    }

    /// <summary>
    /// 거리 결과를 측선 방향과 직교 방향 기준으로 Top View에 그리기
    /// </summary>
    private void RedrawTopViewWithSurveyAngle(MainViewModel vm)
    {
        TopViewCanvas.Children.Clear();
        UpdateRankedOverlay(TopViewRankOverlay, [], _ => string.Empty, "신뢰도 높은 순 (거리)");

        if (vm.Results.Count == 0 || !TryGetRanges(vm, out var rangeX, out _))
        {
            return;
        }

        var width = TopViewCanvas.ActualWidth;
        var height = TopViewCanvas.ActualHeight;
        if (width < 120 || height < 120)
        {
            return;
        }

        const double padLeft = 58;
        const double padRight = 58;
        const double padTop = 42;
        const double padBottom = 78;
        var drawWidth = width - padLeft - padRight;
        var drawHeight = height - padTop - padBottom;
        if (drawWidth < 80 || drawHeight < 80)
        {
            return;
        }

        var rankedResults = vm.Results
            .OrderByDescending(r => r.ConfidenceRatio)
            .ToList();

        // 실제 측정 범위보다 결과가 몰린 구간을 우선 보여주기 위해 가시 범위를 다시 계산
        var visibleRangeX = Math.Max(rangeX, 1);
        var isZoomedView = false;
        var direction = GetTopViewDirection(vm);
        if (direction.Y < 0)
        {
            direction *= -1;
        }

        var normalizedDirection = direction;
        normalizedDirection.Normalize();
        var normal = new Vector(-normalizedDirection.Y, normalizedDirection.X);
        normal.Normalize();

        var lineScale = Math.Min(
            (drawWidth * 0.28) / Math.Max(Math.Abs(normalizedDirection.X) * visibleRangeX, 0.0001),
            (drawHeight * 0.80) / Math.Max(Math.Abs(normalizedDirection.Y) * visibleRangeX, 0.0001));
        lineScale = Math.Max(lineScale, 1);

        var startPoint = new Point(
            padLeft + (drawWidth * 0.36),
            padTop + 18);
        var endPoint = new Point(
            startPoint.X + (normalizedDirection.X * visibleRangeX * lineScale),
            startPoint.Y + (normalizedDirection.Y * visibleRangeX * lineScale));

        var mainLineOffset = normal * -16;
        var resultAxisOffset = normal * 26;
        var startMain = startPoint + mainLineOffset;
        var endMain = endPoint + mainLineOffset;
        var startResultAxis = startPoint + resultAxisOffset;
        var endResultAxis = endPoint + resultAxisOffset;

        DrawSolidGuideLine(TopViewCanvas, startMain, endMain, "#5EA0F2", 4.2);
        DrawBaselineDirectional(TopViewCanvas, startPoint.X, startPoint.Y, endPoint.X, endPoint.Y);
        DrawSolidGuideLine(TopViewCanvas, startResultAxis, endResultAxis, "#B8C2D6", 1.3);
        DrawDirectionArrow(TopViewCanvas, startPoint, endPoint, "#5EA0F2");
        DrawDistanceBracket(TopViewCanvas, startMain, endMain, normal * -18, $"{visibleRangeX:0.##}m");

        AddCanvasText(TopViewCanvas, "측정시작점", startPoint.X, startPoint.Y, (normal.X * 42) - 10, (normal.Y * 42) - 14, "#00E0B2", 12, FontWeights.Bold);
        AddCanvasText(TopViewCanvas, "측정종점", endPoint.X, endPoint.Y, (normal.X * 36) - 18, (normal.Y * 36) - 2, "#00E0B2", 11, FontWeights.Bold);
        if (isZoomedView)
        {
            AddCanvasText(
                TopViewCanvas,
                $"확대 표시 0 ~ {visibleRangeX:0.##}m / 전체 {rangeX:0.##}m",
                width - 10,
                10,
                -220,
                0,
                "#A8B3C6",
                10,
                FontWeights.SemiBold);
        }
        else
        {
            AddCanvasText(
                TopViewCanvas,
                $"전체 측정범위 {rangeX:0.##}m",
                width - 10,
                10,
                -120,
                0,
                "#A8B3C6",
                10,
                FontWeights.SemiBold);
        }

        for (var i = 0; i < rankedResults.Count; i++)
        {
            var result = rankedResults[i];
            var color = GetRankColor(i);
            // 결과 점은 측선 방향 벡터를 따라 거리만큼 이동한 위치에 놓기
            var distance = Math.Min(Math.Max(result.DistanceMeters, 0), visibleRangeX) * lineScale;
            var axisPoint = new Point(
                startResultAxis.X + (normalizedDirection.X * distance),
                startResultAxis.Y + (normalizedDirection.Y * distance));

            AddPoint(TopViewCanvas, axisPoint.X, axisPoint.Y, color, 9);
            AddCanvasText(
                TopViewCanvas,
                $"{result.Index:00}(#{result.SourceIndex:00})",
                axisPoint.X,
                axisPoint.Y,
                10,
                -11 + ((i % 2) * 16),
                color,
                11,
                FontWeights.Bold);
        }

        UpdateRankedOverlay(TopViewRankOverlay, rankedResults, r => $"{r.DistanceMeters:0.00} m", "신뢰도 높은 순 (거리)");
    }

    /// <summary>
    /// 거리와 심도 결과를 Front View 프로파일 형태로 그리기
    /// </summary>
    private void RedrawFrontView(MainViewModel vm)
    {
        FrontViewCanvas.Children.Clear();
        UpdateRankedOverlay(FrontViewRankOverlay, [], _ => string.Empty, "신뢰도 높은 순 (심도)");

        if (vm.Results.Count == 0 || !TryGetRanges(vm, out var rangeX, out var rangeY))
        {
            return;
        }

        var width = FrontViewCanvas.ActualWidth;
        var height = FrontViewCanvas.ActualHeight;
        if (width < 120 || height < 120)
        {
            return;
        }

        const double padLeft = 44;
        const double padRight = 44;
        const double padTop = 34;
        const double padBottom = 68;
        var drawWidth = width - padLeft - padRight;
        var drawHeight = height - padTop - padBottom;
        if (drawWidth < 80 || drawHeight < 80)
        {
            return;
        }

        var rankedResults = vm.Results
            .OrderByDescending(r => r.ConfidenceRatio)
            .ToList();

        var visibleRangeX = Math.Max(rangeX, 1);
        var visibleRangeY = Math.Max(rangeY, 0.8);
        var isZoomedView = false;

        double ToScreenX(double distance) => padLeft + (Math.Min(distance, visibleRangeX) / visibleRangeX) * drawWidth;
        double ToScreenY(double depth) => padTop + (depth / visibleRangeY) * drawHeight;

        var groundY = padTop;
        DrawBaseline(FrontViewCanvas, padLeft, padLeft + drawWidth, groundY);
        AddCanvasText(FrontViewCanvas, "측정시작점", padLeft, groundY, -4, -22, "#00E0B2", 12, FontWeights.Bold);
        AddCanvasText(FrontViewCanvas, "측정종점", padLeft + drawWidth, groundY, -38, -22, "#00E0B2", 11, FontWeights.Bold);
        AddArrowLabel(FrontViewCanvas, "X방향", padLeft + drawWidth / 2, groundY - 18);

        if (isZoomedView)
        {
            AddCanvasText(
                FrontViewCanvas,
                $"확대 표시 0 ~ {visibleRangeX:0.##}m / 전체 {rangeX:0.##}m",
                width - 10,
                10,
                -220,
                0,
                "#A8B3C6",
                10,
                FontWeights.SemiBold);
        }
        else
        {
            AddCanvasText(
                FrontViewCanvas,
                $"전체 측정범위 {rangeX:0.##}m",
                width - 10,
                10,
                -120,
                0,
                "#A8B3C6",
                10,
                FontWeights.SemiBold);
        }

        for (var i = 0; i < rankedResults.Count; i++)
        {
            var result = rankedResults[i];
            var color = GetRankColor(i);
            var brush = new SolidColorBrush(color);
            // Front View는 X를 거리, Y를 심도로 두는 단면도라서 깊이가 아래로 갈수록 값이 커짐
            var x = ToScreenX(result.DistanceMeters);
            var y = ToScreenY(result.DepthMeters);

            FrontViewCanvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = groundY,
                X2 = x,
                Y2 = y,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D2D7E0")!),
                StrokeThickness = 1.6,
                Opacity = 0.95
            });

            AddPoint(FrontViewCanvas, x, y, color, 8.5);
            AddCanvasText(FrontViewCanvas, $"{result.DepthMeters:0.0} m", x, (groundY + y) / 2, 8, -2, "#D8DDE8", 10.5, FontWeights.Normal);
            AddCanvasText(FrontViewCanvas, $"{result.Index:00}(#{result.SourceIndex:00})", x, y, -8, 12, color, 11, FontWeights.Bold);
        }

        UpdateRankedOverlay(FrontViewRankOverlay, rankedResults, r => $"{r.DepthMeters:0.00} m", "신뢰도 높은 순 (심도)");
    }

    /// <summary>
    /// 입력된 측정 범위를 숫자로 파싱해 뷰 계산에 사용
    /// </summary>
    private static bool TryGetRanges(MainViewModel vm, out double rangeX, out double rangeY)
    {
        rangeX = 0;
        rangeY = 0;
        return TryParseDouble(vm.ScanRangeX, out rangeX) && rangeX > 0
            && TryParseDouble(vm.ScanRangeY, out rangeY) && rangeY > 0;
    }

    /// <summary>
    /// 정면 보기의 기준선을 단순 수평선으로 그리기
    /// </summary>
    private static void DrawBaseline(Canvas canvas, double x1, double x2, double y)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y,
            X2 = x2,
            Y2 = y,
            Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0xA6, 0xFF)),
            StrokeThickness = 2.2,
            StrokeDashArray = new DoubleCollection { 6, 4 }
        });
    }

    /// <summary>
    /// 측선 방향을 가진 기준선을 캔버스에 그리기
    /// </summary>
    private static void DrawBaselineDirectional(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0xA6, 0xFF)),
            StrokeThickness = 2.2,
            StrokeDashArray = new DoubleCollection { 6, 4 }
        });
    }

    /// <summary>
    /// 실선 안내선을 지정된 색과 두께로 그리기
    /// </summary>
    private static void DrawSolidGuideLine(Canvas canvas, Point start, Point end, string hexColor, double thickness)
    {
        canvas.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)!),
            StrokeThickness = thickness,
            SnapsToDevicePixels = true
        });
    }

    /// <summary>
    /// 진행 방향을 보여주는 화살표 도형을 그리기
    /// </summary>
    private static void DrawDirectionArrow(Canvas canvas, Point start, Point end, string hexColor)
    {
        var direction = end - start;
        direction.Normalize();
        var arrowCenter = start + (direction * ((end - start).Length * 0.56));
        var arrowSize = 16.0;
        var normal = new Vector(-direction.Y, direction.X);
        normal.Normalize();

        var arrowTip = arrowCenter + (direction * (arrowSize * 0.9));
        var basePoint = arrowCenter - (direction * (arrowSize * 0.7));
        var leftWing = basePoint + (normal * 7);
        var rightWing = basePoint - (normal * 7);

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)!);
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection { arrowTip, leftWing, rightWing },
            Fill = brush,
            Stroke = brush,
            StrokeThickness = 1
        });
    }

    /// <summary>
    /// 거리 범위를 설명하는 브래킷과 텍스트를 그리기
    /// </summary>
    private static void DrawDistanceBracket(Canvas canvas, Point start, Point end, Vector sideOffset, string text)
    {
        var bracketStart = start + sideOffset;
        var bracketEnd = end + sideOffset;
        var direction = bracketEnd - bracketStart;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        normal.Normalize();

        canvas.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = bracketStart.X,
            Y2 = bracketStart.Y,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5EA0F2")!),
            StrokeThickness = 1.2
        });
        canvas.Children.Add(new Line
        {
            X1 = end.X,
            Y1 = end.Y,
            X2 = bracketEnd.X,
            Y2 = bracketEnd.Y,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5EA0F2")!),
            StrokeThickness = 1.2
        });
        canvas.Children.Add(new Line
        {
            X1 = bracketStart.X,
            Y1 = bracketStart.Y,
            X2 = bracketEnd.X,
            Y2 = bracketEnd.Y,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5EA0F2")!),
            StrokeThickness = 1.2
        });

        DrawBracketArrowHead(canvas, bracketStart, direction, normal, "#5EA0F2");
        DrawBracketArrowHead(canvas, bracketEnd, -direction, normal, "#5EA0F2");

        AddCanvasText(
            canvas,
            text,
            (bracketStart.X + bracketEnd.X) / 2,
            (bracketStart.Y + bracketEnd.Y) / 2,
            -12,
            -8,
            "#D6E6FF",
            10,
            FontWeights.SemiBold);
    }

    /// <summary>
    /// 브래킷 양 끝에 화살촉을 추가
    /// </summary>
    private static void DrawBracketArrowHead(Canvas canvas, Point point, Vector direction, Vector normal, string hexColor)
    {
        direction.Normalize();
        normal.Normalize();
        var tip = point;
        var left = tip + (direction * 10) + (normal * 4);
        var right = tip + (direction * 10) - (normal * 4);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)!);

        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection { tip, left, right },
            Fill = brush,
            Stroke = brush,
            StrokeThickness = 1
        });
    }

    /// <summary>
    /// 결과 점 또는 기준 점을 원형 마커로 캔버스에 추가
    /// </summary>
    private static void AddPoint(Canvas canvas, double x, double y, Color color, double size)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = 1.1
        };
        Canvas.SetLeft(dot, x - (size / 2));
        Canvas.SetTop(dot, y - (size / 2));
        canvas.Children.Add(dot);
    }

    /// <summary>
    /// 문자열 색상을 16진수로 받아 캔버스 텍스트를 배치
    /// </summary>
    private static void AddCanvasText(Canvas canvas, string text, double x, double y, double offsetX, double offsetY, string hexColor, double fontSize, FontWeight? weight = null)
        => AddCanvasText(canvas, text, x, y, offsetX, offsetY, (Color)ColorConverter.ConvertFromString(hexColor)!, fontSize, weight);

    /// <summary>
    /// 캔버스 좌표와 오프셋을 기준으로 분석 뷰의 텍스트 라벨을 배치
    /// </summary>
    private static void AddCanvasText(Canvas canvas, string text, double x, double y, double offsetX, double offsetY, Color color, double fontSize, FontWeight? weight = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontSize = fontSize,
            FontFamily = new FontFamily("Malgun Gothic"),
            FontWeight = weight ?? FontWeights.Normal
        };
        Canvas.SetLeft(tb, x + offsetX);
        Canvas.SetTop(tb, y + offsetY);
        canvas.Children.Add(tb);
    }

    /// <summary>
    /// 방향 화살표 근처에 좌우 안내 라벨을 배치
    /// </summary>
    private static void AddArrowLabel(Canvas canvas, string label, double x, double y)
    {
        var tb = new TextBlock
        {
            Text = $"← {label} →",
            Foreground = new SolidColorBrush(Color.FromArgb(0xD6, 0xC4, 0xCB, 0xD8)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(tb, x - 28);
        Canvas.SetTop(tb, y - 8);
        canvas.Children.Add(tb);
    }

    /// <summary>
    /// 신뢰도 순 결과 목록 오버레이 패널을 갱신
    /// </summary>
    private static void UpdateRankedOverlay(Panel panel, IReadOnlyList<PredictionResult> ranked, Func<PredictionResult, string> valueSelector, string title)
    {
        panel.Children.Clear();

        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A8B3C6")!),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Malgun Gothic"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        for (var i = 0; i < ranked.Count; i++)
        {
            var result = ranked[i];
            panel.Children.Add(new TextBlock
            {
                Text = $"{result.Index:00}(#{result.SourceIndex:00})  {valueSelector(result)}",
                Foreground = new SolidColorBrush(GetRankColor(i)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 1)
            });
        }
    }

    /// <summary>
    /// 시작점과 방향점을 이용해 Top View의 진행 단위 벡터를 구
    /// </summary>
    private static Vector GetTopViewDirection(MainViewModel vm)
    {
        if (TryParseDouble(vm.DirectionPointX, out var directionX) &&
            TryParseDouble(vm.DirectionPointY, out var directionY) &&
            TryParseDouble(vm.StartPointX, out var startX) &&
            TryParseDouble(vm.StartPointY, out var startY))
        {
            var dx = directionX - startX;
            var dy = startY - directionY;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length > 0.0001)
            {
                return new Vector(dx / length, dy / length);
            }
        }

        return new Vector(1, 0);
    }

    /// <summary>
    /// 순위 인덱스에 맞는 결과 표시 색상을 반환
    /// </summary>
    private static Color GetRankColor(int rank)
        => rank < RankColors.Length ? RankColors[rank] : Colors.White;

    private static bool TryParseDouble(string value, out double result)
    {
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                     double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        return parsed && double.IsFinite(result);
    }
}
