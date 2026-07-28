using System.Collections.ObjectModel;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 병합 대상 SEN 파일 1개와 그 안에서 선택된 점 정보를 함께 보관
/// </summary>
public sealed class MergeResultFileItem : ObservableObject
{
    private bool isSelected;
    private SavedResultPoint? selectedPoint;

    public string FileName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public int PointCount { get; init; }

    public ObservableCollection<SavedResultPoint> AvailablePoints { get; init; } = [];

    /// <summary>
    /// 이 파일을 병합에 포함할지 여부를 나타냄
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    /// <summary>
    /// 현재 파일에서 병합 기준으로 선택된 점을 저장
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
    /// </summary>
    public string SummaryText => $"{PointCount}개 점";

    /// <summary>
    /// 선택된 점이 있으면 병합용 표시 문자열을, 없으면 안내 문구를 반환
    /// </summary>
    public string PointSummaryText => SelectedPoint?.MergeDisplayText ?? "점 선택 없음";
}
