using System.Globalization;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// SEN 파일에서 읽은 저장 결과 점 1건과 라벨 표시 위치를 함께 보관
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class SavedResultPoint
{
    /// <summary>
    /// Label 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// SourceName 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string SourceName { get; init; } = string.Empty;

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
    /// ScreenX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double ScreenX { get; init; }

    /// <summary>
    /// ScreenY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double ScreenY { get; init; }

    /// <summary>
    /// LabelOffsetX 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double LabelOffsetX { get; init; }

    /// <summary>
    /// LabelOffsetY 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public double LabelOffsetY { get; init; }

    /// <summary>
    /// 지도 마우스 오버 시 보여줄 툴팁 문자열
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string Tooltip => string.Create(
        CultureInfo.CurrentCulture,
        $"{SourceName} / {Label} / Z {DepthMeters:0.00}m");

    /// <summary>
    /// 병합 창 콤보박스에서 사용할 간단한 표시 문자열
    /// 호출 흐름을 분리해 변경 영향과 중복 처리 최소화
    /// </summary>
    public string MergeDisplayText => string.Create(
        CultureInfo.CurrentCulture,
        $"{Label}-{DepthMeters:0.00}(m)");
}
