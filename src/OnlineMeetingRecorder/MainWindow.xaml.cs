using System.ComponentModel;
using System.Windows;
using OnlineMeetingRecorder.Helpers;
using OnlineMeetingRecorder.Services;
using OnlineMeetingRecorder.ViewModels;

namespace OnlineMeetingRecorder;

public partial class MainWindow : Window
{
    private readonly SystemTrayService _trayService;

    public MainWindow(SystemTrayService trayService)
    {
        _trayService = trayService;
        InitializeComponent();
        AppIconHelper.ApplyIcon(this);
    }

    private void GadgetButton_Click(object sender, RoutedEventArgs e)
    {
        _trayService.ShowGadgetWindow();
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
        // トレイ終了でない場合はトレイに最小化
        if (!_trayService.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _trayService.NotifyMinimizedToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // MainViewModel.Dispose() は App.OnExit() で一括実行（二重呼び出し防止）
        base.OnClosed(e);
    }
}
