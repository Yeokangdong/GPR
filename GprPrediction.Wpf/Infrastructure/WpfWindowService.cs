using System.Windows;
using GprPrediction.Wpf.Contracts;
using GprPrediction.Wpf.Windows;

namespace GprPrediction.Wpf.Infrastructure;

/// <summary>
/// WPF Window 생성과 Owner 연결을 담당하는 View 계층 어댑터입니다.
/// </summary>
public sealed class WpfWindowService : IWindowService
{
    public void ShowMapDialog(object dataContext)
    {
        var window = new MapViewWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.ShowDialog();
    }

    public void ShowPrintDialog(object dataContext)
    {
        var window = new PrintWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.ShowDialog();
    }

    public void ShowInputDialog(object dataContext)
    {
        var window = new InputWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.ShowDialog();
    }

    public void ShowCommand(object dataContext)
    {
        var window = new CommandWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.Show();
    }
}
