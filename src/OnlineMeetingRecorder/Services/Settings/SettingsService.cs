using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Minutes;

namespace OnlineMeetingRecorder.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OnlineMeetingRecorder", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            EnsureDefaultPreset();
            return;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(SettingsPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "設定ファイルの読み込みに失敗。デフォルト値を使用します");
            Settings = new AppSettings();
            return;
        }

        // DPAPI暗号化キーを復号
        if (!string.IsNullOrEmpty(Settings.OpenAiApiKeyEncrypted))
        {
            Settings.OpenAiApiKey = DecryptString(Settings.OpenAiApiKeyEncrypted);
        }

        // デフォルトプリセットを保証
        EnsureDefaultPreset();

        // マイグレーション: 旧形式の平文キー（openAiApiKey）が残っている場合
        if (string.IsNullOrEmpty(Settings.OpenAiApiKey))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("openAiApiKey", out var oldKeyElement))
                {
                    var plainKey = oldKeyElement.GetString();
                    if (!string.IsNullOrWhiteSpace(plainKey))
                    {
                        Settings.OpenAiApiKey = plainKey;
                        // 暗号化形式で再保存（平文フィールドはJsonIgnoreにより書き出されない）
                        await SaveAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "APIキーマイグレーションに失敗");
            }
        }
    }

    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);

        // APIキーをDPAPI暗号化してからシリアライズ
        Settings.OpenAiApiKeyEncrypted = !string.IsNullOrEmpty(Settings.OpenAiApiKey)
            ? EncryptString(Settings.OpenAiApiKey)
            : string.Empty;

        var json = JsonSerializer.Serialize(Settings, JsonOptions);

        // アトミック書き込み: 一時ファイルに書いてからリネーム
        var tempPath = SettingsPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    /// <summary>デフォルトプリセットが存在しない場合に追加する</summary>
    private void EnsureDefaultPreset()
    {
        const string defaultPresetId = "default";

        var defaultPreset = Settings.PromptPresets.FirstOrDefault(p => p.Id == defaultPresetId);
        if (defaultPreset == null)
        {
            Settings.PromptPresets.Insert(0, new PromptPreset
            {
                Id = defaultPresetId,
                Name = "デフォルト",
                SystemPrompt = CloudMinutesGenerator.GetDefaultSystemPromptBase(),
                IsBuiltIn = true
            });
        }

        // 選択中プリセットが存在しない場合はデフォルトにフォールバック
        if (string.IsNullOrEmpty(Settings.SelectedPresetId) ||
            !Settings.PromptPresets.Any(p => p.Id == Settings.SelectedPresetId))
        {
            Settings.SelectedPresetId = defaultPresetId;
        }
    }

    /// <summary>
    /// DPAPI (CurrentUser スコープ) で文字列を暗号化し、Base64 で返す
    /// </summary>
    private static string EncryptString(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// DPAPI (CurrentUser スコープ) で Base64 文字列を復号する
    /// </summary>
    private static string DecryptString(string encryptedBase64)
    {
        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedBase64);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // 別ユーザーで暗号化されたデータや破損データの場合
            return string.Empty;
        }
        catch (FormatException)
        {
            // Base64 として不正な場合
            return string.Empty;
        }
    }
}
