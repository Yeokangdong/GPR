using System.Globalization;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// CSV에서 읽은 분석 결과 1건을 거리, 심도, 신뢰도와 함께 보관
/// </summary>
public sealed class PredictionResult
{
    public int Index { get; init; }

    public int SourceIndex { get; init; }

    public double DistanceMeters { get; init; }

    public double DepthMeters { get; init; }

    public double ConfidenceRatio { get; init; }

    /// <summary>
    /// 메인 화면 표시용 거리 문자열
    /// </summary>
    public string Distance => DistanceMeters.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// 메인 화면 표시용 심도 문자열
    /// </summary>
    public string Depth => DepthMeters.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// 퍼센트 기준 신뢰도 문자열
    /// </summary>
    public string Confidence => (ConfidenceRatio * 100).ToString("0.00", CultureInfo.CurrentCulture);

    public string RawLine { get; init; } = string.Empty;

    /// <summary>
    /// Top View 범례에 표시할 문구를 생성
    /// </summary>
    public string TopViewText => $"{Index:00}(#{SourceIndex:00}): {Distance}(m)";

    /// <summary>
    /// Front View 범례에 표시할 문구를 생성
    /// </summary>
    public string FrontViewText => $"{Index:00}(#{SourceIndex:00}): {Depth}(m)";
}
