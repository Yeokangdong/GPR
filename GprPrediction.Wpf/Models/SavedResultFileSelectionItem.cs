using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 저장된 SEN 결과 파일 목록에서 한 항목의 표시 정보와 체크 상태를 보관
/// </summary>
public sealed class SavedResultFileSelectionItem : ObservableObject
{
    private bool isSelected;

    public string FileName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public string DisplayPath { get; init; } = string.Empty;

    public string ModifiedText { get; init; } = string.Empty;

    public string FileSizeText { get; init; } = string.Empty;

    /// <summary>
    /// 현재 파일을 불러오기 대상으로 선택했는지 여부를 나타냄
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
