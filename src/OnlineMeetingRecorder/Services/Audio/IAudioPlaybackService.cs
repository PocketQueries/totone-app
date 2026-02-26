namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// 音声ファイルの再生サービス。
/// NAudio WaveOutEvent を使用してWAVファイルを再生する。
/// </summary>
public interface IAudioPlaybackService : IDisposable
{
    /// <summary>現在の再生状態</summary>
    PlaybackState State { get; }

    /// <summary>現在の再生位置</summary>
    TimeSpan CurrentPosition { get; }

    /// <summary>音声ファイルの総再生時間</summary>
    TimeSpan TotalDuration { get; }

    /// <summary>音声ファイルをロードする</summary>
    void Load(string wavFilePath);

    /// <summary>再生を開始する</summary>
    void Play();

    /// <summary>再生を一時停止する</summary>
    void Pause();

    /// <summary>再生を停止してファイルをアンロードする</summary>
    void Stop();

    /// <summary>指定位置にシークする</summary>
    void Seek(TimeSpan position);

    /// <summary>ファイルハンドルを解放する（削除前に呼び出す）</summary>
    void Unload();

    /// <summary>再生状態が変化したときに発火</summary>
    event EventHandler<PlaybackState>? StateChanged;

    /// <summary>再生位置が更新されたときに発火 (~30fps)</summary>
    event EventHandler<TimeSpan>? PositionChanged;
}

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}
