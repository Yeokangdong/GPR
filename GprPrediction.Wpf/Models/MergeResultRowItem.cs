using System.Collections.ObjectModel;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 병합 창의 한 행을 표현하며, 여러 결과 파일에서 대응 점을 가로로 묶기
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class MergeResultRowItem : ObservableObject
{
    private bool isSelected;

    /// <summary>
    /// RowLabel 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string RowLabel { get; init; } = string.Empty;

    /// <summary>
    /// Choices 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<MergeResultRowChoice> Choices { get; init; } = [];

    /// <summary>
    /// 이 행을 최종 병합 대상으로 사용할지 여부를 나타냄
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
