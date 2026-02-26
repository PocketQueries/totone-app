using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Audio;
using OnlineMeetingRecorder.Services.MeetingDetection;
using OnlineMeetingRecorder.Services.Settings;
using OnlineMeetingRecorder.ViewModels;

namespace OnlineMeetingRecorder.Services;

/// <summary>
/// システムトレイ（タスクトレイ）常駐サービス。
/// 録音状態をアイコンで表示し、コンテキストメニューから操作を提供する。
/// WPF Dispatcher スレッド上で NotifyIcon を生成・操作する前提。
/// </summary>
public class SystemTrayService : IDisposable
{
    private readonly RecordingViewModel _recording;
    private readonly IMeetingDetectionService _meetingDetection;
    private readonly ISettingsService _settingsService;
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private Window? _gadgetWindow;
    private Icon? _idleIcon;
    private Icon? _recordingIcon;
    private Icon? _pausedIcon;
    private ToolStripMenuItem? _startItem;
    private ToolStripMenuItem? _stopItem;
    private ToolStripMenuItem? _pauseItem;
    private ToolStripMenuItem? _gadgetItem;
    private bool _hasShownMinimizeNotification;
    private bool _isGadgetMode;

    /// <summary>バルーンクリック時に実行するアクション</summary>
    private enum BalloonAction { None, StartRecording, StopRecording }
    private BalloonAction _pendingBalloonAction;

    /// <summary>アプリケーション終了要求イベント</summary>
    public event EventHandler? ExitRequested;

    /// <summary>アプリケーション終了中かどうか</summary>
    public bool IsExiting { get; internal set; }

    /// <summary>ガジェットモード中かどうか</summary>
    public bool IsGadgetMode => _isGadgetMode;

    public SystemTrayService(
        RecordingViewModel recording,
        IMeetingDetectionService meetingDetection,
        ISettingsService settingsService)
    {
        _recording = recording;
        _meetingDetection = meetingDetection;
        _settingsService = settingsService;
    }

    /// <summary>
    /// メインウィンドウ作成後に呼び出してトレイアイコンを初期化する。
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        if (_notifyIcon != null) return; // already initialized

        _mainWindow = mainWindow;

        // WinForms コンテキストメニューの見た目を OS ネイティブに合わせる
        System.Windows.Forms.Application.EnableVisualStyles();

        CreateIcons();

        _notifyIcon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "Totonoe - 待機中",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreActiveWindow();
        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
        _notifyIcon.BalloonTipClosed += (_, _) => _pendingBalloonAction = BalloonAction.None;

        // 録音状態の変更を監視
        _recording.PropertyChanged += OnRecordingPropertyChanged;

        // 会議検知イベントを購読
        _meetingDetection.MeetingDetected += OnMeetingDetected;
        _meetingDetection.MeetingEnded += OnMeetingEnded;

        // 設定に応じて会議検知を開始
        if (_settingsService.Settings.MeetingDetectionEnabled)
        {
            _meetingDetection.StartMonitoring();
        }

