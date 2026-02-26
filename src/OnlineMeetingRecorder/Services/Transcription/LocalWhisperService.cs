using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace OnlineMeetingRecorder.Services.Transcription;

/// <summary>
/// Whisper.net を使用したローカル文字起こしサービス（オフライン動作）。
/// モデルをキャッシュして再利用し、ロード時間を削減する。
/// </summary>
public class LocalWhisperService : ITranscriptionService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ILogger<LocalWhisperService> _logger;
    private WhisperFactory? _cachedFactory;
    private string? _cachedModelPath;

    public string Name => "Whisper.net (ローカル)";

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_settings.Settings.WhisperModelPath) &&
        File.Exists(_settings.Settings.WhisperModelPath);

    public LocalWhisperService(ISettingsService settings, ILogger<LocalWhisperService> logger)
    {
        _settings = settings;
        _logger = logger;

        // CUDA ランタイムを優先し、利用不可時は CPU にフォールバック
        // ※ WhisperFactory 生成前に設定する必要がある
        RuntimeOptions.RuntimeLibraryOrder =
        [
            RuntimeLibrary.Cuda,
            RuntimeLibrary.Cpu,
        ];
    }

    // 2分ごとにチャンク分割（baseモデルでの長時間音声ハルシネーションを防止）
    private const int SamplesPerChunk = 2 * 60 * 16000;

    public async Task<List<TranscriptSegment>> TranscribeAsync(
        string wavFilePath,
        string speaker,
        string language,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                "Whisperモデルファイルが見つかりません。設定画面でモデルファイルのパスを指定してください。");

        if (!File.Exists(wavFilePath))
            throw new FileNotFoundException($"音声ファイルが見つかりません: {wavFilePath}");

        progress?.Report(5);

        // 16kHz mono float32 に変換
        var samples = await AudioConverter.ConvertToWhisperSamplesAsync(wavFilePath, cancellationToken);
        progress?.Report(20);

        // モデルをキャッシュから取得または新規ロード
        var factory = GetOrCreateFactory();
        progress?.Report(25);

        var allSegments = new List<TranscriptSegment>();
        var chunkCount = Math.Max(1, (int)Math.Ceiling((double)samples.Length / SamplesPerChunk));

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = chunkIndex * SamplesPerChunk;
            var length = Math.Min(SamplesPerChunk, samples.Length - start);
            var chunkSamples = new float[length];
            Array.Copy(samples, start, chunkSamples, 0, length);

            var timeOffset = TimeSpan.FromSeconds((double)start / 16000);

            // チャンクごとに processor を再生成してモデル状態をリセット
            var builder = factory.CreateBuilder()
                .WithLanguage(language);

            if (language == "ja")
                builder.WithPrompt("以下は日本語の会議の書き起こしです。");

            using var processor = builder.Build();

            string? prevText = null;
            await foreach (var segment in processor.ProcessAsync(chunkSamples, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = segment.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(text) && !IsHallucination(text, prevText))
                {
                    allSegments.Add(new TranscriptSegment
                    {
                        Start = segment.Start + timeOffset,
                        End = segment.End + timeOffset,
                        Text = text,
                        Speaker = speaker
                    });
                }
                prevText = text;
            }

            // Progress: 25-95 をチャンク数で等分
            var chunkProgress = 25 + (int)(70.0 * (chunkIndex + 1) / chunkCount);
            progress?.Report(Math.Min(95, chunkProgress));
        }

        progress?.Report(100);
        return allSegments;
    }

    // Whisper が無音区間や音声末尾で出力しがちな典型的ハルシネーションフレーズ
    private static readonly string[] HallucinationPhrases =
    [
        "ご視聴ありがとうございました",
        "チャンネル登録",
        "お願いします",
        "おやすみなさい",
        "Thank you for watching",
        "Thanks for watching",
        "Subscribe",
        "Please subscribe",
        "Amara.org",
        "www.mooji.org",
        "Sous-titres réalisés",
        "Subtitles by",
    ];

    /// <summary>
    /// Whisper のハルシネーション（幻覚出力）を検出する。
    /// 典型的なフレーズ一致、および直前セグメントとの繰り返しをチェック。
    /// </summary>
    private static bool IsHallucination(string text, string? previousText)
    {
        // 典型的ハルシネーションフレーズに一致
        foreach (var phrase in HallucinationPhrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 同一テキストの連続繰り返し
        if (previousText != null && text == previousText)
            return true;

        // テキスト内で同じ短いフレーズが3回以上繰り返される（例: "はいはいはい..."）
        // 4文字以上のフレーズの繰り返しを検出
        if (Regex.IsMatch(text, @"(.{4,})\1{2,}"))
            return true;

        return false;
    }

    /// <summary>ロードされた Whisper ランタイム名（CUDA / CPU 等）</summary>
    public string? LoadedRuntimeName { get; private set; }

    private WhisperFactory GetOrCreateFactory()
    {
        var modelPath = _settings.Settings.WhisperModelPath;

        // モデルパスが変わった場合は再ロード
        if (_cachedFactory != null && _cachedModelPath == modelPath)
            return _cachedFactory;

        _cachedFactory?.Dispose();

        // CUDA 依存 DLL の事前チェック（ロード失敗時の診断用）
        LogCudaDllAvailability();

        _cachedFactory = WhisperFactory.FromPath(modelPath);
        _cachedModelPath = modelPath;

        // ロードされたランタイムを記録・ログ出力
        LoadedRuntimeName = RuntimeOptions.LoadedLibrary?.ToString() ?? "Unknown";
        var isCuda = LoadedRuntimeName?.Contains("Cuda", StringComparison.OrdinalIgnoreCase) == true;
        _logger.LogInformation("[Whisper] Loaded runtime: {Runtime} (GPU acceleration: {GpuEnabled})",
            LoadedRuntimeName, isCuda ? "有効" : "無効 - CPUフォールバック");

        return _cachedFactory;
    }

    /// <summary>CUDA 関連 DLL のロード可否を診断ログに出力する</summary>
    private void LogCudaDllAvailability()
    {
        string[] cudaDlls = ["nvcuda.dll", "cudart64_12.dll", "cublas64_12.dll",
                             "cudart64_13.dll", "cublas64_13.dll", "nvcudart_hybrid64.dll"];
        foreach (var dll in cudaDlls)
        {
            var loaded = NativeLibrary.TryLoad(dll, out var handle);
            if (loaded && handle != IntPtr.Zero)
            {
                NativeLibrary.Free(handle);
                _logger.LogDebug("[Whisper] CUDA DLL check: {Dll} = OK", dll);
            }
            else
            {
                _logger.LogDebug("[Whisper] CUDA DLL check: {Dll} = NOT FOUND", dll);
            }
        }
    }

    public void Dispose()
    {
        _cachedFactory?.Dispose();
        _cachedFactory = null;
    }
}
