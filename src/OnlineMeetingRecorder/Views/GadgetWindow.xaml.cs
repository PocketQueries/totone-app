using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OnlineMeetingRecorder.Services;
using OnlineMeetingRecorder.ViewModels;

namespace OnlineMeetingRecorder.Views;

public partial class GadgetWindow : Window
{
    private readonly SystemTrayService _trayService;
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public GadgetWindow(SystemTrayService trayService)
    {
        _trayService = trayService;
        InitializeComponent();
        Helpers.AppIconHelper.ApplyIcon(this);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Recording.StartRecordingCommand.CanExecute(null) == true)
            ViewModel.Recording.StartRecordingCommand.Execute(null);
    }

    private void ToggleDevicePanel_Click(object sender, RoutedEventArgs e)
    {
        DevicePanel.Visibility = DevicePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Recording.TogglePauseCommand.CanExecute(null) == true)
            ViewModel.Recording.TogglePauseCommand.Execute(null);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Recording.StopRecordingCommand.CanExecute(null) == true)
            ViewModel.Recording.StopRecordingCommand.Execute(null);
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _trayService.ShowMainWindow();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // 最小化時はトレイに格納
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _trayService.NotifyMinimizedToTray();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // トレイ終了でない場合はトレイに格納
        if (!_trayService.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _trayService.NotifyMinimizedToTray();
            return;
        }

        base.OnClosing(e);
    }
}
