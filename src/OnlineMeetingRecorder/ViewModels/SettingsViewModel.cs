using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Minutes;
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

    // --- プロンプトプリセット管理 ---

    /// <summary>プリセット一覧</summary>
    public ObservableCollection<PromptPreset> PromptPresets { get; } = new();

    /// <summary>編集中のプリセット</summary>
    [ObservableProperty]
    private PromptPreset? _selectedPreset;

    /// <summary>プリセット名（編集用）</summary>
    [ObservableProperty]
    private string _presetName = string.Empty;

    /// <summary>プリセットのシステムプロンプト（編集用）</summary>
    [ObservableProperty]
    private string _presetSystemPrompt = string.Empty;

    /// <summary>選択中プリセットが組み込みか（削除ボタンの表示制御用）</summary>
    [ObservableProperty]
    private bool _isSelectedPresetBuiltIn;

    /// <summary>プリセット編集エリアを表示するか</summary>
    [ObservableProperty]
    private bool _isPresetEditorVisible;

    /// <summary>プリセット名が編集可能か</summary>
    [ObservableProperty]
    private bool _canEditPresetName;

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

        // プリセット一覧をロード
        PromptPresets.Clear();
        foreach (var preset in s.PromptPresets)
            PromptPresets.Add(preset);

        SelectedPreset = PromptPresets.FirstOrDefault(p => p.Id == s.SelectedPresetId)
            ?? PromptPresets.FirstOrDefault();
    }

    partial void OnSelectedPresetChanged(PromptPreset? value)
    {
        if (value != null)
        {
            _isUpdatingFromSelection = true;
            PresetName = value.Name;
            PresetSystemPrompt = value.SystemPrompt;
            _isUpdatingFromSelection = false;
            IsSelectedPresetBuiltIn = value.IsBuiltIn;
            CanEditPresetName = !value.IsBuiltIn;
            IsPresetEditorVisible = true;
        }
        else
        {
            PresetName = string.Empty;
            PresetSystemPrompt = string.Empty;
            IsSelectedPresetBuiltIn = false;
            CanEditPresetName = false;
            IsPresetEditorVisible = false;
        }
    }

    // 選択変更によるプロパティ更新時にリアルタイム反映を抑制するフラグ
    private bool _isUpdatingFromSelection;

    partial void OnPresetNameChanged(string value)
    {
        if (_isUpdatingFromSelection || SelectedPreset == null) return;
        // Name setter の INotifyPropertyChanged で ComboBox 表示が自動更新される
        SelectedPreset.Name = value;
    }

    partial void OnPresetSystemPromptChanged(string value)
    {
        if (_isUpdatingFromSelection || SelectedPreset == null) return;
        SelectedPreset.SystemPrompt = value;
    }

    [RelayCommand]
    private void AddPreset()
    {
        var newPreset = new PromptPreset
        {
            Name = "新規プリセット",
            SystemPrompt = CloudMinutesGenerator.GetDefaultSystemPromptBase()
        };
        PromptPresets.Add(newPreset);
        SelectedPreset = newPreset;
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset == null || SelectedPreset.IsBuiltIn) return;

        var idx = PromptPresets.IndexOf(SelectedPreset);
        PromptPresets.Remove(SelectedPreset);
        SelectedPreset = PromptPresets.ElementAtOrDefault(Math.Min(idx, PromptPresets.Count - 1));
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

        // プリセットを書き戻し
        _settingsService.Settings.PromptPresets = PromptPresets.ToList();
        _settingsService.Settings.SelectedPresetId = SelectedPreset?.Id ?? string.Empty;

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
