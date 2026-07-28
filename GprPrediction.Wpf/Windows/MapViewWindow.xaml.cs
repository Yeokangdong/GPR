using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GprPrediction.Wpf.Models;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// 지도 배경, 측선, 저장 결과와 병합선을 확대/이동 가능한 전용 맵 창에 표시
/// </summary>
public partial class MapViewWindow : Window
{
    private const double PreviewWidth = 800;
    private const double PreviewHeight = 580;
    private const double MinScale = 0.05;
    private const double MaxScale = 64;

    public static readonly DependencyProperty InverseMapScaleProperty =
        DependencyProperty.Register(
            nameof(InverseMapScale), typeof(double), typeof(MapViewWindow), new PropertyMetadata(1.0));

    public static readonly DependencyProperty SurveyLineStrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(SurveyLineStrokeThickness), typeof(double), typeof(MapViewWindow), new PropertyMetadata(3.4));

    public static readonly DependencyProperty MergeLineStrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(MergeLineStrokeThickness), typeof(double), typeof(MapViewWindow), new PropertyMetadata(2.6));

    private readonly ScaleTransform scaleTransform = new();
    private readonly TranslateTransform panTransform = new();
    private readonly DispatcherTimer mapBitmapRefreshTimer = new();
    private readonly MatrixTransform backgroundPreviewTransform = new();
    private double committedScale = 1;
    private double committedPanX;
    private double committedPanY;
    private Point dragStart;
    private bool isPanning;
    private bool isPanArmed;
    private bool hasUserNavigated;
    private bool showLoadingOnRefresh;
    private MainViewModel? subscribedViewModel;

    public double InverseMapScale
    {
        get => (double)GetValue(InverseMapScaleProperty);
        private set => SetValue(InverseMapScaleProperty, value);
    }

    public double SurveyLineStrokeThickness
    {
        get => (double)GetValue(SurveyLineStrokeThicknessProperty);
        private set => SetValue(SurveyLineStrokeThicknessProperty, value);
    }

    public double MergeLineStrokeThickness
    {
        get => (double)GetValue(MergeLineStrokeThicknessProperty);
        private set => SetValue(MergeLineStrokeThicknessProperty, value);
    }

    /// <summary>
    /// 맵 뷰 창을 초기화하고 비트맵 캐시 타이머와 이벤트를 연결
    /// </summary>
    public MapViewWindow()
    {
        InitializeComponent();

        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(scaleTransform);
        transformGroup.Children.Add(panTransform);
        MapCanvas.RenderTransform = transformGroup;
        BackgroundImageLayer.RenderTransform = backgroundPreviewTransform;
        UpdateOverlayScaleMetrics(1.0);

        mapBitmapRefreshTimer.Interval = TimeSpan.FromMilliseconds(120);
        mapBitmapRefreshTimer.Tick += MapBitmapRefreshTimer_Tick;

        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        Loaded += (_, _) =>
        {
            QueueMapViewReset(showLoading: true);
            RefreshPointCoordinateTexts();
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
    /// 타이머와 이벤트 구독을 정리해 창 종료 후 잔여 작업을 방지
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        mapBitmapRefreshTimer.Stop();

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            subscribedViewModel = null;
        }
    }

    /// <summary>
    /// ViewModel 교체 시 속성 변경 이벤트를 다시 연결
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
    /// 맵 관련 속성이 바뀌면 좌표와 비트맵 갱신을 반영
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
    /// 레이아웃 완료 뒤 초기 맵 뷰와 비트맵 캐시를 맞춤
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
    /// 상단 맵 선택 버튼 클릭 시 해당 지도를 활성화
    /// </summary>
    private void MapChipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MapEntry entry } && DataContext is MainViewModel vm)
        {
            vm.SelectMapCommand.Execute(entry);
        }
    }

    /// <summary>
    /// GPR 병합 창을 열어 병합선 선택 작업을 시작
    /// </summary>
    private void OpenMergeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var window = new MergeWindow(viewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    /// <summary>
    /// 저장 결과 열기 창을 열어 SEN 파일을 지도에 불러오기
    /// </summary>
    private void OpenSavedResults_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var window = new ResultOpenWindow(viewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    /// <summary>
    /// 현재 맵 뷰의 확대와 이동 상태를 초기화
    /// </summary>
    private void ResetMapView_Click(object sender, RoutedEventArgs e)
    {
        hasUserNavigated = false;
        ResetMapView(showLoading: true);
    }

    /// <summary>
    /// 패닝 드래그 시작 위치를 기록하고 마우스를 캡처
    /// </summary>
    private void MapSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        isPanArmed = true;
        isPanning = false;
        dragStart = e.GetPosition(MapSurface);
        MapSurface.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// 패닝 중인 거리만큼 화면을 이동하고 커서 좌표를 갱신
    /// </summary>
    private void MapSurface_MouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(MapSurface);
        UpdateCursorCoordinateText(current);

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
    /// 패닝 드래그를 종료
    /// </summary>
    private void MapSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        isPanning = false;
        isPanArmed = false;
        MapSurface.ReleaseMouseCapture();
    }

    /// <summary>
    /// 맵 영역을 벗어나면 드래그와 커서 좌표 상태를 정리
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
    /// 마우스 위치를 기준으로 맵을 확대 또는 축소
    /// </summary>
    private void MapSurface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
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
    /// 배경 맵 비트맵 재생성을 타이머로 지연 예약
    /// </summary>
    private void ScheduleMapBitmapRefresh(bool showLoading = false)
    {
        showLoadingOnRefresh |= showLoading;

        // 연속 휠/패닝 입력 동안 즉시 재생성하지 않고 짧게 디바운스해 체감 성능을 높
        if (showLoading && DataContext is MainViewModel viewModel)
        {
            viewModel.ShowTransientMapLoading("캐싱중...");
        }

        mapBitmapRefreshTimer.Stop();
        mapBitmapRefreshTimer.Start();
    }

    /// <summary>
    /// 예약된 시점에 현재 뷰포트 기준의 맵 비트맵을 다시 만들기
    /// </summary>
    private async void MapBitmapRefreshTimer_Tick(object? sender, EventArgs e)
    {
        mapBitmapRefreshTimer.Stop();

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.RefreshMapBitmapForViewportAsync(
                scaleTransform.ScaleX,
                panTransform.X,
                panTransform.Y,
                MapSurface.ActualWidth,
                MapSurface.ActualHeight);
        }
        finally
        {
            if (showLoadingOnRefresh)
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
    /// 배경 비트맵 미리보기 변환을 현재 줌/패닝 상태와 맞춤
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
    /// 화면 좌표를 내부 캔버스 좌표계로 변환
    /// </summary>
    private Point ConvertSurfaceToCanvas(Point surfacePoint)
    {
        var scale = scaleTransform.ScaleX <= 0 ? 1 : scaleTransform.ScaleX;
        return new Point(
            (surfacePoint.X - panTransform.X) / scale,
            (surfacePoint.Y - panTransform.Y) / scale);
    }

    /// <summary>
    /// 캔버스 전체 또는 측선 초점 기준으로 맵 뷰를 초기 배치
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
    /// 측선 시작점과 방향점을 중심으로 적절한 확대/센터 값을 계산
    /// </summary>
    private bool TryFitToSurveyFocus()
    {
        if (DataContext is not MainViewModel vm)
        {
            return false;
        }

        // 시작점과 방향점을 모두 포함하는 사각형을 만든 뒤 여백을 주어 화면 중심에 맞춤
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
    /// 캔버스 전체가 보이도록 기본 맞춤 배율을 적용
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
    /// 실제 확대/이동 Transform과 관련 의존 속성을 한 번에 갱신
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
    /// 줌 비율에 따라 오버레이 선 두께와 라벨 보정값을 계산
    /// </summary>
    private void UpdateOverlayScaleMetrics(double scale)
    {
        var safeScale = scale <= 0 ? 1.0 : scale;
        InverseMapScale = 1.0 / safeScale;
        SurveyLineStrokeThickness = 3.4 / safeScale;
        MergeLineStrokeThickness = 2.6 / safeScale;
    }

    /// <summary>
    /// 지정 좌표가 캔버스 범위 안에 있는지 검사
    /// </summary>
    private static bool IsInsideCanvas(Point point)
        => point.X >= 0 && point.X <= PreviewWidth && point.Y >= 0 && point.Y <= PreviewHeight;

    /// <summary>
    /// 좌표가 NaN이나 무한대가 아닌 유효한 캔버스 점인지 검사
    /// </summary>
    private static bool IsCanvasPointValid(Point point)
        => !double.IsNaN(point.X) &&
           !double.IsNaN(point.Y) &&
           !double.IsInfinity(point.X) &&
           !double.IsInfinity(point.Y) &&
           IsInsideCanvas(point);

    /// <summary>
    /// 마우스 위치의 투영 좌표와 위경도 텍스트를 갱신
    /// </summary>
    private void UpdateCursorCoordinateText(Point surfacePosition)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.IsMapTransformReady)
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

        var (gisX, gisY) = viewModel.ConvertCanvasToGis(canvasPosition.X, canvasPosition.Y);
        CursorCoordinateText.Text = $"커서  X : {gisX:F4}    Y : {gisY:F4}";
        CursorGeographicText.Text = FormatLatLonLine("커서", viewModel, gisX, gisY);
    }

    /// <summary>
    /// 커서 좌표 표시 문자열을 기본 안내 상태로 되돌리기
    /// </summary>
    private void ResetCursorCoordinateTexts()
    {
        CursorCoordinateText.Text = "커서  X : -    Y : -";
        CursorGeographicText.Text = "커서  위도 : -    경도 : -";
    }

    /// <summary>
    /// 측정 시작점과 방향점 좌표 표시 문자열을 다시 계산
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
    /// 문자열 X/Y 좌표를 안전하게 숫자로 파싱
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
    /// 투영 좌표 텍스트 한 줄을 일관된 형식으로 만들기
    /// </summary>
    private static string FormatProjectedLine(string label, string xText, string yText)
        => TryParsePoint(xText, yText, out var x, out var y)
            ? $"{label}  X : {x:F4}    Y : {y:F4}"
            : $"{label}  X : -    Y : -";

    /// <summary>
    /// 투영 좌표를 위경도로 변환해 보조 텍스트 한 줄로 만들기
    /// </summary>
    private static string FormatLatLonLine(string label, MainViewModel vm, double x, double y)
        => vm.TryConvertToLatLon(x, y, out var latitude, out var longitude)
            ? $"{label}  위도 : {latitude:F6}    경도 : {longitude:F6}"
            : $"{label}  위도 : -    경도 : -";
}
