using System.Collections.ObjectModel;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 병합 대상 SEN 파일 1개와 그 안에서 선택된 점 정보를 함께 보관
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class MergeResultFileItem : ObservableObject
{
    private bool isSelected;
    private SavedResultPoint? selectedPoint;

    /// <summary>
    /// FileName 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// FilePath 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// PointCount 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public int PointCount { get; init; }

    /// <summary>
    /// AvailablePoints 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<SavedResultPoint> AvailablePoints { get; init; } = [];

    /// <summary>
    /// 이 파일을 병합에 포함할지 여부를 나타냄
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    /// <summary>
    /// 현재 파일에서 병합 기준으로 선택된 점을 저장
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public SavedResultPoint? SelectedPoint
    {
        get => selectedPoint;
        set
        {
            if (SetProperty(ref selectedPoint, value))
            {
                OnPropertyChanged(nameof(PointSummaryText));
            }
        }
    }

    /// <summary>
    /// 파일 안에 포함된 점 개수를 짧은 텍스트로 반환
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string SummaryText => $"{PointCount}개 점";

    /// <summary>
    /// 선택된 점이 있으면 병합용 표시 문자열을, 없으면 안내 문구를 반환
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string PointSummaryText => SelectedPoint?.MergeDisplayText ?? "점 선택 없음";
}
