using System.ComponentModel;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 맵 목록에 표시할 항목 정보와 현재 선택 상태를 보관
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class MapEntry : INotifyPropertyChanged
{
    private bool isSelected;

    /// <summary>
    /// Name 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// FilePath 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 현재 맵이 UI에서 선택되어 있는지 여부를 나타냄
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
