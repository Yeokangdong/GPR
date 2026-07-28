using System.Globalization;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// SEN 파일에서 읽은 저장 결과 점 1건과 라벨 표시 위치를 함께 보관
/// </summary>
public sealed class SavedResultPoint
{
    public string Label { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double DepthMeters { get; init; }

    public double ScreenX { get; init; }

    public double ScreenY { get; init; }

    public double LabelOffsetX { get; init; }

    public double LabelOffsetY { get; init; }

    /// <summary>
    /// 지도 마우스 오버 시 보여줄 툴팁 문자열
    /// </summary>
    public string Tooltip => string.Create(
        CultureInfo.CurrentCulture,
        $"{SourceName} / {Label} / Z {DepthMeters:0.00}m");

    /// <summary>
    /// 병합 창 콤보박스에서 사용할 간단한 표시 문자열
    /// </summary>
    public string MergeDisplayText => string.Create(
        CultureInfo.CurrentCulture,
        $"{Label}-{DepthMeters:0.00}(m)");
}
