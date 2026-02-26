using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// 音声バッファからPeak/RMSレベルを計算するステートレスなユーティリティ。
/// キャプチャコールバック内で呼ばれるため、アロケーションを最小限に抑える。
/// </summary>
public static class AudioLevelMeter
{
    private const float SilenceThreshold = 0.001f;  // ~-60dB
    private const float ClippingThreshold = 0.99f;

    /// <summary>
    /// バイトバッファ（IEEE 32bit float）からレベルを計算する
    /// </summary>
    public static AudioLevelData Calculate(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 4)
            return AudioLevelData.Empty;

        int sampleCount = bytesRecorded / 4;
        float peak = 0f;
        double sumSquares = 0.0;

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = BitConverter.ToSingle(buffer, i * 4);
            float abs = MathF.Abs(sample);
            if (abs > peak) peak = abs;
            sumSquares += (double)sample * sample;
        }

        float rms = (float)Math.Sqrt(sumSquares / sampleCount);

        return new AudioLevelData
        {
            Peak = Math.Min(peak, 1.5f),  // 極端な値をクリップ
            Rms = rms,
            PeakDb = 20f * MathF.Log10(MathF.Max(peak, 1e-10f)),
            RmsDb = 20f * MathF.Log10(MathF.Max(rms, 1e-10f)),
            IsClipping = peak >= ClippingThreshold,
            IsSilent = rms < SilenceThreshold
        };
    }
}
