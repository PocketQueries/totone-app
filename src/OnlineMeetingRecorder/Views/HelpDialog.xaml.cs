using System.Windows;

namespace OnlineMeetingRecorder.Views;

public partial class HelpDialog : Window
{
    public HelpDialog()
    {
        InitializeComponent();
    }

    /// <summary>ヘルプダイアログを表示する</summary>
    public static void ShowHelp(Window? owner)
    {
        var dialog = new HelpDialog { Owner = owner };
        dialog.ShowDialog();
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
