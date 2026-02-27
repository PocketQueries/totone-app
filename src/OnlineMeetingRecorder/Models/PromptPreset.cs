namespace OnlineMeetingRecorder.Models;

/// <summary>
/// プロンプトプリセット: AI議事録生成用のシステムプロンプトを保存する
/// </summary>
public class PromptPreset
{
    /// <summary>一意識別子</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>プリセット名（表示用）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>システムプロンプト本文</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>組み込みプリセットか（削除不可）</summary>
    public bool IsBuiltIn { get; set; }
}
