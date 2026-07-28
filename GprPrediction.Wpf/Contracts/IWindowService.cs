namespace GprPrediction.Wpf.Contracts;

/// <summary>
/// ViewModel이 구체적인 WPF Window 형식을 알지 않고 화면 전환을 요청하는 계약
/// 구현과 사용 계층의 결합을 낮춰 대체 가능성 확보
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// ShowMapDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    void ShowMapDialog(object dataContext);

    /// <summary>
    /// ShowPrintDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    void ShowPrintDialog(object dataContext);

    /// <summary>
    /// ShowInputDialog 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    void ShowInputDialog(object dataContext);

    /// <summary>
    /// ShowCommand 화면 표시
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    void ShowCommand(object dataContext);
}
