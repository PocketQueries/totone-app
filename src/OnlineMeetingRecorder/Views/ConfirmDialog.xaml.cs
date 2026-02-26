using System.Windows;

namespace OnlineMeetingRecorder.Views;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Totonoeデザインの確認ダイアログを表示する
    /// </summary>
    public static bool Show(
        Window? owner,
        string title,
        string message,
        string confirmText = "削除する",
        string cancelText = "キャンセル",
        string icon = "⚠",
        bool showCopyButton = false)
    {
        var dialog = new ConfirmDialog();
        dialog.Owner = owner;
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        dialog.IconText.Text = icon;

        if (showCopyButton)
        {
            dialog.CopyButton.Visibility = Visibility.Visible;
        }

        if (string.IsNullOrEmpty(cancelText))
        {
            dialog.CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            dialog.CancelButton.Content = cancelText;
        }

        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    private void OnConfirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(MessageText.Text);
            CopyButton.Content = "✓ コピー済み";
        }
        catch
        {
            // クリップボードアクセス失敗は無視
        }
    }
}
