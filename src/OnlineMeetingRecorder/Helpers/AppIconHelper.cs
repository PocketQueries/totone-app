using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OnlineMeetingRecorder.Helpers;

/// <summary>
/// app.ico からアプリアイコンを読み込んで Window に適用
/// </summary>
public static class AppIconHelper
{
    private static BitmapFrame? _cachedIcon;

    public static void ApplyIcon(Window window)
    {
        _cachedIcon ??= LoadIcon();
        if (_cachedIcon != null)
            window.Icon = _cachedIcon;
    }

    private static BitmapFrame? LoadIcon()
    {
        // 実行ファイルと同じディレクトリの app.ico を探す
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var icoPath = Path.Combine(exeDir, "app.ico");
        if (!File.Exists(icoPath))
            return null;

        using var stream = File.OpenRead(icoPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
    }
}
