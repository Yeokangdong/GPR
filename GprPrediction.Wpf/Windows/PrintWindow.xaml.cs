using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GprPrediction.Wpf.ViewModels;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// PrintWindow 관련 상태와 동작 관리
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class PrintWindow : Window
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly Color[] RankColors =
    {
        (Color)ColorConverter.ConvertFromString("#FF4D79")!,
        (Color)ColorConverter.ConvertFromString("#FF9F43")!,
        (Color)ColorConverter.ConvertFromString("#FFD166")!,
        (Color)ColorConverter.ConvertFromString("#59E390")!,
        (Color)ColorConverter.ConvertFromString("#6FC3FF")!
    };

    private MainViewModel? subscribedViewModel;

    /// <summary>
    /// PrintWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public PrintWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        Loaded += (_, _) => RedrawReportViews();
    }

    /// <summary>
    /// MinimizeWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// MaximizeRestoreWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MaximizeRestoreWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// CloseWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
        => Close();

    /// <summary>
    /// OnClosed 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            subscribedViewModel = null;
        }
    }

    /// <summary>
    /// OnDataContextChanged 처리 수행
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
        }

        RedrawReportViews();
    }

    /// <summary>
    /// ViewModel_PropertyChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Results) or
            nameof(MainViewModel.ScanRangeX) or
            nameof(MainViewModel.ScanRangeY) or
            nameof(MainViewModel.StartPointX) or
            nameof(MainViewModel.StartPointY) or
            nameof(MainViewModel.DirectionPointX) or
            nameof(MainViewModel.DirectionPointY))
        {
            Dispatcher.Invoke(RedrawReportViews);
        }
    }

    /// <summary>
    /// ReportCanvas_SizeChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ReportCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RedrawReportViews();

    /// <summary>
    /// RedrawReportViews 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RedrawReportViews()
    {
        if (DataContext is not MainViewModel vm)
        {
            ReportTopViewCanvas.Children.Clear();
            ReportFrontViewCanvas.Children.Clear();
            return;
        }

        RedrawTopView(vm);
        RedrawFrontView(vm);
    }

    /// <summary>
    /// Print_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var originalWidth = ReportPreviewRoot.Width;
        var printableWidth = Math.Max(700, dialog.PrintableAreaWidth - 60);

        ReportPreviewRoot.Width = printableWidth;
        ReportPreviewRoot.Measure(new Size(printableWidth, double.PositiveInfinity));
        ReportPreviewRoot.Arrange(new Rect(new Point(0, 0), ReportPreviewRoot.DesiredSize));
        ReportPreviewRoot.UpdateLayout();

        RedrawReportViews();

        var viewer = ReportPreviewRoot.Parent as ScrollViewer;
        if (viewer is not null)
        {
            var originalOffset = viewer.VerticalOffset;
            viewer.ScrollToHome();
            dialog.PrintVisual(ReportPreviewRoot, "GPR Analysis Report");
            viewer.ScrollToVerticalOffset(originalOffset);
        }
        else
        {
            dialog.PrintVisual(ReportPreviewRoot, "GPR Analysis Report");
        }

        ReportPreviewRoot.Width = originalWidth;
        ReportPreviewRoot.UpdateLayout();
        RedrawReportViews();
    }

    /// <summary>
    /// SaveReport_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "보고서를 저장할 폴더 선택"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var reportDirectory = IOPath.Combine(dialog.FolderName, $"GPR_Report_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(reportDirectory);

        var imageTargetName = "analysis.jpg";
        var imageSourcePath = viewModel.GetPreferredAnalysisImagePath();
        if (!string.IsNullOrWhiteSpace(imageSourcePath) && File.Exists(imageSourcePath))
        {
            File.Copy(imageSourcePath, IOPath.Combine(reportDirectory, imageTargetName), true);
        }

        ReportPreviewRoot.UpdateLayout();
        RedrawReportViews();

        var previewImageName = "report_preview.png";
        var previewImagePath = IOPath.Combine(reportDirectory, previewImageName);
        SaveElementAsPng(ReportPreviewRoot, previewImagePath);

        var csvPath = IOPath.Combine(reportDirectory, "prediction_results.csv");
        WriteResultsCsv(viewModel, csvPath);

        var htmlPath = IOPath.Combine(reportDirectory, "report.html");
        File.WriteAllText(htmlPath, BuildReportHtml(viewModel, previewImageName), Utf8NoBom);

        CustomMessageBox.Show($"보고서 저장 완료:\n{reportDirectory}", "출력 및 보고서", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// RedrawTopView 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RedrawTopView(MainViewModel vm)
    {
        ReportTopViewCanvas.Children.Clear();

        if (vm.Results.Count == 0 || !TryGetRanges(vm, out var rangeX, out _))
        {
            return;
        }

        var width = ReportTopViewCanvas.ActualWidth;
        var height = ReportTopViewCanvas.ActualHeight;
        if (width < 120 || height < 120)
        {
            return;
        }

        const double padLeft = 44;
        const double padRight = 44;
        const double padTop = 22;
        const double padBottom = 28;
        var drawWidth = width - padLeft - padRight;
        var drawHeight = height - padTop - padBottom;
        if (drawWidth < 80 || drawHeight < 80)
        {
            return;
        }

        var rankedResults = vm.Results.OrderByDescending(r => r.ConfidenceRatio).ToList();
        var visibleRangeX = Math.Max(rangeX, 1);
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
            (drawHeight * 0.78) / Math.Max(Math.Abs(normalizedDirection.Y) * visibleRangeX, 0.0001));
        lineScale = Math.Max(lineScale, 1);

        var startPoint = new Point(
            padLeft + (drawWidth * 0.38),
            padTop + 16);
        var endPoint = new Point(
            startPoint.X + (normalizedDirection.X * visibleRangeX * lineScale),
            startPoint.Y + (normalizedDirection.Y * visibleRangeX * lineScale));

        var mainLineOffset = normal * -12;
        var resultAxisOffset = normal * 20;
        var startMain = startPoint + mainLineOffset;
        var endMain = endPoint + mainLineOffset;
        var startResultAxis = startPoint + resultAxisOffset;
        var endResultAxis = endPoint + resultAxisOffset;

        DrawSolidGuideLine(ReportTopViewCanvas, startMain, endMain, "#5EA0F2", 3.6);
        DrawBaselineDirectional(ReportTopViewCanvas, startPoint.X, startPoint.Y, endPoint.X, endPoint.Y);
        DrawSolidGuideLine(ReportTopViewCanvas, startResultAxis, endResultAxis, "#B8C2D6", 1.1);
        DrawDirectionArrow(ReportTopViewCanvas, startPoint, endPoint, "#5EA0F2");
        DrawDistanceBracket(ReportTopViewCanvas, startMain, endMain, normal * -14, $"{visibleRangeX:0.##}m");

        AddCanvasText(ReportTopViewCanvas, "측정 시작점", startPoint.X, startPoint.Y, (normal.X * 34) - 10, (normal.Y * 34) - 12, "#00E0B2", 10.5, FontWeights.Bold);
        AddCanvasText(ReportTopViewCanvas, "측정종점", endPoint.X, endPoint.Y, (normal.X * 30) - 16, (normal.Y * 30) - 2, "#00E0B2", 10.5, FontWeights.Bold);

        for (var i = 0; i < rankedResults.Count; i++)
        {
            var result = rankedResults[i];
            var color = GetRankColor(i);
            var distance = Math.Min(Math.Max(result.DistanceMeters, 0), visibleRangeX) * lineScale;
            var axisPoint = new Point(
                startResultAxis.X + (normalizedDirection.X * distance),
                startResultAxis.Y + (normalizedDirection.Y * distance));

            AddPoint(ReportTopViewCanvas, axisPoint.X, axisPoint.Y, color, 8.2);
            AddCanvasText(ReportTopViewCanvas, $"{result.Index:00}(#{result.SourceIndex:00})", axisPoint.X, axisPoint.Y, 8, -10 + ((i % 2) * 14), color, 9.2, FontWeights.Bold);
        }
    }

    /// <summary>
    /// RedrawFrontView 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RedrawFrontView(MainViewModel vm)
    {
        ReportFrontViewCanvas.Children.Clear();

        if (vm.Results.Count == 0 || !TryGetRanges(vm, out var rangeX, out var rangeY))
        {
            return;
        }

        var width = ReportFrontViewCanvas.ActualWidth;
        var height = ReportFrontViewCanvas.ActualHeight;
        if (width < 120 || height < 120)
        {
            return;
        }

        const double padLeft = 38;
        const double padRight = 38;
        const double padTop = 28;
        const double padBottom = 38;
        var drawWidth = width - padLeft - padRight;
        var drawHeight = height - padTop - padBottom;
        if (drawWidth < 80 || drawHeight < 80)
        {
            return;
        }

        var rankedResults = vm.Results.OrderByDescending(r => r.ConfidenceRatio).ToList();
        var visibleRangeX = Math.Max(rangeX, 1);
        var visibleRangeY = Math.Max(rangeY, 0.8);

        double ToScreenX(double distance) => padLeft + (Math.Min(distance, visibleRangeX) / visibleRangeX) * drawWidth;
        double ToScreenY(double depth) => padTop + (depth / visibleRangeY) * drawHeight;

        var groundY = padTop;
        DrawBaseline(ReportFrontViewCanvas, padLeft, padLeft + drawWidth, groundY);
        AddCanvasText(ReportFrontViewCanvas, "측정 시작점", padLeft, groundY, -4, -20, "#00E0B2", 10.5, FontWeights.Bold);
        AddCanvasText(ReportFrontViewCanvas, "측정종점", padLeft + drawWidth, groundY, -36, -20, "#00E0B2", 10.5, FontWeights.Bold);
        AddArrowLabel(ReportFrontViewCanvas, "X방향", padLeft + drawWidth / 2, groundY - 18);

        for (var i = 0; i < rankedResults.Count; i++)
        {
            var result = rankedResults[i];
            var color = GetRankColor(i);
            var x = ToScreenX(result.DistanceMeters);
            var y = ToScreenY(result.DepthMeters);

            ReportFrontViewCanvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = groundY,
                X2 = x,
                Y2 = y,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D2D7E0")!),
                StrokeThickness = 1.3,
                Opacity = 0.95
            });

            AddPoint(ReportFrontViewCanvas, x, y, color, 7.6);
            AddCanvasText(ReportFrontViewCanvas, $"{result.DepthMeters:0.0} m", x, (groundY + y) / 2, 6, -2, "#D8DDE8", 9.2, FontWeights.Normal);
            AddCanvasText(ReportFrontViewCanvas, $"{result.Index:00}(#{result.SourceIndex:00})", x, y, -7, 10, color, 9.2, FontWeights.Bold);
        }
    }

    /// <summary>
    /// WriteResultsCsv 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void WriteResultsCsv(MainViewModel viewModel, string csvPath)
    {
        // 결과 필드 순서를 고정해 외부 도구에서 동일한 CSV 스키마 사용
        var builder = new StringBuilder();
        builder.AppendLine("index,source_index,distance_m,depth_m,confidence_pct,raw");

        foreach (var result in viewModel.Results)
        {
            builder.Append(result.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(result.SourceIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(result.DistanceMeters.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(result.DepthMeters.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append((result.ConfidenceRatio * 100).ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.AppendLine($"\"{result.RawLine.Replace("\"", "\"\"")}\"");
        }

        File.WriteAllText(csvPath, builder.ToString(), Utf8NoBom);
    }

    /// <summary>
    /// SaveElementAsPng 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void SaveElementAsPng(FrameworkElement element, string outputPath)
    {
        element.Measure(new Size(element.ActualWidth, element.ActualHeight));
        element.Arrange(new Rect(new Size(element.ActualWidth, element.ActualHeight)));
        element.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    /// <summary>
    /// BuildReportHtml 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string BuildReportHtml(MainViewModel viewModel, string? previewImageFileName)
    {
        // 미리보기 이미지를 포함한 독립 실행형 HTML 보고서 구성
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"ko\"><head><meta charset=\"utf-8\"><title>GPR Report</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Malgun Gothic,Segoe UI,Arial,sans-serif;background:#fff;color:#111;margin:0;padding:24px;}");
        builder.AppendLine(".page{width:760px;margin:0 auto;border:1px solid #c9cdd4;padding:20px;box-sizing:border-box;}");
        builder.AppendLine("h1{margin:0 0 18px 0;font-size:28px;font-weight:400;} img{display:block;max-width:100%;height:auto;border:1px solid #d6d9e0;}");
        builder.AppendLine("</style></head><body><div class=\"page\">");
        builder.AppendLine("<h1>결과 출력 양식</h1>");
        if (!string.IsNullOrWhiteSpace(previewImageFileName))
        {
            builder.AppendLine($"<img src=\"{WebUtility.HtmlEncode(previewImageFileName)}\" alt=\"report preview\">");
        }
        builder.AppendLine("</div></body></html>");
        return builder.ToString();
    }

    /// <summary>
    /// TryGetRanges 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryGetRanges(MainViewModel vm, out double rangeX, out double rangeY)
    {
        rangeX = 0;
        rangeY = 0;
        return TryParseDouble(vm.ScanRangeX, out rangeX) && rangeX > 0
            && TryParseDouble(vm.ScanRangeY, out rangeY) && rangeY > 0;
    }

    /// <summary>
    /// DrawBaseline 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
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
    /// DrawBaselineDirectional 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
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
    /// DrawSolidGuideLine 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
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
    /// DrawDirectionArrow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void DrawDirectionArrow(Canvas canvas, Point start, Point end, string hexColor)
    {
        var direction = end - start;
        direction.Normalize();
        var arrowCenter = start + (direction * ((end - start).Length * 0.56));
        var arrowSize = 14.0;
        var normal = new Vector(-direction.Y, direction.X);
        normal.Normalize();

        var arrowTip = arrowCenter + (direction * (arrowSize * 0.9));
        var basePoint = arrowCenter - (direction * (arrowSize * 0.7));
        var leftWing = basePoint + (normal * 6);
        var rightWing = basePoint - (normal * 6);

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
    /// DrawDistanceBracket 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
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
            StrokeThickness = 1.1
        });
        canvas.Children.Add(new Line
        {
            X1 = end.X,
            Y1 = end.Y,
            X2 = bracketEnd.X,
            Y2 = bracketEnd.Y,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5EA0F2")!),
            StrokeThickness = 1.1
        });
        canvas.Children.Add(new Line
        {
            X1 = bracketStart.X,
            Y1 = bracketStart.Y,
            X2 = bracketEnd.X,
            Y2 = bracketEnd.Y,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5EA0F2")!),
            StrokeThickness = 1.1
        });

        DrawBracketArrowHead(canvas, bracketStart, direction, normal, "#5EA0F2");
        DrawBracketArrowHead(canvas, bracketEnd, -direction, normal, "#5EA0F2");
        AddCanvasText(canvas, text, (bracketStart.X + bracketEnd.X) / 2, (bracketStart.Y + bracketEnd.Y) / 2, -10, -8, "#D6E6FF", 9.2, FontWeights.SemiBold);
    }

    /// <summary>
    /// DrawBracketArrowHead 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void DrawBracketArrowHead(Canvas canvas, Point point, Vector direction, Vector normal, string hexColor)
    {
        direction.Normalize();
        normal.Normalize();
        var tip = point;
        var left = tip + (direction * 8) + (normal * 3.5);
        var right = tip + (direction * 8) - (normal * 3.5);
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
    /// AddPoint 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void AddPoint(Canvas canvas, double x, double y, Color color, double size)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = 1.0
        };
        Canvas.SetLeft(dot, x - (size / 2));
        Canvas.SetTop(dot, y - (size / 2));
        canvas.Children.Add(dot);
    }

    /// <summary>
    /// AddCanvasText 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void AddCanvasText(Canvas canvas, string text, double x, double y, double offsetX, double offsetY, string hexColor, double fontSize, FontWeight? weight = null)
        => AddCanvasText(canvas, text, x, y, offsetX, offsetY, (Color)ColorConverter.ConvertFromString(hexColor)!, fontSize, weight);

    /// <summary>
    /// AddCanvasText 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
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
    /// AddArrowLabel 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static void AddArrowLabel(Canvas canvas, string label, double x, double y)
    {
        var tb = new TextBlock
        {
            Text = $"← {label} →",
            Foreground = new SolidColorBrush(Color.FromArgb(0xD6, 0xC4, 0xCB, 0xD8)),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(tb, x - 24);
        Canvas.SetTop(tb, y - 8);
        canvas.Children.Add(tb);
    }

    /// <summary>
    /// GetTopViewDirection 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
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
    /// GetRankColor 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static Color GetRankColor(int rank)
        => rank < RankColors.Length ? RankColors[rank] : Colors.White;

    /// <summary>
    /// TryParseDouble 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryParseDouble(string value, out double result)
    {
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                     double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        return parsed && double.IsFinite(result);
    }
}

