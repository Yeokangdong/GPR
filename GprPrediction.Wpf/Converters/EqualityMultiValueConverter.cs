using System.Globalization;
using System.Windows.Data;

namespace GprPrediction.Wpf.Converters;

/// <summary>
/// 여러 바인딩 값의 문자열 동등 여부 비교
/// 선택 항목과 명령 매개변수의 공통 스타일 연결
/// </summary>
public sealed class EqualityMultiValueConverter : IMultiValueConverter
{
    /// <summary>
    /// 전달된 모든 값을 문자열 기준으로 비교
    /// 서로 다른 숫자 표현 형식의 선택 상태 비교 지원
    /// </summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        var first = System.Convert.ToString(values[0], CultureInfo.InvariantCulture);
        return values
            .Skip(1)
            .Select(value => System.Convert.ToString(value, CultureInfo.InvariantCulture))
            .All(value => string.Equals(first, value, StringComparison.Ordinal));
    }

    /// <summary>
    /// 선택 상태의 역방향 변환 차단
    /// 화면 표시 전용 비교 결과의 원본 값 변경 방지
    /// </summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
