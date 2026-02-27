using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.Services.Minutes;

/// <summary>
/// OpenAI Chat Completions API を使用したクラウド議事録生成サービス
/// </summary>
public class CloudMinutesGenerator : IMinutesGenerator, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

    public CloudMinutesGenerator(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    /// <summary>APIキーが設定済みで利用可能か</summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settingsService.Settings.OpenAiApiKey);

    public Task<MinutesResult> GenerateAsync(RecordingSession session, List<TranscriptSegment> segments, CancellationToken cancellationToken = default)
        => GenerateWithContextAsync(session, segments, null, cancellationToken);

    public Task<MinutesResult> GenerateWithContextAsync(RecordingSession session, List<TranscriptSegment> segments, TotonoeContext? context, CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(context);
        var userMessage = BuildUserMessage(session, segments);
        return GenerateWithPromptsAsync(systemPrompt, userMessage, cancellationToken);
    }

    /// <summary>カスタムプロンプトで議事録を生成する</summary>
    public async Task<MinutesResult> GenerateWithPromptsAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("OpenAI APIキーが設定されていません。設定画面でAPIキーを入力してください。");

        var model = _settingsService.Settings.MinutesApiModel;
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4o-mini";

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt.Trim() },
                new { role = "user", content = userMessage }
            }
        };

        // gpt-5 系など temperature 非対応モデルでは省略する
        if (!model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
        {
            requestBody["temperature"] = 0.3;
        }

        var json = JsonSerializer.Serialize(requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.Settings.OpenAiApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(ExtractApiErrorMessage(responseBody, response.StatusCode));
        }

        using var doc = JsonDocument.Parse(responseBody);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        // トークン使用量を抽出
        int? promptTokens = null, completionTokens = null, totalTokens = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt))
                promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct))
                completionTokens = ct.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var tt))
                totalTokens = tt.GetInt32();
        }

        var (minutesText, suggestions) = ParseMinutesAndSuggestions(content?.Trim() ?? string.Empty);

        return new MinutesResult
        {
            Text = minutesText,
            TokenCount = totalTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            SuggestedAdditionalInfo = suggestions
        };
    }

    /// <summary>
    /// LLMレスポンスを議事録本文と追加情報の提案に分割する。
    /// 「## 追加情報の提案」マーカーで区切る。
    /// </summary>
    public static (string minutesText, string? suggestions) ParseMinutesAndSuggestions(string fullText)
    {
        // "## 追加情報の提案" で分割
        var marker = "## 追加情報の提案";
        var idx = fullText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return (fullText, null);

        var minutesText = fullText[..idx].TrimEnd();
        var suggestionsRaw = fullText[(idx + marker.Length)..].Trim();

        if (string.IsNullOrWhiteSpace(suggestionsRaw))
            return (minutesText, null);

        return (minutesText, suggestionsRaw);
    }

    /// <summary>ととのえ用ユーザーメッセージを構築する（生成済み議事録ベース）</summary>
    public static string BuildTotonoeUserMessage(string minutesText)
    {
        return $"以下の議事録を、追加情報を踏まえてより正確にブラッシュアップしてください。\n\n{minutesText.Trim()}";
    }

    /// <summary>ユーザーメッセージを構築する</summary>
    public static string BuildUserMessage(RecordingSession session, List<TranscriptSegment> segments)
    {
        var transcript = string.Join("\n",
            segments.OrderBy(s => s.Start).Select(s => s.ToString()));

        var header = $"会議日時: {session.StartTime:yyyy/MM/dd HH:mm} 〜 {session.EndTime:yyyy/MM/dd HH:mm}\n" +
                     $"所要時間: {session.Duration:hh\\:mm\\:ss}\n\n";

        return $"以下の会議文字起こしから議事録を作成してください。\n\n{header}{transcript.Trim()}";
    }

    /// <summary>デフォルトのベースシステムプロンプトを取得する</summary>
    public static string GetDefaultSystemPromptBase()
    {
        return """
            あなたは会議の議事録を作成するアシスタントです。
            以下の会議の文字起こしを読み、議事録をMarkdown形式で作成してください。

            議事録には以下のセクションを含めてください：
            1. **要約** - 会議全体の概要（2-3文）
            2. **決定事項** - 会議で決まったことのリスト
            3. **アクションアイテム** - 担当者と期限を含むTODOリスト
            4. **次回予定** - 次回の会議予定

            簡潔かつ正確に記述してください。
            """.Trim();
    }

    /// <summary>追加情報の提案を出力させるプロンプト部分</summary>
    public static string GetSuggestionPromptSuffix()
    {
        return """
            また、議事録の最後に「## 追加情報の提案」というセクションを追加し、
            この議事録をより正確に完成させるために確認・補足が必要な情報を箇条書きで記載してください。
            具体的には以下の観点で提案してください：
            - 文字起こしで聞き取りにくい・不明確な単語や固有名詞
            - 不自然な文章や意味が通りにくい箇所
            - 参加者の正確な氏名・所属
            - 会議の目的や背景で補足があると良い情報
            - 専門用語やドメイン固有の知識で確認が必要なもの

            提案がない場合は「## 追加情報の提案」セクション自体を省略してください。
            """.Trim();
    }

    /// <summary>システムプロンプトを構築する</summary>
    public static string BuildSystemPrompt(TotonoeContext? context, string? basePromptOverride = null)
    {
        var basePrompt = basePromptOverride ?? GetDefaultSystemPromptBase();

        // 初回生成時（コンテキストなし）は追加情報の提案も出力させる
        if (context == null || !context.HasAnyContent())
        {
            return basePrompt.Trim() + "\n\n" + GetSuggestionPromptSuffix();
        }

        var sb = new StringBuilder(basePrompt.Trim());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("以下の追加情報を踏まえて、より正確で質の高い議事録を作成してください。");

        if (!string.IsNullOrWhiteSpace(context.CustomerCompany))
            sb.AppendLine($"- お客様の会社名: {context.CustomerCompany}");

        if (!string.IsNullOrWhiteSpace(context.CustomerParticipants))
            sb.AppendLine($"- お客様の参加者: {context.CustomerParticipants}");

        if (!string.IsNullOrWhiteSpace(context.OurParticipants))
            sb.AppendLine($"- 自社の参加者: {context.OurParticipants}");

        if (!string.IsNullOrWhiteSpace(context.MeetingPurpose))
            sb.AppendLine($"- 会議の目的・内容: {context.MeetingPurpose}");

        if (!string.IsNullOrWhiteSpace(context.DomainKnowledge))
        {
            sb.AppendLine();
            sb.AppendLine("専門用語・ドメイン知識:");
            sb.AppendLine(context.DomainKnowledge);
        }

        sb.AppendLine();
        sb.AppendLine("参加者名が分かっている場合は、発言者を適切に識別して記載してください。");

        return sb.ToString();
    }

    /// <summary>
    /// APIエラーレスポンスから安全なエラーメッセージを抽出する。
    /// レスポンスボディ全体を露出させず、error.message のみを返す。
    /// </summary>
    private static string ExtractApiErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                var msg = message.GetString();
                if (!string.IsNullOrEmpty(msg))
                    return $"OpenAI API エラー ({statusCode}): {msg}";
            }
        }
        catch
        {
            // JSONパース失敗時はステータスコードのみ
        }
        return $"OpenAI API エラー ({statusCode})";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
