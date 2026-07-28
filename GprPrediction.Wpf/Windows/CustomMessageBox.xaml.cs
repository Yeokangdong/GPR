using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GprPrediction.Wpf.Windows;

/// <summary>
/// CustomMessageBox 관련 상태와 동작 관리
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public partial class CustomMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private MessageBoxResult _closeResult = MessageBoxResult.OK;

    /// <summary>
    /// CustomMessageBox 인스턴스 초기화
    /// 필수 의존성과 초기 상태를 생성 시점에 확정
    /// </summary>
    private CustomMessageBox(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        CaptionText.Text = string.IsNullOrWhiteSpace(caption) ? "GPR" : caption;
        MessageText.Text = message ?? string.Empty;
        ConfigureIcon(image);
        ConfigureButtons(buttons);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Show 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new CustomMessageBox(message, caption, buttons, image);
        var activeWindow = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window != dialog);

        if (activeWindow is not null)
        {
            dialog.Owner = activeWindow;
        }

        dialog.ShowDialog();
        return dialog._result == MessageBoxResult.None ? dialog._closeResult : dialog._result;
    }

    /// <summary>
    /// ConfigureIcon 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ConfigureIcon(MessageBoxImage image)
    {
        (IconText.Text, IconBackground.Background) = image switch
        {
            MessageBoxImage.Error => ("×", BrushFrom("#F04464")),
            MessageBoxImage.Warning => ("!", BrushFrom("#F5A623")),
            MessageBoxImage.Question => ("?", BrushFrom("#5B8FF9")),
            MessageBoxImage.Information => ("i", BrushFrom("#21B8A6")),
            _ => ("i", BrushFrom("#5B8FF9"))
        };
    }

    /// <summary>
    /// ConfigureButtons 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ConfigureButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("확인", MessageBoxResult.OK, true);
                AddButton("취소", MessageBoxResult.Cancel, false);
                _closeResult = MessageBoxResult.Cancel;
                break;
            case MessageBoxButton.YesNo:
                AddButton("예", MessageBoxResult.Yes, true);
                AddButton("아니요", MessageBoxResult.No, false);
                _closeResult = MessageBoxResult.No;
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("예", MessageBoxResult.Yes, true);
                AddButton("아니요", MessageBoxResult.No, false);
                AddButton("취소", MessageBoxResult.Cancel, false);
                _closeResult = MessageBoxResult.Cancel;
                break;
            default:
                AddButton("확인", MessageBoxResult.OK, true);
                _closeResult = MessageBoxResult.OK;
                break;
        }
    }

    /// <summary>
    /// AddButton 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void AddButton(string text, MessageBoxResult result, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 82,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 7, 16, 7),
            IsDefault = primary,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Tag = result
        };
        button.Click += ResultButton_Click;
        ButtonPanel.Children.Add(button);
    }

    /// <summary>
    /// BrushFrom 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private static Brush BrushFrom(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    /// <summary>
    /// ResultButton_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void ResultButton_Click(object sender, RoutedEventArgs e)
    {
        _result = (MessageBoxResult)((Button)sender).Tag;
        Close();
    }

    /// <summary>
    /// Copy_Click 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(MessageText.Text);
    }

    /// <summary>
    /// Close_Click 화면 닫기
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = _closeResult;
        Close();
    }

    /// <summary>
    /// TitleBar_MouseLeftButtonDown 처리 수행
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// OnPreviewKeyDown 이벤트 처리
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result = _closeResult;
            Close();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                 && string.IsNullOrEmpty(MessageText.SelectedText))
        {
            Clipboard.SetText(MessageText.Text);
            e.Handled = true;
        }
    }
}
