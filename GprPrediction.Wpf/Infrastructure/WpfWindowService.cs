using System.Windows;
using GprPrediction.Wpf.Contracts;
using GprPrediction.Wpf.Windows;

namespace GprPrediction.Wpf.Infrastructure;

/// <summary>
/// WPF Window 생성과 Owner 연결을 담당하는 View 계층 어댑터
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class WpfWindowService : IWindowService
{
    /// <summary>
    /// ShowMapDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void ShowMapDialog(object dataContext)
    {
        var window = new MapViewWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.ShowDialog();
    }

    /// <summary>
    /// ShowPrintDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void ShowPrintDialog(object dataContext)
    {
        var window = new PrintWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.ShowDialog();
    }

    /// <summary>
    /// ShowInputDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public void ShowInputDialog(object dataContext)
    {
        var window = new InputWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = dataContext
        };
        window.ShowDialog();
    }

    /// <summary>
    /// ShowCommand 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
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
