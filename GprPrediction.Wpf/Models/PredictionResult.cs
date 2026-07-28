using System.Globalization;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// CSV에서 읽은 분석 결과 1건을 거리, 심도, 신뢰도와 함께 보관
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class PredictionResult
{
    /// <summary>
    /// Index 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// SourceIndex 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public int SourceIndex { get; init; }

    /// <summary>
    /// DistanceMeters 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double DistanceMeters { get; init; }

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
    /// 메인 화면 표시용 거리 문자열
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string Distance => DistanceMeters.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// 메인 화면 표시용 심도 문자열
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string Depth => DepthMeters.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// 퍼센트 기준 신뢰도 문자열
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string Confidence => (ConfidenceRatio * 100).ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// RawLine 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string RawLine { get; init; } = string.Empty;

    /// <summary>
    /// Top View 범례에 표시할 문구를 생성
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string TopViewText => $"{Index:00}(#{SourceIndex:00}): {Distance}(m)";

    /// <summary>
    /// Front View 범례에 표시할 문구를 생성
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string FrontViewText => $"{Index:00}(#{SourceIndex:00}): {Depth}(m)";
}
