#if ENABLE_LLM
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.Services.Minutes;

/// <summary>
/// LLamaSharp + Qwen3 によるAI議事録生成。
/// モデルは初回呼び出し時に遅延ロードし、以降はキャッシュして再利用する。
/// </summary>
public class LlmMinutesGenerator : IMinutesGenerator, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LlmMinutesGenerator> _logger;
    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private LLamaWeights? _model;
    private ModelParams? _modelParams;
    private string? _loadedModelPath;
    private uint _loadedContextSize;
    private int _loadedGpuLayerCount;

    /// <summary>ロードされた LLamaSharp バックエンド情報（CUDA / CPU 等）</summary>
    public string? LoadedBackendInfo { get; private set; }

    public LlmMinutesGenerator(ISettingsService settingsService, ILogger<LlmMinutesGenerator> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<MinutesResult> GenerateAsync(RecordingSession session, List<TranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        var modelPath = _settingsService.Settings.LlmModelPath;

        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new InvalidOperationException(
                $"LLMモデルファイルが見つかりません: {modelPath}");

        // モデルのロード（パスが変わった場合は再ロード、SemaphoreSlim で排他制御）
        await _modelLock.WaitAsync(cancellationToken);
        try
        {
            var contextSize = _settingsService.Settings.LlmContextSize;
            if (contextSize < 512) contextSize = 8192;
            var gpuLayerCount = _settingsService.Settings.LlmGpuLayerCount;

            if (_model == null || _loadedModelPath != modelPath
                || _loadedContextSize != contextSize || _loadedGpuLayerCount != gpuLayerCount)
            {
                _model?.Dispose();

                // CUDA DLL の事前チェック（ロード失敗時の診断用）
                LogCudaDllAvailability();

                _modelParams = new ModelParams(modelPath)
                {
                    ContextSize = contextSize,
                    GpuLayerCount = gpuLayerCount,
                };
                _logger.LogInformation("[LLM] モデルロード開始: GpuLayerCount={GpuLayerCount}, ContextSize={ContextSize}",
                    gpuLayerCount, contextSize);

                _model = await Task.Run(() => LLamaWeights.LoadFromFile(_modelParams), cancellationToken);
                _loadedModelPath = modelPath;
                _loadedContextSize = contextSize;
                _loadedGpuLayerCount = gpuLayerCount;

                // ロード後のシステム情報を記録
                LogBackendInfo();
            }
        }
        finally
        {
            _modelLock.Release();
        }

        // トランスクリプトテキストの構築
        var transcript = string.Join("\n",
            segments.OrderBy(s => s.Start).Select(s => s.ToString()));

        var prompt = BuildPrompt(session, transcript);

        var executor = new StatelessExecutor(_model, _modelParams!);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 1024,
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f },
            AntiPrompts = ["<|im_end|>", "<|im_start|>"],
        };

        // 推論実行（<think>ブロックをフィルタリング + トークンカウント）
        var result = new StringBuilder();
        var tokenCount = 0;
        await Task.Run(async () =>
        {
            var insideThink = false;
            var buffer = "";

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                tokenCount++;
                buffer += token;

                // <think> 開始検出
                if (!insideThink && buffer.Contains("<think>"))
                {
                    var idx = buffer.IndexOf("<think>");
                    if (idx > 0)
                        result.Append(buffer[..idx]);
                    insideThink = true;
                    buffer = buffer[(idx + "<think>".Length)..];
                    continue;
                }

                // </think> 終了検出
                if (insideThink && buffer.Contains("</think>"))
                {
                    insideThink = false;
                    buffer = buffer[(buffer.IndexOf("</think>") + "</think>".Length)..];
                    if (buffer.Length > 0)
                    {
                        result.Append(buffer);
                        buffer = "";
                    }
                    continue;
                }

                // think内はバッファに溜めるだけ（捨てる）
                if (insideThink)
                {
                    if (buffer.Length > 100)
                        buffer = buffer[^20..];
                    continue;
                }

                // 通常出力
                if (!buffer.Contains('<'))
                {
                    result.Append(buffer);
                    buffer = "";
                }
                else if (buffer.Length > 20)
                {
                    result.Append(buffer);
                    buffer = "";
                }
            }

            // 残りのバッファを出力
            if (!insideThink && buffer.Length > 0)
                result.Append(buffer);
        }, cancellationToken);

        var (minutesText, suggestions) = CloudMinutesGenerator.ParseMinutesAndSuggestions(result.ToString().Trim());

        return new MinutesResult
        {
            Text = minutesText,
            TokenCount = tokenCount,
            CompletionTokens = tokenCount,
            SuggestedAdditionalInfo = suggestions
        };
    }

    private static string BuildPrompt(RecordingSession session, string transcript)
    {
        var systemPrompt = CloudMinutesGenerator.GetDefaultSystemPromptBase()
            + "\n\n" + CloudMinutesGenerator.GetSuggestionPromptSuffix();

        var header = $"会議日時: {session.StartTime:yyyy/MM/dd HH:mm} 〜 {session.EndTime:yyyy/MM/dd HH:mm}\n" +
                     $"所要時間: {session.Duration:hh\\:mm\\:ss}\n\n";

        return $"<|im_start|>system\n{systemPrompt.Trim()}<|im_end|>\n" +
               $"<|im_start|>user\n以下の会議文字起こしから議事録を作成してください。\n\n{header}{transcript.Trim()} /no_think<|im_end|>\n" +
               $"<|im_start|>assistant\n";
    }

    /// <summary>CUDA 関連 DLL のロード可否を診断ログに出力する</summary>
    private void LogCudaDllAvailability()
    {
        string[] cudaDlls = ["nvcuda.dll", "cudart64_12.dll", "cublas64_12.dll",
                             "cublasLt64_12.dll"];
        foreach (var dll in cudaDlls)
        {
            var loaded = NativeLibrary.TryLoad(dll, out var handle);
            if (loaded && handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
                _logger.LogDebug("[LLM] CUDA DLL check: {Dll} = OK", dll);
            }
            else
            {
                _logger.LogWarning("[LLM] CUDA DLL check: {Dll} = NOT FOUND", dll);
            }
        }
    }

    /// <summary>モデルロード後のバックエンド情報をログ出力する</summary>
    private void LogBackendInfo()
    {
        try
        {
            var infoPtr = NativeApi.llama_print_system_info();
            var systemInfo = Marshal.PtrToStringUTF8(infoPtr) ?? "Unknown";
            var isCuda = systemInfo.Contains("CUDA", StringComparison.OrdinalIgnoreCase)
                      || systemInfo.Contains("cublas", StringComparison.OrdinalIgnoreCase);
            LoadedBackendInfo = systemInfo;
            _logger.LogInformation("[LLM] System info: {SystemInfo}", systemInfo);
            _logger.LogInformation("[LLM] GPU acceleration: {GpuEnabled}",
                isCuda ? "有効 (CUDA)" : "無効 - CPUフォールバック");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLM] Failed to get system info");
            LoadedBackendInfo = "Unknown";
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _model = null;
        _modelLock.Dispose();
    }
}
#endif
