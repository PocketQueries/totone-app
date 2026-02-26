using System.Windows;
using OnlineMeetingRecorder.Helpers;
using OnlineMeetingRecorder.ViewModels;

namespace OnlineMeetingRecorder.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        AppIconHelper.ApplyIcon(this);

        // PasswordBox はバインディング非対応のため手動で同期
        ApiKeyBox.Password = viewModel.OpenAiApiKey;
        ApiKeyBox.PasswordChanged += (_, _) => viewModel.OpenAiApiKey = ApiKeyBox.Password;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
