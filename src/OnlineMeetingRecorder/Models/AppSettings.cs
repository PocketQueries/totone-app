using System.Text.Json.Serialization;

namespace OnlineMeetingRecorder.Models;

/// <summary>
/// アプリケーション設定
/// </summary>
public class AppSettings
{
    /// <summary>STTエンジン選択</summary>
    public SttEngine SttEngine { get; set; } = SttEngine.Cloud;

    /// <summary>OpenAI APIキー（ランタイム専用。JSONには書き出さない）</summary>
    [JsonIgnore]
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>DPAPI暗号化されたAPIキー（JSON永続化用）</summary>
    public string OpenAiApiKeyEncrypted { get; set; } = string.Empty;

    /// <summary>Whisperモデルファイルパス</summary>
    public string WhisperModelPath { get; set; } = string.Empty;

    /// <summary>文字起こし言語</summary>
    public string Language { get; set; } = "ja";

    /// <summary>議事録エンジン選択</summary>
    public MinutesEngine MinutesEngine { get; set; } = MinutesEngine.Template;

    /// <summary>LLMモデルファイルパス (GGUF)</summary>
    public string LlmModelPath { get; set; } = string.Empty;

    /// <summary>LLM推論時のコンテキストサイズ（トークン数）</summary>
    public uint LlmContextSize { get; set; } = 8192;

    /// <summary>GPUにオフロードするレイヤー数（999=全レイヤー、0=CPUのみ）</summary>
    public int LlmGpuLayerCount { get; set; } = 999;

    /// <summary>音声エクスポートフォーマット</summary>
    public AudioExportFormat AudioExportFormat { get; set; } = AudioExportFormat.Wav;

    /// <summary>議事録生成に使用するOpenAIモデル名</summary>
    public string MinutesApiModel { get; set; } = "gpt-4o-mini";

    /// <summary>セッション保存先フォルダ（空の場合は %LOCALAPPDATA%/OnlineMeetingRecorder/Sessions）</summary>
    public string SessionStoragePath { get; set; } = string.Empty;

    /// <summary>会議アプリの自動検知を有効にする</summary>
    public bool MeetingDetectionEnabled { get; set; } = true;
}

/// <summary>音声エクスポートフォーマット</summary>
public enum AudioExportFormat
{
    /// <summary>WAV（無変換）</summary>
    Wav,

    /// <summary>MP3 圧縮</summary>
    Mp3,

    /// <summary>FLAC 可逆圧縮</summary>
    Flac
}

public enum SttEngine
{
    Local,
    Cloud
}

/// <summary>議事録生成エンジン</summary>
public enum MinutesEngine
{
    /// <summary>テンプレートベースの整形出力</summary>
    Template,

    /// <summary>ローカルLLM (LLamaSharp + Qwen3) による AI生成</summary>
    Llm,

    /// <summary>OpenAI API によるクラウドAI生成</summary>
    CloudApi
}
