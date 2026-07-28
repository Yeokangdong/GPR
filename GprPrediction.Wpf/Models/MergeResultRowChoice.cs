using System.Collections.ObjectModel;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 병합 행의 한 열에서 선택 가능한 점 목록과 현재 선택 점을 표현
/// </summary>
public sealed class MergeResultRowChoice : ObservableObject
{
    private SavedResultPoint? selectedPoint;

    public ObservableCollection<SavedResultPoint> AvailablePoints { get; init; } = [];

    /// <summary>
    /// 현재 열에서 사용자가 선택한 점을 저장
    /// </summary>
    public SavedResultPoint? SelectedPoint
    {
        get => selectedPoint;
        set => SetProperty(ref selectedPoint, value);
    }
}
