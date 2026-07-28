using System.ComponentModel;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 맵 목록에 표시할 항목 정보와 현재 선택 상태를 보관
/// </summary>
public sealed class MapEntry : INotifyPropertyChanged
{
    private bool isSelected;

    public required string Name { get; init; }

    public required string FilePath { get; init; }

    /// <summary>
    /// 현재 맵이 UI에서 선택되어 있는지 여부를 나타냄
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
