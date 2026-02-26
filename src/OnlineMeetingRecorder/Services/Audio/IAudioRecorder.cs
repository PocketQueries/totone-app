using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Audio;

public interface IAudioRecorder : IDisposable
{
    RecorderState State { get; }
    TimeSpan Elapsed { get; }

    void StartRecording(string inputDeviceId, string outputDeviceId, string sessionFolder);
    void PauseRecording();
    void ResumeRecording();
    void StopRecording();

    /// <summary>~30Hzでレベルデータを通知</summary>
    event EventHandler<DualLevelEventArgs>? LevelsUpdated;

    /// <summary>ヘルスステータス変化を通知</summary>
    event EventHandler<HealthChangedEventArgs>? HealthChanged;

    /// <summary>録音状態変化を通知</summary>
    event EventHandler<RecorderState>? StateChanged;

    /// <summary>エラー発生を通知</summary>
    event EventHandler<Exception>? ErrorOccurred;
}

public enum RecorderState
{
    Idle,
    Recording,
    Paused,
    Stopping
}

public class DualLevelEventArgs : EventArgs
{
    public AudioLevelData MicLevel { get; init; }
    public AudioLevelData SpeakerLevel { get; init; }
}

public class HealthChangedEventArgs : EventArgs
{
    public HealthStatus MicHealth { get; init; }
    public HealthStatus SpeakerHealth { get; init; }
}
