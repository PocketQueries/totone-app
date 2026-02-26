using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private SttEngine _sttEngine;

    [ObservableProperty]
    private string _openAiApiKey = string.Empty;

    [ObservableProperty]
    private string _whisperModelPath = string.Empty;

    [ObservableProperty]
    private string _language = "ja";

    [ObservableProperty]
    private MinutesEngine _minutesEngine;

    [ObservableProperty]
    private string _llmModelPath = string.Empty;

    [ObservableProperty]
    private uint _llmContextSize = 8192;

    [ObservableProperty]
    private int _llmGpuLayerCount = 999;

    [ObservableProperty]
    private string _minutesApiModel = "gpt-4o-mini";

    [ObservableProperty]
    private AudioExportFormat _audioExportFormat;

    [ObservableProperty]
    private string _sessionStoragePath = string.Empty;

    [ObservableProperty]
    private bool _meetingDetectionEnabled;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>LLM組み込みビルドかどうか（UIの表示制御に使用）</summary>
    public bool IsLlmAvailable =>
#if ENABLE_LLM
        true;
#else
        false;
#endif

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Settings;
        SttEngine = s.SttEngine;
        OpenAiApiKey = s.OpenAiApiKey;
        WhisperModelPath = s.WhisperModelPath;
        Language = s.Language;
        MinutesEngine = s.MinutesEngine;
        LlmModelPath = s.LlmModelPath;
        LlmContextSize = s.LlmContextSize;
        LlmGpuLayerCount = s.LlmGpuLayerCount;
        MinutesApiModel = s.MinutesApiModel;
        AudioExportFormat = s.AudioExportFormat;
        SessionStoragePath = s.SessionStoragePath;
        MeetingDetectionEnabled = s.MeetingDetectionEnabled;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settingsService.Settings.SttEngine = SttEngine;
        _settingsService.Settings.OpenAiApiKey = OpenAiApiKey;
        _settingsService.Settings.WhisperModelPath = WhisperModelPath;
        _settingsService.Settings.Language = Language;
        _settingsService.Settings.MinutesEngine = MinutesEngine;
        _settingsService.Settings.LlmModelPath = LlmModelPath;
        _settingsService.Settings.LlmContextSize = LlmContextSize;
        _settingsService.Settings.LlmGpuLayerCount = LlmGpuLayerCount;
        _settingsService.Settings.MinutesApiModel = MinutesApiModel;
        _settingsService.Settings.AudioExportFormat = AudioExportFormat;
        _settingsService.Settings.SessionStoragePath = SessionStoragePath;
        _settingsService.Settings.MeetingDetectionEnabled = MeetingDetectionEnabled;

        try
        {
            await _settingsService.SaveAsync();
            StatusText = "設定を保存しました。";
        }
        catch (Exception ex)
        {
            StatusText = $"保存エラー: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseModelFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Whisperモデルファイルを選択",
            Filter = "GGMLモデル (*.bin)|*.bin|すべてのファイル (*.*)|*.*",
            InitialDirectory = GetDirectoryOrDefault(WhisperModelPath)
        };

        if (dialog.ShowDialog() == true)
        {
            WhisperModelPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseLlmModelFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "LLMモデルファイルを選択 (GGUF形式)",
            Filter = "GGUFモデル (*.gguf)|*.gguf|すべてのファイル (*.*)|*.*",
            InitialDirectory = GetDirectoryOrDefault(LlmModelPath)
        };

        if (dialog.ShowDialog() == true)
        {
            LlmModelPath = dialog.FileName;
        }
    }

    /// <summary>
    /// ファイルパスが設定済みの場合はその親フォルダを返し、未設定の場合はユーザーフォルダを返す。
    /// </summary>
    private static string GetDirectoryOrDefault(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null && Directory.Exists(dir))
                return dir;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [RelayCommand]
    private void BrowseSessionFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "セッション保存先フォルダを選択"
        };

        if (!string.IsNullOrWhiteSpace(SessionStoragePath))
            dialog.InitialDirectory = SessionStoragePath;

        if (dialog.ShowDialog() == true)
        {
            SessionStoragePath = dialog.FolderName;
        }
    }
}