        UpdateTrayState(_recording.RecorderState);
    }

    /// <summary>
    /// ガジェットウィンドウを登録する。
    /// </summary>
    public void InitializeGadgetWindow(Window gadgetWindow)
    {
        _gadgetWindow = gadgetWindow;
    }

    /// <summary>
    /// ガジェットモードに切り替える（メインウィンドウを非表示にしてガジェットウィンドウを表示）。
    /// </summary>
    public void ShowGadgetWindow()
    {
        if (_gadgetWindow == null) return;

        _mainWindow?.Hide();
        _isGadgetMode = true;
        _gadgetWindow.Show();
        _gadgetWindow.Activate();
        UpdateGadgetMenuItem();
    }

    /// <summary>
    /// 初期状態のガジェットモードを設定する（起動時に呼び出す）。
    /// </summary>
    public void SetGadgetMode(bool isGadget)
    {
        _isGadgetMode = isGadget;
        UpdateGadgetMenuItem();
    }

    /// <summary>
    /// ウィンドウがトレイに最小化されたことをバルーン通知する（初回のみ）。
    /// </summary>
    public void NotifyMinimizedToTray()
    {
        if (_notifyIcon == null || _hasShownMinimizeNotification) return;
        _hasShownMinimizeNotification = true;

        _notifyIcon.ShowBalloonTip(
            3000,
            "Totonoe",
            "タスクトレイに常駐しています。\nダブルクリックでウィンドウを表示します。",
            ToolTipIcon.Info);
    }

    #region Meeting Detection

    private void OnMeetingDetected(object? sender, MeetingApp app)
    {
        if (_notifyIcon == null) return;
        if (!_settingsService.Settings.MeetingDetectionEnabled) return;

        var appName = MeetingAppInfo.GetDisplayName(app);
        _pendingBalloonAction = BalloonAction.StartRecording;
        _notifyIcon.ShowBalloonTip(
            5000,
            "Totonoe - 会議を検知",
            $"{appName} の会議を検知しました。\nクリックで録音を開始します。",
            ToolTipIcon.Info);
    }

    private void OnMeetingEnded(object? sender, MeetingApp app)
    {
        if (_notifyIcon == null) return;
        if (!_recording.IsRecording) return;

        var appName = MeetingAppInfo.GetDisplayName(app);
        _pendingBalloonAction = BalloonAction.StopRecording;
        _notifyIcon.ShowBalloonTip(
            5000,
            "Totonoe - 会議の終了を検知",
            $"{appName} が終了しました。\nクリックで録音を停止します。",
            ToolTipIcon.Info);
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        var action = _pendingBalloonAction;
        _pendingBalloonAction = BalloonAction.None;

        switch (action)
        {
            case BalloonAction.StartRecording:
                if (_recording.StartRecordingWithDefaultsCommand.CanExecute(null))
                    _recording.StartRecordingWithDefaultsCommand.Execute(null);
                break;
            case BalloonAction.StopRecording:
                if (_recording.StopRecordingCommand.CanExecute(null))
                    _recording.StopRecordingCommand.Execute(null);
                break;
        }
    }

    #endregion

    private void OnRecordingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordingViewModel.RecorderState)) return;

        // NotifyIcon は WPF Dispatcher スレッド上で生成されるため、同スレッドで更新
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            UpdateTrayState(_recording.RecorderState);
        });
    }

    private void UpdateTrayState(RecorderState state)
    {
        if (_notifyIcon == null) return;

        (_notifyIcon.Icon, _notifyIcon.Text) = state switch
        {
            RecorderState.Recording => (_recordingIcon ?? _idleIcon, "Totonoe - 録音中"),
            RecorderState.Paused => (_pausedIcon ?? _idleIcon, "Totonoe - 一時停止中"),
            RecorderState.Stopping => (_recordingIcon ?? _idleIcon, "Totonoe - 停止処理中..."),
            _ => (_idleIcon, "Totonoe - 待機中")
        };

        UpdateMenuItems(state);

        // 会議検知サービスに録音状態を通知
        bool isActive = state == RecorderState.Recording || state == RecorderState.Paused;
        _meetingDetection.SetRecordingActive(isActive);

        // 録音停止時はガジェットモードを維持（自動復帰しない）
    }

    private void UpdateMenuItems(RecorderState state)
    {
        if (_startItem == null || _stopItem == null || _pauseItem == null) return;

        bool isIdle = state == RecorderState.Idle;
        bool isActive = state == RecorderState.Recording || state == RecorderState.Paused;

        _startItem.Visible = isIdle;
        _stopItem.Visible = isActive;
        _pauseItem.Visible = isActive;
        _pauseItem.Text = state == RecorderState.Paused ? "録音再開(&R)" : "一時停止(&P)";
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("ウィンドウを表示(&S)", null, (_, _) => ShowMainWindowInternal()));

        _gadgetItem = new ToolStripMenuItem("ガジェット表示(&G)", null, (_, _) =>
        {
            if (_isGadgetMode)
                ShowMainWindowInternal();
            else
                ShowGadgetWindow();
        });
        menu.Items.Add(_gadgetItem);

        menu.Items.Add(new ToolStripSeparator());

        _startItem = new ToolStripMenuItem("録音開始(&R)", null, (_, _) =>
        {
            ShowMainWindowInternal();
            if (_recording.StartRecordingCommand.CanExecute(null))
                _recording.StartRecordingCommand.Execute(null);
        });

        _stopItem = new ToolStripMenuItem("録音停止(&T)", null, (_, _) =>
        {
            if (_recording.StopRecordingCommand.CanExecute(null))
                _recording.StopRecordingCommand.Execute(null);
        });

        _pauseItem = new ToolStripMenuItem("一時停止(&P)", null, (_, _) =>
        {
            if (_recording.TogglePauseCommand.CanExecute(null))
                _recording.TogglePauseCommand.Execute(null);
        });

        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了(&X)", null, (_, _) => RequestExit()));

        return menu;
    }

    private void ShowMainWindowInternal()
    {
        if (_mainWindow == null) return;

        _gadgetWindow?.Hide();
        _isGadgetMode = false;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        UpdateGadgetMenuItem();
    }

    /// <summary>
    /// メインウィンドウを表示する（ガジェットウィンドウは非表示にする）。
    /// </summary>
    public void ShowMainWindow() => ShowMainWindowInternal();

    /// <summary>
    /// 最後にアクティブだったウィンドウを復元する（トレイからの復帰用）。
    /// </summary>
    private void RestoreActiveWindow()
    {
        if (_isGadgetMode)
            ShowGadgetWindow();
        else
            ShowMainWindowInternal();
    }

    /// <summary>ガジェット表示メニュー項目の表示を更新</summary>
    private void UpdateGadgetMenuItem()
    {
        if (_gadgetItem == null) return;
        _gadgetItem.Text = _isGadgetMode ? "通常ウィンドウに戻る(&G)" : "ガジェット表示(&G)";
    }

    private async void RequestExit()
    {
        // 録音中または停止処理中の場合は確認ダイアログを表示
        if (_recording.IsRecording || _recording.RecorderState == RecorderState.Stopping)
        {
            var result = System.Windows.MessageBox.Show(
                "録音中です。終了すると録音は停止されます。\n終了しますか？",
                "Totonoe",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // 録音を正常に停止してセッションを完了させる
            if (_recording.StopRecordingCommand.CanExecute(null))
                await _recording.StopRecordingCommand.ExecuteAsync(null);
        }

        IsExiting = true;
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    #region Icon Generation

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void CreateIcons()
    {
        var baseIcon = LoadAppIcon();
        _idleIcon = baseIcon ?? CreateOwnedIcon(Color.FromArgb(107, 203, 119));
        _recordingIcon = CreateOverlayIcon(baseIcon, Color.FromArgb(231, 76, 60));
        _pausedIcon = CreateOverlayIcon(baseIcon, Color.FromArgb(230, 126, 34));
    }

    private static Icon? LoadAppIcon()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var icoPath = Path.Combine(exeDir, "app.ico");
        if (!File.Exists(icoPath)) return null;
        return new Icon(icoPath, 16, 16);
    }

    /// <summary>ベースアイコンに状態表示用の色付きドットをオーバーレイする。</summary>
    private static Icon CreateOverlayIcon(Icon? baseIcon, Color dotColor)
    {
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (baseIcon != null)
        {
            using var baseBitmap = baseIcon.ToBitmap();
            g.DrawImage(baseBitmap, 0, 0, 16, 16);
        }
        else
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(107, 203, 119));
            g.FillEllipse(bgBrush, 1, 1, 14, 14);
        }

        // 右下に状態インジケータのドット（白枠付き）
        using var brush = new SolidBrush(dotColor);
        using var pen = new Pen(Color.White, 1.5f);
        g.FillEllipse(brush, 8, 8, 7, 7);
        g.DrawEllipse(pen, 8, 8, 7, 7);

        return CloneAndDestroyHandle(bitmap);
    }

    /// <summary>app.ico が見つからない場合のフォールバックアイコンを生成。</summary>
    private static Icon CreateOwnedIcon(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);

        using var micPen = new Pen(Color.White, 1.5f);
        g.DrawLine(micPen, 8, 3, 8, 9);
        g.DrawArc(micPen, 5, 6, 6, 5, 0, 180);
        g.DrawLine(micPen, 8, 11, 8, 13);

        return CloneAndDestroyHandle(bitmap);
    }

    /// <summary>
    /// Bitmap から HICON を生成し、所有権を持つ Icon を Clone で作成してからネイティブハンドルを破棄する。
    /// Icon.FromHandle は HICON の所有権を持たないため、Clone で独立コピーを取る。
    /// </summary>
    private static Icon CloneAndDestroyHandle(Bitmap bitmap)
    {
        var hIcon = bitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    #endregion

    public void Dispose()
    {
        _recording.PropertyChanged -= OnRecordingPropertyChanged;
        _meetingDetection.MeetingDetected -= OnMeetingDetected;
        _meetingDetection.MeetingEnded -= OnMeetingEnded;

        if (_notifyIcon != null)
        {
            _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _idleIcon?.Dispose();
        _recordingIcon?.Dispose();
        _pausedIcon?.Dispose();
    }
}
