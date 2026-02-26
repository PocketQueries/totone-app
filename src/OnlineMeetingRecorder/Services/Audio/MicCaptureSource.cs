using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// WASAPI SharedMode によるマイクキャプチャ。
/// ミーティングアプリのデバイス使用を妨げない。
/// </summary>
public class MicCaptureSource : ICaptureSource
{
    private WasapiCapture? _capture;

    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public bool IsCapturing => _capture != null;

    public event EventHandler<AudioDataEventArgs>? DataAvailable;
    public event EventHandler<CaptureStoppedEventArgs>? Stopped;

    public void Initialize(MMDevice device)
    {
        Dispose();
        // SharedMode: デバイスを他のアプリと共有（デフォルトがShared）
        _capture = new WasapiCapture(device);
        _capture.DataAvailable += OnNativeDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public void Start()
    {
        _capture?.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
    }

    private void OnNativeDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        DataAvailable?.Invoke(this, new AudioDataEventArgs
        {
            Buffer = e.Buffer,
            BytesRecorded = e.BytesRecorded,
            Format = _capture!.WaveFormat
        });
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Stopped?.Invoke(this, new CaptureStoppedEventArgs
        {
            Exception = e.Exception
        });
    }

    public void Dispose()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnNativeDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
    }
}
