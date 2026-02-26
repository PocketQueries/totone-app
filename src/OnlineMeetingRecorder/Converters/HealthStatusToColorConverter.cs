using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Converters;

public class HealthStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is HealthStatus status)
        {
            return status switch
            {
                HealthStatus.Healthy => new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),     // Success Green
                HealthStatus.Silent => new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),      // Warning Orange
                HealthStatus.Clipping => new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),    // Warning Orange
                HealthStatus.DataStalled => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)), // Error Red
                HealthStatus.DeviceDisconnected => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                HealthStatus.WriteError => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                _ => new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C))
            };
        }
        return new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s == "Inverse";
        if (value is bool b)
        {
            if (invert) b = !b;
            return b ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BooleanAndMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.All(v => v is bool b && b);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SessionStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SessionStatus status)
        {
            return status switch
            {
                SessionStatus.Recording => "録音中",
                SessionStatus.Completed => "録音済み",
                SessionStatus.Transcribing => "文字起こし中",
                SessionStatus.Transcribed => "文字起こし済み",
                SessionStatus.Error => "エラー",
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 通信モード（オンライン/オフライン）をアイコンに変換。
/// true=オンラインモード（API通信あり）、false=オフラインモード（ローカル処理）
/// </summary>
public class NetworkStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isOnline)
            return isOnline ? "🌐" : "💻";
        return "❓";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// セグメントインデックスとハイライトインデックスを比較して背景色を決定する。
/// parameter にインデックスを渡し、一致したらハイライト色を返す。
/// </summary>
public class SpeakerToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string speaker)
        {
            return speaker == "mic"
                ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))    // 自分: ダーク
                : new SolidColorBrush(Color.FromRgb(0x2D, 0x20, 0x28));   // 相手: Toki-Pink薄暗
        }
        return new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 話者を表示アイコンに変換する
/// </summary>
public class SpeakerToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string speaker)
            return speaker == "mic" ? "🎙" : "🔊";
        return "💬";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 話者を表示名に変換する
/// </summary>
public class SpeakerToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string speaker)
            return speaker == "mic" ? "自分" : "相手";
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// TimeSpan を mm:ss 形式に変換する
/// </summary>
public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"mm\:ss");
        }
        return "00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 二つのオブジェクトの等価性を判定する。セッション選択状態のハイライトに使用。
/// </summary>
public class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] != null && values[1] != null)
            return values[0].Equals(values[1]);
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
