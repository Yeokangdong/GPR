namespace GprPrediction.Wpf.Models;

/// <summary>
/// 분석 결과 한 점을 지도 좌표와 화면 좌표 기준으로 함께 표현
/// </summary>
public sealed class MapPoint
{
    public int Index { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double DepthMeters { get; init; }

    public double ConfidenceRatio { get; init; }

    public double ScreenX { get; set; }

    public double ScreenY { get; set; }

    /// <summary>
    /// 지도 위에 표시할 간단한 번호 라벨을 생성
    /// </summary>
    public string Label => $"#{Index:00}";
}
