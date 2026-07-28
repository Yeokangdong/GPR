using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 저장된 SEN 결과 파일 목록에서 한 항목의 표시 정보와 체크 상태를 보관
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class SavedResultFileSelectionItem : ObservableObject
{
    private bool isSelected;

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
    /// DisplayPath 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string DisplayPath { get; init; } = string.Empty;

    /// <summary>
    /// ModifiedText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string ModifiedText { get; init; } = string.Empty;

    /// <summary>
    /// FileSizeText 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public string FileSizeText { get; init; } = string.Empty;

    /// <summary>
    /// 현재 파일을 불러오기 대상으로 선택했는지 여부를 나타냄
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
