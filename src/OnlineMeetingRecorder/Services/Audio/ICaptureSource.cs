using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OnlineMeetingRecorder.Services.Audio;

public interface ICaptureSource : IDisposable
{
    WaveFormat? WaveFormat { get; }
    bool IsCapturing { get; }

    void Initialize(MMDevice device);
    void Start();
    void Stop();

    /// <summary>
    /// 音声データ受信時に発火。バッファはIEEE float サンプル。
    /// キャプチャスレッド上で呼ばれる。
    /// </summary>
    event EventHandler<AudioDataEventArgs>? DataAvailable;
    event EventHandler<CaptureStoppedEventArgs>? Stopped;
}

public class AudioDataEventArgs : EventArgs
{
    public required byte[] Buffer { get; init; }
    public int BytesRecorded { get; init; }
    public required WaveFormat Format { get; init; }
}

public class CaptureStoppedEventArgs : EventArgs
{
    public Exception? Exception { get; init; }
}
