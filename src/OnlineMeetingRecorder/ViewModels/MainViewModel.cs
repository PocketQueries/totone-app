using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;
using OnlineMeetingRecorder.Views;

namespace OnlineMeetingRecorder.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;

    public DeviceSelectionViewModel DeviceSelection { get; }
    public AudioLevelViewModel AudioLevel { get; }
    public RecordingViewModel Recording { get; }
    public TranscriptionViewModel Transcription { get; }
    public SessionListViewModel SessionList { get; }
    public PlaybackViewModel Playback { get; }

    /// <summary>文字起こしエンジンがオンライン（API通信あり）かどうか</summary>
    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _networkStatusText = "";

    /// <summary>文字起こしエンジン名</summary>
    [ObservableProperty]
    private string _sttModelName = "";

    /// <summary>議事録エンジン名</summary>
    [ObservableProperty]
    private string _minutesModelName = "";

    public MainViewModel(
        DeviceSelectionViewModel deviceSelection,
        AudioLevelViewModel audioLevel,
        RecordingViewModel recording,
        TranscriptionViewModel transcription,
        SessionListViewModel sessionList,
        PlaybackViewModel playback,
        IServiceProvider serviceProvider,
        ISettingsService settingsService)
    {
        DeviceSelection = deviceSelection;
        AudioLevel = audioLevel;
        Recording = recording;
        Transcription = transcription;
        SessionList = sessionList;
        Playback = playback;
        _serviceProvider = serviceProvider;
        _settingsService = settingsService;

        // 録音完了時に文字起こしVMへ通知 + セッション一覧を更新 + 自動文字起こし・議事録生成
        Recording.RecordingCompleted += async (_, session) =>
        {
            Transcription.OnRecordingCompleted(session);
            _ = SessionList.LoadSessionsAsync();
            // 自動で文字起こし→議事録生成を実行
            await Transcription.AutoProcessAfterRecordingAsync();
            // 処理後にセッション一覧を再更新（ステータス反映）
            _ = SessionList.LoadSessionsAsync();
        };

        // セッション一覧からの選択を文字起こし・再生VMに接続
        SessionList.SessionSelected += async (_, session) =>
        {
            await Transcription.LoadSessionAsync(session);
            if (session != null)
                Playback.LoadSession(session, Transcription.Segments.ToList());
            else
                Playback.UnloadSession();
        };

        // 文字起こしエンジンの設定状態を初期チェック
        RefreshTranscriptionStatus();

        // セッション一覧を非同期でロード
        _ = SessionList.LoadSessionsAsync();
    }

    [RelayCommand]
    private void OpenHelp()
    {
        Views.HelpDialog.ShowHelp(System.Windows.Application.Current?.MainWindow);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsVm = _serviceProvider.GetRequiredService<SettingsViewModel>();
        var settingsWindow = new SettingsWindow(settingsVm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        settingsWindow.ShowDialog();

        // 設定変更後に文字起こしエンジンの状態を再チェック
        RefreshTranscriptionStatus();
    }

    private void RefreshTranscriptionStatus()
    {
        Transcription.RefreshConfigurationStatus();
        Recording.UpdateTranscriptionConfigured(Transcription.IsTranscriptionConfigured);
        UpdateConnectionMode();
        UpdateModelNames();
    }

    /// <summary>文字起こしエンジンの設定に基づいてオンライン/オフラインモードを更新</summary>
    private void UpdateConnectionMode()
    {
        IsOnline = _settingsService.Settings.SttEngine == SttEngine.Cloud;
        NetworkStatusText = IsOnline ? "オンラインモード" : "オフラインモード";
    }

    /// <summary>使用中のモデル名を更新</summary>
    private void UpdateModelNames()
    {
        var s = _settingsService.Settings;

        SttModelName = s.SttEngine switch
        {
            SttEngine.Cloud => "OpenAI Whisper API",
            SttEngine.Local when !string.IsNullOrWhiteSpace(s.WhisperModelPath)
                => Path.GetFileNameWithoutExtension(s.WhisperModelPath),
            _ => "未設定"
        };

        MinutesModelName = s.MinutesEngine switch
        {
            MinutesEngine.CloudApi => $"OpenAI {s.MinutesApiModel}",
            MinutesEngine.Llm when !string.IsNullOrWhiteSpace(s.LlmModelPath)
                => Path.GetFileNameWithoutExtension(s.LlmModelPath),
            MinutesEngine.Template => "テンプレート",
            _ => "未設定"
        };
    }

    public void Dispose()
    {
        AudioLevel.Dispose();
        Recording.Dispose();
        Playback.Dispose();
    }
}
