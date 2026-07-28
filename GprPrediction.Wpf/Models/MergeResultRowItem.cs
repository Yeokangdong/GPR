using System.Collections.ObjectModel;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 병합 창의 한 행을 표현하며, 여러 결과 파일에서 대응 점을 가로로 묶기
/// </summary>
public sealed class MergeResultRowItem : ObservableObject
{
    private bool isSelected;

    public string RowLabel { get; init; } = string.Empty;

    public ObservableCollection<MergeResultRowChoice> Choices { get; init; } = [];

    /// <summary>
    /// 이 행을 최종 병합 대상으로 사용할지 여부를 나타냄
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
