using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.Services.Transcription;

/// <summary>
/// OpenAI Whisper API を使用したクラウド文字起こしサービス
/// </summary>
public class CloudWhisperService : ITranscriptionService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _httpClient;

    public string Name => "OpenAI Whisper API";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.Settings.OpenAiApiKey);

    public CloudWhisperService(ISettingsService settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async Task<List<TranscriptSegment>> TranscribeAsync(
        string wavFilePath,
        string speaker,
        string language,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("OpenAI APIキーが設定されていません。設定画面でAPIキーを入力してください。");

        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException($"音声ファイルが見つかりません: {wavFilePath}");

        progress?.Report(5);

        // 16kHz mono 16-bit PCM に変換（ファイルサイズ削減）
        var convertedPath = await AudioConverter.ConvertTo16kHzMonoPcmAsync(wavFilePath, cancellationToken);
        progress?.Report(10);

        var chunkFiles = new List<string>();
        try
        {
            // ファイルサイズが API 制限（25MB）を超える場合はチャンク分割
            var chunks = await AudioConverter.SplitWavIfNeededAsync(convertedPath, ct: cancellationToken);
            progress?.Report(20);

            var allSegments = new List<TranscriptSegment>();

            for (int i = 0; i < chunks.Count; i++)
            {
                var (chunkPath, offset) = chunks[i];
                if (chunkPath != convertedPath)
                    chunkFiles.Add(chunkPath);

                // Progress: 20-90 をチャンク数で等分
                var chunkBase = 20 + (int)(70.0 * i / chunks.Count);
                var chunkRange = (int)(70.0 / chunks.Count);
                var chunkProgress = new Progress<int>(p =>
                    progress?.Report(chunkBase + p * chunkRange / 100));

                var segments = await CallWhisperApiAsync(chunkPath, language, chunkProgress, cancellationToken);

                // タイムスタンプをオフセット分調整し、話者を設定
                foreach (var seg in segments)
                {
                    seg.Start += offset;
                    seg.End += offset;
                    seg.Speaker = speaker;
                }

                allSegments.AddRange(segments);
            }

            progress?.Report(100);
            return allSegments;
        }
        finally
        {
            foreach (var f in chunkFiles)
                TryDeleteFile(f);
            TryDeleteFile(convertedPath);
        }
    }

    private async Task<List<TranscriptSegment>> CallWhisperApiAsync(
        string audioFilePath,
        string language,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Settings.OpenAiApiKey);

        using var content = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(audioFilePath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", Path.GetFileName(audioFilePath));
        content.Add(new StringContent("whisper-1"), "model");
        content.Add(new StringContent(language), "language");
        content.Add(new StringContent("verbose_json"), "response_format");
        content.Add(new StringContent("segment"), "timestamp_granularities[]");

        request.Content = content;

        progress?.Report(40);

        var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        progress?.Report(80);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(ExtractApiErrorMessage(responseBody, response.StatusCode));
        }

        return ParseResponse(responseBody);
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

    private static List<TranscriptSegment> ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var segments = new List<TranscriptSegment>();

        if (root.TryGetProperty("segments", out var segmentsElement))
        {
            foreach (var seg in segmentsElement.EnumerateArray())
            {
                var start = seg.GetProperty("start").GetDouble();
                var end = seg.GetProperty("end").GetDouble();
                var text = seg.GetProperty("text").GetString()?.Trim() ?? string.Empty;

                if (!string.IsNullOrEmpty(text))
                {
                    segments.Add(new TranscriptSegment
                    {
                        Start = TimeSpan.FromSeconds(start),
                        End = TimeSpan.FromSeconds(end),
                        Text = text,
                        Speaker = string.Empty
                    });
                }
            }
        }

        return segments;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
