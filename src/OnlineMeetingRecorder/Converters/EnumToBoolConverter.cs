using System.Globalization;
using System.Windows.Data;

namespace OnlineMeetingRecorder.Converters;

/// <summary>
/// Enum値とBoolを相互変換するコンバーター（RadioButton用）
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.Equals(parameter) ?? false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? parameter : Binding.DoNothing;
    }
}
