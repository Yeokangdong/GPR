using System.Collections.ObjectModel;
using GprPrediction.Wpf.ViewModels;

namespace GprPrediction.Wpf.Models;

/// <summary>
/// 병합 행의 한 열에서 선택 가능한 점 목록과 현재 선택 점을 표현
/// 관련 책임을 한곳에 모아 구조와 수명 경계 명확화
/// </summary>
public sealed class MergeResultRowChoice : ObservableObject
{
    private SavedResultPoint? selectedPoint;

    /// <summary>
    /// AvailablePoints 값 제공
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public ObservableCollection<SavedResultPoint> AvailablePoints { get; init; } = [];

    /// <summary>
    /// 현재 열에서 사용자가 선택한 점을 저장
    /// UI 바인딩과 내부 상태가 같은 값을 공유하도록 유지
    /// </summary>
    public SavedResultPoint? SelectedPoint
    {
        get => selectedPoint;
        set => SetProperty(ref selectedPoint, value);
    }
}
