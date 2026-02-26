using System.IO;
using NAudio.Lame;
using NAudio.MediaFoundation;
using NAudio.Wave;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Transcription;

/// <summary>
/// Whisper 処理用の音声フォーマット変換ユーティリティ。
/// 録音WAV（32-bit float, 48kHz, stereo）を Whisper が期待する形式に変換する。
/// </summary>
public static class AudioConverter
{
    /// <summary>
    /// WAVファイルを 16kHz mono 16-bit PCM に変換する（Cloud API アップロード用）。
    /// ファイルサイズを大幅に削減してAPI制限（25MB）内に収める。
    /// </summary>
    public static async Task<string> ConvertTo16kHzMonoPcmAsync(string inputWavPath, CancellationToken ct = default)
    {
        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputWavPath)!,
            Path.GetFileNameWithoutExtension(inputWavPath) + "_16k.wav");

        await Task.Run(() =>
        {
            using var reader = new AudioFileReader(inputWavPath);
            var targetFormat = new WaveFormat(16000, 16, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat);
            resampler.ResamplerQuality = 60;
            WaveFileWriter.CreateWaveFile(outputPath, resampler);
        }, ct);

        return outputPath;
    }

    /// <summary>
    /// WAVファイルを 16kHz mono float32 サンプル配列に変換する（Whisper.net 用）。
    /// Whisper.net は float32 PCM サンプル配列を入力として受け取る。
    /// </summary>
    public static async Task<float[]> ConvertToWhisperSamplesAsync(string inputWavPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var reader = new AudioFileReader(inputWavPath);
            var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat);
            resampler.ResamplerQuality = 60;

            // float32 サンプルを読み取り
            // リサンプル後のサンプル数を推定し、List の倍々拡張によるメモリ浪費を回避
            var estimatedSeconds = reader.TotalTime.TotalSeconds;
            var estimatedSamples = (int)(estimatedSeconds * 16000) + 1024; // 16kHz mono + margin
            var samples = new List<float>(estimatedSamples);
            var buffer = new byte[4096];
            int bytesRead;

            while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < bytesRead; i += 4)
                {
                    if (i + 4 <= bytesRead)
                        samples.Add(BitConverter.ToSingle(buffer, i));
                }
            }

            return samples.ToArray();
        }, ct);
    }

    /// <summary>
    /// WAVファイルを MP3 に変換する（NAudio.Lame 使用）。
    /// </summary>
    public static async Task<string> ConvertToMp3Async(string inputWavPath, CancellationToken ct = default)
    {
        var outputPath = Path.ChangeExtension(inputWavPath, ".mp3");

        await Task.Run(() =>
        {
            using var reader = new AudioFileReader(inputWavPath);
            // 16kHz mono 16-bit に変換してからMP3エンコード（サイズ削減）
            var targetFormat = new WaveFormat(16000, 16, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat);
            resampler.ResamplerQuality = 60;
            using var writer = new LameMP3FileWriter(outputPath, targetFormat, LAMEPreset.STANDARD);

            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, bytesRead);
            }
        }, ct);

        return outputPath;
    }

    /// <summary>
    /// WAVファイルを FLAC に変換する（Windows MediaFoundation 使用）。
    /// Windows 10 以降で MediaFoundation FLAC エンコーダーが利用可能。
    /// 利用不可の場合は WAV のままパスを返す。
    /// </summary>
    public static async Task<string> ConvertToFlacAsync(string inputWavPath, CancellationToken ct = default)
    {
        var outputPath = Path.ChangeExtension(inputWavPath, ".flac");

        await Task.Run(() =>
        {
            using var reader = new AudioFileReader(inputWavPath);
            // 16-bit PCM に変換
            var targetFormat = new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels);
            using var resampler = new MediaFoundationResampler(reader, targetFormat);
            resampler.ResamplerQuality = 60;

            // MediaFoundation FLAC encoder (Windows 10+)
            var mediaType = MediaFoundationEncoder.SelectMediaType(
                AudioSubtypes.MFAudioFormat_FLAC, resampler.WaveFormat, 0);

            if (mediaType == null)
                throw new InvalidOperationException(
                    "FLAC エンコーダーが利用できません。Windows 10 以降が必要です。");

            using var encoder = new MediaFoundationEncoder(mediaType);
            encoder.Encode(outputPath, resampler);
        }, ct);

        return outputPath;
    }

    /// <summary>
    /// WAV ファイルが指定サイズを超える場合にチャンク分割する。
    /// 超えない場合は元ファイルをそのまま返す。
    /// OpenAI Whisper API のファイルサイズ制限（25MB）対策。
    /// </summary>
    public static async Task<List<(string Path, TimeSpan Offset)>> SplitWavIfNeededAsync(
        string wavPath, long maxFileSize = 24 * 1024 * 1024, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(wavPath);
        if (fileInfo.Length <= maxFileSize)
            return [(wavPath, TimeSpan.Zero)];

        return await Task.Run(() =>
        {
            var chunks = new List<(string Path, TimeSpan Offset)>();
            using var reader = new WaveFileReader(wavPath);
            var bytesPerSecond = reader.WaveFormat.AverageBytesPerSecond;

            // WAV ヘッダー分のマージンを確保
            var maxDataBytes = maxFileSize - 1024;
            var chunkDurationSec = (double)maxDataBytes / bytesPerSecond;
            var totalSeconds = reader.TotalTime.TotalSeconds;

            var offset = 0.0;
            var chunkIndex = 0;

            while (offset < totalSeconds)
            {
                ct.ThrowIfCancellationRequested();

                var chunkPath = Path.Combine(
                    Path.GetDirectoryName(wavPath)!,
                    $"{Path.GetFileNameWithoutExtension(wavPath)}_chunk{chunkIndex}.wav");

                var remainingSec = totalSeconds - offset;
                var thisDurationSec = Math.Min(chunkDurationSec, remainingSec);
                var bytesToWrite = (long)(thisDurationSec * bytesPerSecond);
                // BlockAlign 境界に揃える
                bytesToWrite -= bytesToWrite % reader.WaveFormat.BlockAlign;

                using (var writer = new WaveFileWriter(chunkPath, reader.WaveFormat))
                {
                    var buffer = new byte[8192];
                    long written = 0;
                    while (written < bytesToWrite)
                    {
                        ct.ThrowIfCancellationRequested();
                        var toRead = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        var read = reader.Read(buffer, 0, toRead);
                        if (read <= 0) break;
                        writer.Write(buffer, 0, read);
                        written += read;
                    }
                }

                chunks.Add((chunkPath, TimeSpan.FromSeconds(offset)));
                offset += chunkDurationSec;
                chunkIndex++;
            }

            return chunks;
        }, ct);
    }

    /// <summary>
    /// 指定フォーマットに変換する。WAVの場合は変換せずパスを返す。
    /// </summary>
    public static async Task<string> ConvertToFormatAsync(
        string inputWavPath, AudioExportFormat format, CancellationToken ct = default)
    {
        return format switch
        {
            AudioExportFormat.Mp3 => await ConvertToMp3Async(inputWavPath, ct),
            AudioExportFormat.Flac => await ConvertToFlacAsync(inputWavPath, ct),
            _ => inputWavPath
        };
    }

    /// <summary>
    /// マイクとスピーカーの2つのWAVファイルを合成し、軽量MP3に変換する（聞き返し用）。
    /// 16kHz mono 16-bit にリサンプル後、両トラックを加算合成して MP3 エンコード。
    /// </summary>
    public static async Task<string> MixToMp3Async(
        string micWavPath, string speakerWavPath,
        int bitrate = 64, CancellationToken ct = default)
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(micWavPath)!, "mixed.mp3");

        await Task.Run(() =>
        {
            var targetFormat = new WaveFormat(16000, 16, 1);

            using var micReader = new AudioFileReader(micWavPath);
            using var speakerReader = new AudioFileReader(speakerWavPath);

            using var micResampler = new MediaFoundationResampler(micReader, targetFormat);
            using var speakerResampler = new MediaFoundationResampler(speakerReader, targetFormat);
            micResampler.ResamplerQuality = 60;
            speakerResampler.ResamplerQuality = 60;

            using var mp3Writer = new LameMP3FileWriter(outputPath, targetFormat, bitrate);

            var micBuffer = new byte[4096];
            var speakerBuffer = new byte[4096];
            var mixedBuffer = new byte[4096];

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                int micRead = micResampler.Read(micBuffer, 0, micBuffer.Length);
                int speakerRead = speakerResampler.Read(speakerBuffer, 0, speakerBuffer.Length);

                if (micRead == 0 && speakerRead == 0) break;

                int maxRead = Math.Max(micRead, speakerRead);

                // 16-bit PCM サンプル単位で加算合成（クリッピング防止）
                for (int i = 0; i < maxRead; i += 2)
                {
                    short micSample = i + 1 < micRead
                        ? BitConverter.ToInt16(micBuffer, i)
                        : (short)0;
                    short speakerSample = i + 1 < speakerRead
                        ? BitConverter.ToInt16(speakerBuffer, i)
                        : (short)0;

                    int mixed = micSample + speakerSample;
                    short clamped = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    BitConverter.TryWriteBytes(mixedBuffer.AsSpan(i), clamped);
                }

                mp3Writer.Write(mixedBuffer, 0, maxRead);
            }
        }, ct);

        return outputPath;
    }
}
