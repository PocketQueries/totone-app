using NAudio.Wave;

namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// NAudio WaveOutEvent を使用した音声再生サービス。
/// マイク・スピーカーの個別WAVファイルの再生に対応する。
/// </summary>
public class AudioPlaybackService : IAudioPlaybackService
{
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioReader;
    private System.Timers.Timer? _positionTimer;
    private readonly object _playbackLock = new();

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_playbackLock)
            {
                return _audioReader?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    public TimeSpan TotalDuration
    {
        get
        {
            lock (_playbackLock)
            {
                return _audioReader?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;

    public void Load(string wavFilePath)
    {
        lock (_playbackLock)
        {
            CleanupPlaybackCore();

            _audioReader = new AudioFileReader(wavFilePath);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioReader);
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            // 位置更新タイマー (~30fps)
            _positionTimer = new System.Timers.Timer(33);
            _positionTimer.Elapsed += (_, _) =>
            {
                if (State == PlaybackState.Playing)
                    PositionChanged?.Invoke(this, CurrentPosition);
            };
            _positionTimer.AutoReset = true;
        }

        SetState(PlaybackState.Stopped);
    }

    public void Play()
    {
        lock (_playbackLock)
        {
            if (_waveOut == null || _audioReader == null) return;

            _waveOut.Play();
            _positionTimer?.Start();
        }

        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        lock (_playbackLock)
        {
            if (_waveOut == null || State != PlaybackState.Playing) return;

            _waveOut.Pause();
            _positionTimer?.Stop();
        }

        PositionChanged?.Invoke(this, CurrentPosition);
        SetState(PlaybackState.Paused);
    }

    public void Stop()
    {
        lock (_playbackLock)
        {
            if (_waveOut == null) return;

            _waveOut.Stop();
            _positionTimer?.Stop();

            if (_audioReader != null)
                _audioReader.Position = 0;
        }

        PositionChanged?.Invoke(this, TimeSpan.Zero);
        SetState(PlaybackState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        lock (_playbackLock)
        {
            if (_audioReader == null) return;

            // 範囲内に制約
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            var total = _audioReader.TotalTime;
            if (position > total) position = total;

            _audioReader.CurrentTime = position;
        }

        PositionChanged?.Invoke(this, position);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_playbackLock)
        {
            _positionTimer?.Stop();
        }

        if (e.Exception != null)
        {
            SetState(PlaybackState.Stopped);
            return;
        }

        // Stop() が明示的に呼ばれた場合は既に状態が設定済み
        if (State == PlaybackState.Stopped)
            return;

        // 最後まで再生した場合はStoppedに
        lock (_playbackLock)
        {
            if (_audioReader != null &&
                _audioReader.CurrentTime >= _audioReader.TotalTime - TimeSpan.FromMilliseconds(100))
            {
                _audioReader.Position = 0;
            }
        }

        PositionChanged?.Invoke(this, TimeSpan.Zero);
        SetState(PlaybackState.Stopped);
    }

    private void SetState(PlaybackState newState)
    {
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Unload()
    {
        lock (_playbackLock)
        {
            CleanupPlaybackCore();
        }
        SetState(PlaybackState.Stopped);
    }

    /// <summary>lock の外から呼ぶ場合用</summary>
    private void CleanupPlayback()
    {
        lock (_playbackLock)
        {
            CleanupPlaybackCore();
        }
    }

    /// <summary>lock 内で呼ぶ実装</summary>
    private void CleanupPlaybackCore()
    {
        _positionTimer?.Stop();
        _positionTimer?.Dispose();
        _positionTimer = null;

        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }

        _audioReader?.Dispose();
        _audioReader = null;

        State = PlaybackState.Stopped;
    }

    public void Dispose()
    {
        CleanupPlayback();
    }
}
