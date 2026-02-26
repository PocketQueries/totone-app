using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Audio;
using OnlineMeetingRecorder.Services.Session;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.ViewModels;

public partial class RecordingViewModel : ObservableObject, IDisposable
{
    private readonly IAudioRecorder _recorder;
    private readonly DeviceSelectionViewModel _deviceSelection;
    private readonly ISessionService _sessionService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<RecordingViewModel> _logger;
    private DispatcherTimer? _elapsedTimer;

    [ObservableProperty]
    private RecorderState _recorderState = RecorderState.Idle;

    [ObservableProperty]
    private string _elapsedTimeText = "00:00:00";

    [ObservableProperty]
    private string _statusText = "待機中";

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    private bool _canStart;

    [ObservableProperty]
    private bool _canStop;

    private bool _isTranscriptionConfigured;

    [ObservableProperty]
    private bool _canPause;

    /// <summary>現在の録音セッション</summary>
    [ObservableProperty]
    private RecordingSession? _currentSession;

    /// <summary>現在のセッションフォルダパス</summary>
    [ObservableProperty]
    private string? _currentSessionFolder;

    /// <summary>録音完了時に発火するイベント</summary>
    public event EventHandler<RecordingSession>? RecordingCompleted;

    public RecordingViewModel(IAudioRecorder recorder, DeviceSelectionViewModel deviceSelection, ISessionService sessionService, ISettingsService settingsService, ILogger<RecordingViewModel> logger)
    {
        _recorder = recorder;
        _deviceSelection = deviceSelection;
        _sessionService = sessionService;
        _settingsService = settingsService;
        _logger = logger;
        _recorder.StateChanged += OnRecorderStateChanged;
        _recorder.ErrorOccurred += OnErrorOccurred;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRecordingWithDefaultsAsync()
    {
        _deviceSelection.SelectDefaultDevices();
        await StartRecordingAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRecordingAsync()
    {
        if (_deviceSelection.SelectedInputDevice == null || _deviceSelection.SelectedOutputDevice == null)
        {
            StatusText = "デバイスを選択してください";
            return;
        }

        try
        {
            // セッションフォルダの作成（検証済みの保存先を使用）
            var sessionsRoot = _sessionService.GetSessionsRoot();
            var sessionFolder = Path.Combine(sessionsRoot,
                $"{DateTime.Now:yyyy-MM-dd_HHmm}_{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(sessionFolder);

            // セッションデータ保護: 現在のユーザーのみにアクセスを制限
            SetRestrictiveAcl(sessionFolder);

            CurrentSessionFolder = sessionFolder;

            // セッションメタデータを作成・保存
            CurrentSession = await _sessionService.CreateSessionAsync(
                sessionFolder,
                _deviceSelection.SelectedInputDevice.FriendlyName,
                _deviceSelection.SelectedOutputDevice.FriendlyName);

            _recorder.StartRecording(
                _deviceSelection.SelectedInputDevice.Id,
                _deviceSelection.SelectedOutputDevice.Id,
                sessionFolder);

            StartElapsedTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "録音開始に失敗");
            StatusText = $"録音開始エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        try
        {
            _recorder.StopRecording();
            StopElapsedTimer();

            // セッションを完了状態に更新
            if (CurrentSession != null)
            {
                await _sessionService.CompleteSessionAsync(CurrentSession);
                RecordingCompleted?.Invoke(this, CurrentSession);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "録音停止に失敗");
            StatusText = $"録音停止エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
        {
            _recorder.ResumeRecording();
        }
        else
        {
            _recorder.PauseRecording();
        }
    }

    /// <summary>
    /// 文字起こしエンジンの設定状態を更新し、CanStart を再計算する
    /// </summary>
    public void UpdateTranscriptionConfigured(bool configured)
    {
        _isTranscriptionConfigured = configured;
        CanStart = RecorderState == RecorderState.Idle && _isTranscriptionConfigured;
    }

    private void OnRecorderStateChanged(object? sender, RecorderState state)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            RecorderState = state;
            IsRecording = state == RecorderState.Recording || state == RecorderState.Paused;
            IsPaused = state == RecorderState.Paused;
            CanStart = state == RecorderState.Idle && _isTranscriptionConfigured;
            CanStop = state == RecorderState.Recording || state == RecorderState.Paused;
            CanPause = state == RecorderState.Recording || state == RecorderState.Paused;

            StatusText = state switch
            {
                RecorderState.Idle => "待機中",
                RecorderState.Recording => "録音中",
                RecorderState.Paused => "一時停止中",
                RecorderState.Stopping => "停止処理中...",
                _ => "不明"
            };
        });
    }

    private void OnErrorOccurred(object? sender, Exception ex)
    {
        _logger.LogError(ex, "AudioRecorder からエラー通知");
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            StatusText = $"エラー: {ex.Message}";
        });
    }

    private void StartElapsedTimer()
    {
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _elapsedTimer.Tick += (_, _) =>
        {
            ElapsedTimeText = _recorder.Elapsed.ToString(@"hh\:mm\:ss");
        };
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
    }

    /// <summary>
    /// ディレクトリに現在のユーザーとSYSTEMのみのアクセスを設定する。
    /// カスタム保存先の場合に会議録音データを保護する。
    /// </summary>
    private static void SetRestrictiveAcl(string directoryPath)
    {
        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            var security = dirInfo.GetAccessControl();

            // 親からの継承を無効化し、明示的ルールのみ適用
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // 既存ルールをすべて削除
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                security.PurgeAccessRules(rule.IdentityReference);

            // 現在のユーザーにフルアクセス
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            // SYSTEM にフルアクセス（バックアップ・ウイルス対策ソフト等のため）
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            dirInfo.SetAccessControl(security);
        }
        catch
        {
            // ACL設定失敗は無視（録音機能は継続）
        }
    }

    public void Dispose()
    {
        StopElapsedTimer();
        _recorder.StateChanged -= OnRecorderStateChanged;
        _recorder.ErrorOccurred -= OnErrorOccurred;
    }
}
