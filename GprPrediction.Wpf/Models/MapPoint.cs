namespace GprPrediction.Wpf.Models;

/// <summary>
/// 분석 결과 한 점을 지도 좌표와 화면 좌표 기준으로 함께 표현
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class MapPoint
{
    /// <summary>
    /// Index 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// X 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Y 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// DepthMeters 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double DepthMeters { get; init; }

    /// <summary>
    /// ConfidenceRatio 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double ConfidenceRatio { get; init; }

    /// <summary>
    /// ScreenX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double ScreenX { get; set; }

    /// <summary>
    /// ScreenY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double ScreenY { get; set; }

    /// <summary>
    /// 지도 위에 표시할 간단한 번호 라벨을 생성
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string Label => $"#{Index:00}";
}
