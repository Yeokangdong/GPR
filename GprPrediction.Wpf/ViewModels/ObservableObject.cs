using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GprPrediction.Wpf.ViewModels;

/// <summary>
/// 속성 변경 알림이 필요한 ViewModel과 모델의 공통 기반 클래스를 제공
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 필드 값을 비교한 뒤 변경된 경우에만 값을 갱신하고 PropertyChanged를 발생
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// 값 대입 없이 강제로 속성 변경 알림만 다시 보내야 할 때 사용
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
