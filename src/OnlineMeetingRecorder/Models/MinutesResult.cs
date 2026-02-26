namespace OnlineMeetingRecorder.Models;

/// <summary>
/// 議事録生成の結果。テキストとトークン消費情報を含む。
/// </summary>
public record MinutesResult
{
    /// <summary>生成された議事録テキスト</summary>
    public required string Text { get; init; }

    /// <summary>消費トークン数（AI利用時のみ。テンプレート生成時は null）</summary>
    public int? TokenCount { get; init; }

    /// <summary>プロンプトトークン数（Cloud API で取得可能な場合）</summary>
    public int? PromptTokens { get; init; }

    /// <summary>生成トークン数</summary>
    public int? CompletionTokens { get; init; }

    /// <summary>議事録を完成させるための追加情報の提案（AI生成時のみ）</summary>
    public string? SuggestedAdditionalInfo { get; init; }
}
