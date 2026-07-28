using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GprPrediction.Wpf.Converters;

/// <summary>
/// true이면 Collapsed, false이면 Visible을 반환하는 단순 역방향 Visibility 변환기
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 불리언 값을 화면 표시 여부로 뒤집어 변환
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 역변환은 사용하지 않으므로 지원 안 함
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
