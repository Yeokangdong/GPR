namespace GprPrediction.Wpf.Contracts;

/// <summary>
/// ViewModel이 구체적인 WPF Window 형식을 알지 않고 화면 전환을 요청하는 계약입니다.
/// </summary>
public interface IWindowService
{
    void ShowMapDialog(object dataContext);

    void ShowPrintDialog(object dataContext);

    void ShowInputDialog(object dataContext);

    void ShowCommand(object dataContext);
}
