using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// CommandWindow 관련 상태와 동작 관리
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class CommandWindow : Window
{
    private const int MaximumVisibleLogLines = 20_000;
    private const int LogTrimBatchLines = 1_000;
    private static readonly Brush ErrorTextBrush =
        new SolidColorBrush(Color.FromRgb(255, 92, 112));
    private static readonly Brush WarningTextBrush =
        new SolidColorBrush(Color.FromRgb(255, 190, 92));
    private static readonly Brush StageTextBrush =
        new SolidColorBrush(Color.FromRgb(105, 170, 255));

    private MainViewModel? viewModel;
    private WizardStep step;
    private bool trackingAnalysis;
    private string lastProgress = string.Empty;
    private int lastDetailedStage;
    private DateTime analysisStartedAt;
    private int logLineCount;
    private bool isWritingLog;
    private double outputVerticalOffset;
    private double outputScrollableHeight;

    /// <summary>
    /// CommandWindow 인스턴스 초기화
    /// 필수 의존성과 초기 상태를 생성 시점에 확정
    /// </summary>
    public CommandWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// MinimizeWindow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MinimizeWindow(object sender, ExecutedRoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// CloseWindow 화면 닫기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CloseWindow(object sender, ExecutedRoutedEventArgs e)
        => Close();

    /// <summary>
    /// MaximizeRestore_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>
    /// Window_StateChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "복원" : "최대화";
    }

    /// <summary>
    /// TitleBar_MouseLeftButtonDown 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore_Click(sender, e);
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Window_Loaded 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        viewModel = DataContext as MainViewModel;
        if (viewModel is null)
        {
            Close();
            return;
        }

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ApplyCommandScrollBarStyle));
        Write("=== GPR 대화형 분석 ===");
        Write($"버전: {GetApplicationVersion()}");
        Write($"실행 환경: .NET {Environment.Version} / {RuntimeInformation.OSDescription}");
        Write($"프로세스: {RuntimeInformation.ProcessArchitecture}, PID {Environment.ProcessId}");
        Write($"작업 폴더: {Environment.CurrentDirectory}");
        Write("");
        Write("질문에 순서대로 답하면 화면의 '바로 분석'과 동일한 분석 로직이 실행됩니다.");
        Write("");
        Write("[입력 순서]");
        Write("1. GPR 스캔 파일 선택");
        Write("2. 지도 선택");
        Write("3. 측선 시작점 좌표 입력");
        Write("4. 측선 방향점 좌표 입력");
        Write("5. 측정 범위 입력");
        Write("6. 화면 스케일 입력");
        Write("7. 신뢰도 하한값 입력");
        Write("8. TDA 전처리 사용 여부 선택");
        Write("9. TDA 기준값 입력(TDA 사용 시)");
        Write("10. 전체 설정 확인 후 분석 시작");
        Write("");
        Write("[입력 방법]");
        Write("- 각 질문 아래의 '입력 예시' 형식으로 입력");
        Write("- 현재 화면 값을 그대로 사용하려면 빈 상태에서 Enter");
        Write("- 언제든 '다시', '취소', '종료' 입력 가능");
        Write("- 이 창의 모든 출력은 마우스로 선택한 뒤 Ctrl+C로 복사 가능\n");
        RestartWizard();
    }

    /// <summary>
    /// ApplyCommandScrollBarStyle 설정 반영
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ApplyCommandScrollBarStyle()
    {
        OutputTextBox.ApplyTemplate();
        OutputTextBox.UpdateLayout();

        if (TryFindResource("CommandScrollBarStyle") is not Style scrollBarStyle)
        {
            return;
        }

        foreach (var scrollBar in FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(OutputTextBox))
        {
            scrollBar.Style = scrollBarStyle;
        }
    }

    /// <summary>
    /// FindVisualChildren 대상 검색
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Window_Closed 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Window_Closed(object? sender, EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            viewModel.SuppressAlgorithmResultDialogs = false;
        }
    }

    /// <summary>
    /// InputTextBox_KeyDown 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        Submit();
    }

    /// <summary>
    /// Window_PreviewKeyDown 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.C && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CopyAllLog();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End && modifiers == ModifierKeys.Control)
        {
            ScrollToLatest();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ToggleAutoFollow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && AnalysisResultOverlay.Visibility == Visibility.Visible)
        {
            AnalysisResultOverlay.Visibility = Visibility.Collapsed;
            InputTextBox.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Submit_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Submit_Click(object sender, RoutedEventArgs e) => Submit();

    /// <summary>
    /// Submit 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Submit()
    {
        if (viewModel is null)
        {
            return;
        }

        var answer = InputTextBox.Text.Trim();
        InputTextBox.Clear();
        Write($"> {answer}");

        if (answer.Equals("종료", StringComparison.OrdinalIgnoreCase))
        {
            Close();
            return;
        }

        if (answer.Equals("취소", StringComparison.OrdinalIgnoreCase))
        {
            if (viewModel.IsAlgorithmRunning && viewModel.CancelAlgorithmCommand.CanExecute(null))
            {
                viewModel.CancelAlgorithmCommand.Execute(null);
                Write("분석 취소를 요청했습니다.");
            }
            else
            {
                Write("현재 실행 중인 분석이 없습니다.");
            }
            return;
        }

        if (answer.Equals("다시", StringComparison.OrdinalIgnoreCase))
        {
            RestartWizard();
            return;
        }

        if (trackingAnalysis)
        {
            Write("분석 중입니다. 취소하려면 '취소'를 입력하세요.");
            return;
        }

        ProcessAnswer(answer);
        InputTextBox.Focus();
    }

    /// <summary>
    /// ProcessAnswer 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ProcessAnswer(string answer)
    {
        if (viewModel is null)
        {
            return;
        }

        switch (step)
        {
            case WizardStep.ScanFile:
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    if (!File.Exists(answer))
                    {
                        Ask("파일을 찾을 수 없습니다. 스캔 파일의 전체 경로를 다시 입력하세요.",
                            @"D:\GPR_DATA\GAP_SEC01_0001.DZT");
                        return;
                    }
                    viewModel.ScanFilePath = answer;
                }
                step = WizardStep.Map;
                AskMap();
                break;

            case WizardStep.Map:
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    if (!int.TryParse(answer, out var mapIndex) ||
                        mapIndex < 1 || mapIndex > viewModel.MapEntries.Count)
                    {
                        AskMap("지도 번호가 올바르지 않습니다.");
                        return;
                    }

                    var selected = viewModel.MapEntries[mapIndex - 1];
                    foreach (var map in viewModel.MapEntries)
                    {
                        map.IsSelected = ReferenceEquals(map, selected);
                    }
                    viewModel.MapDwgPath = selected.FilePath;
                }
                step = WizardStep.StartPoint;
                Ask($"시작점 X Y를 입력하세요. [{viewModel.StartPointX} {viewModel.StartPointY}]",
                    "222035.5937 490429.4286");
                break;

            case WizardStep.StartPoint:
                if (!TryPair(answer, viewModel.StartPointX, viewModel.StartPointY, out var sx, out var sy))
                {
                    Ask("숫자 2개를 공백으로 구분해 입력하세요.",
                        "222035.5937 490429.4286");
                    return;
                }
                viewModel.StartPointX = sx;
                viewModel.StartPointY = sy;
                step = WizardStep.DirectionPoint;
                Ask($"방향점 X Y를 입력하세요. [{viewModel.DirectionPointX} {viewModel.DirectionPointY}]",
                    "222041.4817 490405.8790");
                break;

            case WizardStep.DirectionPoint:
                if (!TryPair(answer, viewModel.DirectionPointX, viewModel.DirectionPointY, out var dx, out var dy))
                {
                    Ask("숫자 2개를 공백으로 구분해 입력하세요.",
                        "222041.4817 490405.8790");
                    return;
                }
                viewModel.DirectionPointX = dx;
                viewModel.DirectionPointY = dy;
                step = WizardStep.Range;
                Ask($"측정 범위 X Z(m)를 입력하세요. [{viewModel.ScanRangeX} {viewModel.ScanRangeY}]",
                    "20 3");
                break;

            case WizardStep.Range:
                if (!TryPair(answer, viewModel.ScanRangeX, viewModel.ScanRangeY, out var rx, out var rz))
                {
                    Ask("숫자 2개를 공백으로 구분해 입력하세요.", "20 3");
                    return;
                }
                viewModel.ScanRangeX = rx;
                viewModel.ScanRangeY = rz;
                step = WizardStep.Scale;
                Ask($"X Z Scale을 입력하세요. [{viewModel.XScale} {viewModel.YScale}]",
                    "6 1");
                break;

            case WizardStep.Scale:
                if (!TryPair(answer, viewModel.XScale, viewModel.YScale, out var xs, out var zs))
                {
                    Ask("숫자 2개를 공백으로 구분해 입력하세요.", "6 1");
                    return;
                }
                viewModel.XScale = xs;
                viewModel.YScale = zs;
                step = WizardStep.Threshold;
                Ask($"신뢰도 Threshold를 입력하세요. [{viewModel.Threshold}]",
                    "0.6 (0~1 사이 숫자)");
                break;

            case WizardStep.Threshold:
                if (!TrySingle(answer, viewModel.Threshold, out var threshold))
                {
                    Ask("0~1 사이 숫자를 입력하세요.", "0.6");
                    return;
                }
                viewModel.Threshold = threshold;
                step = WizardStep.UseTda;
                Ask($"TDA 전처리를 사용할까요? (y/n) [{(viewModel.UseTda ? "y" : "n")}]",
                    "y (사용) 또는 n (사용 안 함)");
                break;

            case WizardStep.UseTda:
                if (!TryYesNo(answer, viewModel.UseTda, out var useTda))
                {
                    Ask("y 또는 n으로 답하세요.",
                        "y (사용) 또는 n (사용 안 함)");
                    return;
                }
                viewModel.UseTda = useTda;
                if (useTda)
                {
                    step = WizardStep.TdaThreshold;
                    Ask($"TDA Threshold를 입력하세요. [{viewModel.TdaThreshold}]",
                        "0.35 (0~1 사이 숫자)");
                }
                else
                {
                    AskConfirmation();
                }
                break;

            case WizardStep.TdaThreshold:
                if (!TrySingle(answer, viewModel.TdaThreshold, out var tdaThreshold))
                {
                    Ask("0~1 사이 숫자를 입력하세요.", "0.35");
                    return;
                }
                viewModel.TdaThreshold = tdaThreshold;
                AskConfirmation();
                break;

            case WizardStep.Confirm:
                if (!TryYesNo(answer, true, out var run))
                {
                    Ask("y 또는 n으로 답하세요.",
                        "y (분석 시작) 또는 n (처음부터 다시 입력)");
                    return;
                }
                if (!run)
                {
                    RestartWizard();
                    return;
                }
                StartAnalysis();
                break;
        }
    }

    /// <summary>
    /// RestartWizard 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void RestartWizard()
    {
        if (viewModel is null || viewModel.IsAlgorithmRunning)
        {
            return;
        }
        trackingAnalysis = false;
        step = WizardStep.ScanFile;
        Ask($"스캔 파일의 전체 경로를 입력하세요. [{viewModel.ScanFilePath}]",
            @"D:\GPR_DATA\GAP_SEC01_0001.DZT");
    }

    /// <summary>
    /// AskMap 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void AskMap(string? prefix = null)
    {
        if (viewModel is null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            Write(prefix);
        }
        Write("사용할 지도를 선택하세요.");
        for (var i = 0; i < viewModel.MapEntries.Count; i++)
        {
            var map = viewModel.MapEntries[i];
            Write($"  {i + 1}. {map.Name}{(map.IsSelected ? " (현재)" : string.Empty)}");
        }
        Ask($"지도 번호 [{CurrentMapIndex()}]", "1");
    }

    /// <summary>
    /// CurrentMapIndex 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private int CurrentMapIndex()
    {
        if (viewModel is null)
        {
            return 1;
        }
        var index = viewModel.MapEntries.ToList().FindIndex(map => map.IsSelected);
        return index >= 0 ? index + 1 : 1;
    }

    /// <summary>
    /// AskConfirmation 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void AskConfirmation()
    {
        if (viewModel is null)
        {
            return;
        }
        step = WizardStep.Confirm;
        Write("\n[실행 설정]");
        Write($"파일: {viewModel.ScanFilePath}");
        Write($"시작점: {viewModel.StartPointX}, {viewModel.StartPointY}");
        Write($"방향점: {viewModel.DirectionPointX}, {viewModel.DirectionPointY}");
        Write($"범위 X/Z: {viewModel.ScanRangeX} / {viewModel.ScanRangeY}");
        Write($"Scale X/Z: {viewModel.XScale} / {viewModel.YScale}");
        Write($"신뢰도: {viewModel.Threshold}");
        Write($"TDA: {(viewModel.UseTda ? $"사용 ({viewModel.TdaThreshold})" : "사용 안 함")}");
        Ask("이 설정으로 분석할까요? (y/n) [y]",
            "y (분석 시작) 또는 n (처음부터 다시 입력)");
    }

    /// <summary>
    /// StartAnalysis 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void StartAnalysis()
    {
        if (viewModel is null || !viewModel.RunAlgorithmCommand.CanExecute(null))
        {
            WriteError("분석을 시작할 수 없습니다. 입력 파일과 설정을 확인하세요.");
            RestartWizard();
            return;
        }

        trackingAnalysis = true;
        viewModel.SuppressAlgorithmResultDialogs = true;
        AnalysisResultOverlay.Visibility = Visibility.Collapsed;
        analysisStartedAt = DateTime.Now;
        lastProgress = string.Empty;
        lastDetailedStage = 0;
        Write("\n============================================================");
        Write("분석 실행을 시작합니다.");
        Write($"시작 시간: {analysisStartedAt:yyyy-MM-dd HH:mm:ss}");
        Write("");
        Write("[입력 데이터]");
        Write($"스캔 파일: {viewModel.ScanFilePath}");
        Write($"지도 파일: {viewModel.MapDwgPath}");
        Write($"시작점 X/Y: {viewModel.StartPointX} / {viewModel.StartPointY}");
        Write($"방향점 X/Y: {viewModel.DirectionPointX} / {viewModel.DirectionPointY}");
        Write("");
        Write("[분석 설정]");
        Write($"측정 범위 X/Z: {viewModel.ScanRangeX}m / {viewModel.ScanRangeY}m");
        Write($"화면 Scale X/Z: {viewModel.XScale} / {viewModel.YScale}");
        Write($"신뢰도 Threshold: {viewModel.Threshold}");
        Write($"TDA 전처리: {(viewModel.UseTda ? $"사용, Threshold {viewModel.TdaThreshold}" : "사용 안 함")}");
        Write("");
        Write("[실행 예정 단계]");
        Write("1/7 입력 파일, 알고리즘 및 실행 환경 확인");
        Write("2/7 작업 폴더 생성 및 이전 임시 파일 정리");
        Write("3/7 스캔 파일과 입력 설정 파일 준비");
        Write("4/7 main_1.py 실행: DZT 읽기, AGC 전처리, data.jpg 생성");
        Write(viewModel.UseTda
            ? "5/7 tda.jl 실행: data.jpg 분석 및 data.png 생성"
            : "5/7 TDA 전처리 미사용: 해당 단계를 건너뜀");
        Write("6/7 main_2.py 실행: 모델 선택, 객체 예측 및 좌표 변환");
        Write("7/7 결과 CSV 확인, 결과 이미지 로드 및 결과 폴더 저장");
        Write("");
        Write("아래부터 실제 진행 상황이 시간과 함께 계속 표시됩니다.");
        Write("분석을 중단하려면 입력란에 '취소'를 입력하세요.");
        Write("============================================================");
        viewModel.RunAlgorithmCommand.Execute(null);
    }

    /// <summary>
    /// ViewModel_PropertyChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (viewModel is null || !trackingAnalysis)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.AlgorithmLogSequence) &&
            !string.IsNullOrWhiteSpace(viewModel.AlgorithmLogLine))
        {
            lastProgress = viewModel.AlgorithmLogLine;
            Dispatcher.Invoke(() =>
            {
                Write($"[{DateTime.Now:HH:mm:ss.fff}] {ClassifyProgress(lastProgress)}");
                var stage = GetProgressStage(lastProgress);
                if (stage > 0 && stage != lastDetailedStage)
                {
                    lastDetailedStage = stage;
                    Write($"           상세: {GetProgressDescription(stage, viewModel.UseTda)}");
                    Write($"           확인: {GetStageVerification(stage, viewModel.UseTda)}");
                }
            });
        }

        if (e.PropertyName == nameof(MainViewModel.IsAlgorithmRunning) &&
            !viewModel.IsAlgorithmRunning)
        {
            Dispatcher.Invoke(() =>
            {
                trackingAnalysis = false;
                var completedAt = DateTime.Now;
                Write("");
                Write("============================================================");
                Write($"완료 시간: {completedAt:yyyy-MM-dd HH:mm:ss}");
                Write($"총 소요 시간: {completedAt - analysisStartedAt:hh\\:mm\\:ss}");
                Write($"{ClassifyCompletion(viewModel.LastAlgorithmResultText)} {viewModel.LastAlgorithmResultText}");
                foreach (var line in viewModel.BuildLastAnalysisReport())
                {
                    Write(ClassifyReportLine(line));
                }
                Write("============================================================");
                Write("새 분석을 시작하려면 '다시', 창을 닫으려면 '종료'를 입력하세요.");
                ShowAnalysisResultOverlay(viewModel.LastAlgorithmResultText);
            });
        }
    }

    /// <summary>
    /// ShowAnalysisResultOverlay 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ShowAnalysisResultOverlay(string resultText)
    {
        var message = string.IsNullOrWhiteSpace(resultText)
            ? "분석이 종료되었습니다. 아래 Command 로그에서 상세 결과를 확인하세요."
            : $"{resultText}\n\n아래 Command 로그에 실행 단계, 산출물 및 저장 경로가 기록되어 있습니다.";

        if (HasErrorMeaning(resultText))
        {
            AnalysisResultTitle.Text = "분석 실패";
            AnalysisResultIcon.Text = "!";
            AnalysisResultIconBackground.Background = ErrorTextBrush;
        }
        else if (ContainsAny(resultText, "결과 없음", "취소"))
        {
            AnalysisResultTitle.Text = resultText.Contains("취소", StringComparison.OrdinalIgnoreCase)
                ? "분석 취소"
                : "탐지 결과 없음";
            AnalysisResultIcon.Text = "!";
            AnalysisResultIconBackground.Background = WarningTextBrush;
        }
        else
        {
            AnalysisResultTitle.Text = "분석 완료";
            AnalysisResultIcon.Text = "✓";
            AnalysisResultIconBackground.Background =
                new SolidColorBrush(Color.FromRgb(33, 184, 166));
        }

        AnalysisResultMessage.Text = message;
        AnalysisResultOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// CloseAnalysisResultOverlay_Click 화면 닫기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CloseAnalysisResultOverlay_Click(object sender, RoutedEventArgs e)
    {
        AnalysisResultOverlay.Visibility = Visibility.Collapsed;
        InputTextBox.Focus();
    }

    /// <summary>
    /// Ask 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Ask(string text, string? example = null)
    {
        Write($"\n{CurrentQuestionNumber()}. {text}");
        if (!string.IsNullOrWhiteSpace(example))
        {
            Write($"  입력 예시: {example}");
        }
        Write("  현재 값을 유지하려면 아무것도 입력하지 않고 Enter");
        InputTextBox.Focus();
    }

    /// <summary>
    /// CurrentQuestionNumber 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private int CurrentQuestionNumber() => step switch
    {
        WizardStep.ScanFile => 1,
        WizardStep.Map => 2,
        WizardStep.StartPoint => 3,
        WizardStep.DirectionPoint => 4,
        WizardStep.Range => 5,
        WizardStep.Scale => 6,
        WizardStep.Threshold => 7,
        WizardStep.UseTda => 8,
        WizardStep.TdaThreshold => 9,
        WizardStep.Confirm => viewModel?.UseTda == true ? 10 : 9,
        _ => 1
    };

    /// <summary>
    /// GetProgressStage 데이터 조회
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static int GetProgressStage(string message)
    {
        for (var stage = 1; stage <= 7; stage++)
        {
            if (message.StartsWith($"{stage}/7", StringComparison.Ordinal))
            {
                return stage;
            }
        }
        return 0;
    }

    /// <summary>
    /// GetProgressDescription 데이터 조회
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string GetProgressDescription(int stage, bool useTda) => stage switch
    {
        1 => "선택한 스캔 파일이 존재하는지 확인하고 Python, Julia, 모델 및 알고리즘 파일의 실행 경로를 검사하는 단계",
        2 => "이번 분석만을 위한 작업 공간을 만들고 이전 실행에서 남은 임시 결과가 섞이지 않도록 정리하는 단계",
        3 => "선택한 DZT 파일을 작업 공간에 복사하고 좌표, 범위, Scale, Threshold와 모델 설정을 기록하는 단계",
        4 => "main_1.py가 DZT 원시 데이터를 읽고 AGC 전처리를 수행한 뒤 모델 입력용 data.jpg를 생성하는 단계",
        5 => useTda
            ? "tda.jl이 data.jpg에 TDA 전처리를 적용하고 TDA 모델 입력용 data.png를 생성하는 단계"
            : "TDA 전처리를 사용하지 않아 data.jpg와 일반 모델을 사용하도록 준비하는 단계",
        6 => useTda
            ? "main_2.py가 TDA 처리 이미지와 TDA 모델을 사용해 매설물 후보, 신뢰도와 좌표를 계산하는 단계"
            : "main_2.py가 일반 이미지와 일반 모델을 사용해 매설물 후보, 신뢰도와 좌표를 계산하는 단계",
        7 => "생성된 CSV를 읽고 결과 이미지를 불러온 뒤 결과를 화면과 결과 폴더에 반영하는 마지막 단계",
        _ => string.Empty
    };

    /// <summary>
    /// GetStageVerification 데이터 조회
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string GetStageVerification(int stage, bool useTda) => stage switch
    {
        1 => "필수 파일 누락, 실행 프로그램 버전 및 접근 권한 오류가 있으면 즉시 [오류]로 표시",
        2 => "작업 폴더 생성 여부와 이전 결과 제거 여부 확인",
        3 => "DZT 복사본과 input_info.txt/model_info.txt에 실제 입력값이 기록됐는지 확인",
        4 => "main_1.py 종료 코드와 data.jpg 생성 여부 확인",
        5 => useTda
            ? "Julia 종료 코드와 data.png 생성 여부 확인. 실패하면 일반 모델 대체 여부도 표시"
            : "TDA가 실행되지 않았고 일반 모델 경로로 전환됐는지 확인",
        6 => "main_2.py 종료 코드, 선택된 모델 파일 및 CSV 생성 여부 확인",
        7 => "CSV 행 수, 결과 이미지, 저장 경로와 화면 반영 여부 확인",
        _ => string.Empty
    };

    /// <summary>
    /// ClassifyProgress 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ClassifyProgress(string message)
    {
        if (HasErrorMeaning(message))
        {
            return $"[오류] {message}";
        }
        if (ContainsAny(message, "[stderr]", "경고", "warning", "대체", "건너", "fallback"))
        {
            return $"[경고] {message}";
        }
        return GetProgressStage(message) > 0 ? $"[단계] {message}" : $"[정보] {message}";
    }

    /// <summary>
    /// ClassifyCompletion 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ClassifyCompletion(string message) =>
        HasErrorMeaning(message)
            ? "[분석 실패]"
            : "[분석 완료]";

    /// <summary>
    /// ClassifyReportLine 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string ClassifyReportLine(string line)
    {
        if (HasErrorMeaning(line) || ContainsAny(line, "누락"))
        {
            return $"[오류 확인] {line}";
        }
        if (ContainsAny(line, "경고", "대체", "fallback", "warning"))
        {
            return $"[경고 확인] {line}";
        }
        return line;
    }

    /// <summary>
    /// ContainsAny 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// IsExplicitNoErrorMessage 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsExplicitNoErrorMessage(string value)
    {
        if (Regex.IsMatch(
                value,
                @"\bexit\s+code\s*[:=]\s*0\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        return ContainsAny(
            value,
            "오류 없음",
            "에러 없음",
            "실패 없음",
            "누락 없음",
            "문제 없음",
            "no error",
            "error: none");
    }

    /// <summary>
    /// HasErrorMeaning 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool HasErrorMeaning(string value)
    {
        if (IsExplicitNoErrorMessage(value))
        {
            return false;
        }

        return ContainsAny(value, "오류", "에러", "실패", "error", "exception", "exit code");
    }

    /// <summary>
    /// IsErrorLine 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsErrorLine(string line)
    {
        if (line.TrimStart().StartsWith("확인:", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsExplicitNoErrorMessage(line))
        {
            return false;
        }

        return ContainsAny(
            line,
            "[오류]",
            "[오류 확인]",
            "[분석 실패]",
            "traceback",
            "exception",
            "oserror",
            "filenotfounderror",
            "winerror",
            "fatal:",
            "error:",
            "오류:",
            "에러:",
            "예외:",
            "실패");
    }

    /// <summary>
    /// GetApplicationVersion 데이터 조회
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    /// <summary>
    /// Write 데이터 기록
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Write(string text)
    {
        Write(text, false);
    }

    /// <summary>
    /// WriteError 데이터 기록
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void WriteError(string text)
    {
        Write(text, true);
    }

    /// <summary>
    /// Write 데이터 기록
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Write(string text, bool forceError)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var wasAtBottom = IsOutputAtBottom();
        isWritingLog = true;
        foreach (var line in normalized.Split('\n'))
        {
            var run = new Run(line);
            if (forceError || IsErrorLine(line))
            {
                run.Foreground = ErrorTextBrush;
            }
            else if (ContainsAny(line, "[경고]", "[stderr]"))
            {
                run.Foreground = WarningTextBrush;
            }
            else if (ContainsAny(line, "[단계]", "[실행]", "[종료]"))
            {
                run.Foreground = StageTextBrush;
            }

            OutputParagraph.Inlines.Add(run);
            OutputParagraph.Inlines.Add(new LineBreak());
            logLineCount++;
        }
        isWritingLog = false;
        TrimOldLogLines();

        if (AutoScrollCheckBox.IsChecked == true && wasAtBottom)
        {
            OutputTextBox.ScrollToEnd();
        }

        UpdateLogStatus();
    }

    /// <summary>
    /// IsOutputAtBottom 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private bool IsOutputAtBottom() =>
        outputScrollableHeight <= 0 ||
        outputVerticalOffset >= outputScrollableHeight - 2;

    /// <summary>
    /// OutputTextBox_ScrollChanged 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OutputTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        outputVerticalOffset = e.VerticalOffset;
        outputScrollableHeight = Math.Max(0, e.ExtentHeight - e.ViewportHeight);

        if (!isWritingLog &&
            e.ExtentHeightChange == 0 &&
            e.VerticalChange < 0 &&
            AutoScrollCheckBox.IsChecked == true)
        {
            AutoScrollCheckBox.IsChecked = false;
        }

        UpdateLogStatus();
    }

    /// <summary>
    /// AutoScrollCheckBox_Changed 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void AutoScrollCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoScrollCheckBox.IsChecked == true && IsLoaded)
        {
            OutputTextBox.ScrollToEnd();
        }

        UpdateLogStatus();
    }

    /// <summary>
    /// ScrollToLatest_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ScrollToLatest_Click(object sender, RoutedEventArgs e)
        => ScrollToLatest();

    /// <summary>
    /// ScrollToLatest 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ScrollToLatest()
    {
        AutoScrollCheckBox.IsChecked = true;
        OutputTextBox.ScrollToEnd();
        UpdateLogStatus();
    }

    /// <summary>
    /// CopyAllLog_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CopyAllLog_Click(object sender, RoutedEventArgs e)
        => CopyAllLog();

    /// <summary>
    /// CopyAllLog 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void CopyAllLog()
    {
        var range = new TextRange(
            OutputTextBox.Document.ContentStart,
            OutputTextBox.Document.ContentEnd);
        var text = range.Text.TrimEnd('\r', '\n');
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
            LogStatusText.Text = $"{logLineCount:N0}줄 · 전체 로그 복사됨";
        }
    }

    /// <summary>
    /// ToggleAutoFollow_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ToggleAutoFollow_Click(object sender, RoutedEventArgs e)
        => ToggleAutoFollow();

    /// <summary>
    /// ToggleAutoFollow 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ToggleAutoFollow()
    {
        AutoScrollCheckBox.IsChecked = AutoScrollCheckBox.IsChecked != true;
        if (AutoScrollCheckBox.IsChecked == true)
        {
            OutputTextBox.ScrollToEnd();
        }

        UpdateLogStatus();
    }

    /// <summary>
    /// UpdateLogStatus 상태 갱신
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void UpdateLogStatus()
    {
        if (LogStatusText is null || OutputTextBox is null)
        {
            return;
        }

        var location = IsOutputAtBottom() ? "최신 로그" : "이전 로그 확인 중";
        LogStatusText.Text = $"{logLineCount:N0}줄 · {location}";
    }

    /// <summary>
    /// TrimOldLogLines 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void TrimOldLogLines()
    {
        if (logLineCount <= MaximumVisibleLogLines)
        {
            return;
        }

        var linesToRemove = Math.Min(LogTrimBatchLines, logLineCount);
        for (var index = 0; index < linesToRemove * 2 && OutputParagraph.Inlines.FirstInline is not null; index++)
        {
            OutputParagraph.Inlines.Remove(OutputParagraph.Inlines.FirstInline);
        }

        logLineCount -= linesToRemove;
    }

    /// <summary>
    /// TryPair 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryPair(string answer, string currentFirst, string currentSecond,
        out string first, out string second)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            first = currentFirst;
            second = currentSecond;
            return true;
        }
        var parts = answer.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && IsNumber(parts[0]) && IsNumber(parts[1]))
        {
            first = parts[0];
            second = parts[1];
            return true;
        }
        first = second = string.Empty;
        return false;
    }

    /// <summary>
    /// TrySingle 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TrySingle(string answer, string current, out string value)
    {
        value = string.IsNullOrWhiteSpace(answer) ? current : answer;
        return TryParseFinite(value, out var parsed) && parsed is >= 0 and <= 1;
    }

    /// <summary>
    /// IsNumber 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool IsNumber(string value) =>
        TryParseFinite(value, out _);

    /// <summary>
    /// TryParseFinite 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryParseFinite(string value, out double result)
    {
        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                     double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        return parsed && double.IsFinite(result);
    }

    /// <summary>
    /// TryYesNo 처리 가능 여부 확인
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static bool TryYesNo(string answer, bool current, out bool value)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            value = current;
            return true;
        }
        if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer == "예")
        {
            value = true;
            return true;
        }
        if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer == "아니오")
        {
            value = false;
            return true;
        }
        value = current;
        return false;
    }

    /// <summary>
    /// WizardStep 상태 선택지 정의
    /// 허용 상태를 제한해 분기 기준의 일관성 확보
    /// </summary>
    private enum WizardStep
    {
        ScanFile,
        Map,
        StartPoint,
        DirectionPoint,
        Range,
        Scale,
        Threshold,
        UseTda,
        TdaThreshold,
        Confirm
    }
}
