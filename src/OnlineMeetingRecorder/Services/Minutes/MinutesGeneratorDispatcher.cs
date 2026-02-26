using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.Services.Minutes;

/// <summary>
/// 設定に基づいて適切な議事録ジェネレータに委譲するディスパッチャ。
/// IMinutesGenerator として DI に登録し、TranscriptionViewModel への変更を不要にする。
/// </summary>
public class MinutesGeneratorDispatcher : IMinutesGenerator, IDisposable
{
    private readonly TemplateMinutesGenerator _templateGenerator;
    private readonly CloudMinutesGenerator _cloudGenerator;
    private readonly ISettingsService _settingsService;

#if ENABLE_LLM
    private readonly LlmMinutesGenerator _llmGenerator;

    public MinutesGeneratorDispatcher(
        TemplateMinutesGenerator templateGenerator,
        CloudMinutesGenerator cloudGenerator,
        LlmMinutesGenerator llmGenerator,
        ISettingsService settingsService)
    {
        _templateGenerator = templateGenerator;
        _cloudGenerator = cloudGenerator;
        _llmGenerator = llmGenerator;
        _settingsService = settingsService;
    }
#else
    public MinutesGeneratorDispatcher(
        TemplateMinutesGenerator templateGenerator,
        CloudMinutesGenerator cloudGenerator,
        ISettingsService settingsService)
    {
        _templateGenerator = templateGenerator;
        _cloudGenerator = cloudGenerator;
        _settingsService = settingsService;
    }
#endif

    public Task<MinutesResult> GenerateAsync(RecordingSession session, List<TranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        return GenerateWithEngineAsync(session, segments, _settingsService.Settings.MinutesEngine, cancellationToken);
    }

    public Task<MinutesResult> GenerateWithContextAsync(RecordingSession session, List<TranscriptSegment> segments, TotonoeContext context, CancellationToken cancellationToken = default)
    {
        return GenerateWithContextAndEngineAsync(session, segments, context, _settingsService.Settings.MinutesEngine, cancellationToken);
    }

    /// <summary>
    /// 指定されたエンジンで議事録を生成する（インラインエンジン選択用）
    /// </summary>
    public Task<MinutesResult> GenerateWithEngineAsync(
        RecordingSession session,
        List<TranscriptSegment> segments,
        MinutesEngine engine,
        CancellationToken cancellationToken = default)
    {
        if (engine == MinutesEngine.CloudApi)
            return _cloudGenerator.GenerateAsync(session, segments, cancellationToken);

#if ENABLE_LLM
        if (engine == MinutesEngine.Llm)
            return _llmGenerator.GenerateAsync(session, segments, cancellationToken);
#endif
        return _templateGenerator.GenerateAsync(session, segments, cancellationToken);
    }

    /// <summary>
    /// 指定されたエンジンでコンテキスト付き議事録を再生成する（ととのえ機能用）
    /// </summary>
    public Task<MinutesResult> GenerateWithContextAndEngineAsync(
        RecordingSession session,
        List<TranscriptSegment> segments,
        TotonoeContext context,
        MinutesEngine engine,
        CancellationToken cancellationToken = default)
    {
        if (engine == MinutesEngine.CloudApi)
            return _cloudGenerator.GenerateWithContextAsync(session, segments, context, cancellationToken);

#if ENABLE_LLM
        if (engine == MinutesEngine.Llm)
            return ((IMinutesGenerator)_llmGenerator).GenerateWithContextAsync(session, segments, context, cancellationToken);
#endif
        // テンプレートはコンテキスト非対応なので通常生成にフォールバック
        return _templateGenerator.GenerateAsync(session, segments, cancellationToken);
    }

    /// <summary>カスタムプロンプトで議事録を生成する（CloudApi限定）</summary>
    public Task<MinutesResult> GenerateWithPromptsAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        return _cloudGenerator.GenerateWithPromptsAsync(systemPrompt, userMessage, cancellationToken);
    }

    public void Dispose()
    {
        _cloudGenerator.Dispose();
#if ENABLE_LLM
        _llmGenerator.Dispose();
#endif
    }
}
