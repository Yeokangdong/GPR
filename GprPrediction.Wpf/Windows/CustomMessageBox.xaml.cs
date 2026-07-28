using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GprPrediction.Wpf.Windows;

public partial class CustomMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private MessageBoxResult _closeResult = MessageBoxResult.OK;

    private CustomMessageBox(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        CaptionText.Text = string.IsNullOrWhiteSpace(caption) ? "GPR" : caption;
        MessageText.Text = message ?? string.Empty;
        ConfigureIcon(image);
        ConfigureButtons(buttons);
        PreviewKeyDown += OnPreviewKeyDown;
    }

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

    private static Brush BrushFrom(string color) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private void ResultButton_Click(object sender, RoutedEventArgs e)
    {
        _result = (MessageBoxResult)((Button)sender).Tag;
        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(MessageText.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = _closeResult;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

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
