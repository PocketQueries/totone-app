using System.Windows;
using System.Windows.Media.Animation;

namespace OnlineMeetingRecorder.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ステータステキストを更新（起動処理の進捗表示用）
    /// </summary>
    public void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    /// <summary>
    /// フェードアウトアニメーション後にウィンドウを閉じる
    /// </summary>
    public void CloseWithFade()
    {
        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
