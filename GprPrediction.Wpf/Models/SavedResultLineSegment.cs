namespace GprPrediction.Wpf.Models;

/// <summary>
/// 저장 결과 점들을 잇는 화면용 선분 정보를 보관
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class SavedResultLineSegment
{
    /// <summary>
    /// X1 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double X1 { get; init; }

    /// <summary>
    /// Y1 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double Y1 { get; init; }

    /// <summary>
    /// X2 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double X2 { get; init; }

    /// <summary>
    /// Y2 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double Y2 { get; init; }
}
