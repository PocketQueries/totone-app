using System.IO;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// マイク + スピーカー出力の同時録音を管理するオーケストレータ。
/// 各キャプチャは独立したWAVファイルに書き込む。
/// </summary>
public class AudioRecorder : IAudioRecorder
{
    private readonly IAudioDeviceService _deviceService;
    private readonly ILogger<AudioRecorder> _logger;

    private MicCaptureSource? _micSource;
    private LoopbackCaptureSource? _loopbackSource;
    private WaveFileWriter? _micWriter;
    private WaveFileWriter? _speakerWriter;

    private readonly AudioHealthMonitor _micHealthMonitor = new();
    private readonly AudioHealthMonitor _speakerHealthMonitor = new();

    private System.Timers.Timer? _levelTimer;
    private System.Timers.Timer? _flushTimer;
    private DateTime _recordingStartTime;
    private TimeSpan _pausedDuration;
    private DateTime? _pauseStartTime;

    // スレッドセーフなレベルデータ（lockで保護）
    private AudioLevelData _latestMicLevel;
    private AudioLevelData _latestSpeakerLevel;
    private readonly object _levelLock = new();

    private bool _isPaused;
    private readonly object _writeLock = new();

    public RecorderState State { get; private set; } = RecorderState.Idle;

    public TimeSpan Elapsed
    {
        get
        {
            if (State == RecorderState.Idle) return TimeSpan.Zero;
            var baseElapsed = DateTime.UtcNow - _recordingStartTime - _pausedDuration;
            if (_isPaused && _pauseStartTime.HasValue)
                baseElapsed -= (DateTime.UtcNow - _pauseStartTime.Value);
            return baseElapsed > TimeSpan.Zero ? baseElapsed : TimeSpan.Zero;
        }
    }

    public event EventHandler<DualLevelEventArgs>? LevelsUpdated;
    public event EventHandler<HealthChangedEventArgs>? HealthChanged;
    public event EventHandler<RecorderState>? StateChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public AudioRecorder(IAudioDeviceService deviceService, ILogger<AudioRecorder> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    public void StartRecording(string inputDeviceId, string outputDeviceId, string sessionFolder)
    {
        if (State != RecorderState.Idle)
            throw new InvalidOperationException("既に録音中です。");

        try
        {
            _currentSessionFolder = sessionFolder;
            var audioFolder = Path.Combine(sessionFolder, "audio");
            Directory.CreateDirectory(audioFolder);

            // マイクキャプチャの初期化
            var inputDevice = ((AudioDeviceService)_deviceService).GetMMDevice(inputDeviceId);
            _micSource = new MicCaptureSource();
            _micSource.Initialize(inputDevice);
            _micWriter = new WaveFileWriter(
                Path.Combine(audioFolder, "mic.wav"),
                _micSource.WaveFormat!);
            _micSource.DataAvailable += OnMicDataAvailable;
            _micSource.Stopped += OnMicStopped;

            // ループバックキャプチャの初期化
            var outputDevice = ((AudioDeviceService)_deviceService).GetMMDevice(outputDeviceId);
            _loopbackSource = new LoopbackCaptureSource();
            _loopbackSource.Initialize(outputDevice);
            _speakerWriter = new WaveFileWriter(
                Path.Combine(audioFolder, "speaker.wav"),
                _loopbackSource.WaveFormat!);
            _loopbackSource.DataAvailable += OnSpeakerDataAvailable;
            _loopbackSource.Stopped += OnSpeakerStopped;

            // ヘルスモニターをリセット
            _micHealthMonitor.Reset();
            _speakerHealthMonitor.Reset();

            // レベルメーター更新タイマー (~30fps)
            _levelTimer = new System.Timers.Timer(33);
            _levelTimer.Elapsed += OnLevelTimerElapsed;
            _levelTimer.AutoReset = true;

            // WAV定期フラッシュタイマー (5秒ごと)
            _flushTimer = new System.Timers.Timer(5000);
            _flushTimer.Elapsed += OnFlushTimerElapsed;
            _flushTimer.AutoReset = true;

            // タイムスタンプ初期化
            _recordingStartTime = DateTime.UtcNow;
            _pausedDuration = TimeSpan.Zero;
            _pauseStartTime = null;
            _isPaused = false;

            // キャプチャ開始（できるだけ近いタイミングで）
            _micSource.Start();
            _loopbackSource.Start();
            _levelTimer.Start();
            _flushTimer.Start();

            SetState(RecorderState.Recording);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "録音開始に失敗");
            Cleanup();
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
    }

    public void PauseRecording()
    {
        if (State != RecorderState.Recording) return;

        _isPaused = true;
        _pauseStartTime = DateTime.UtcNow;
        SetState(RecorderState.Paused);
    }

    public void ResumeRecording()
    {
        if (State != RecorderState.Paused) return;

        if (_pauseStartTime.HasValue)
            _pausedDuration += DateTime.UtcNow - _pauseStartTime.Value;
        _pauseStartTime = null;
        _isPaused = false;
        SetState(RecorderState.Recording);
    }

    public void StopRecording()
    {
        if (State == RecorderState.Idle || State == RecorderState.Stopping) return;

        SetState(RecorderState.Stopping);

        try
        {
            _levelTimer?.Stop();
            _flushTimer?.Stop();

            _micSource?.Stop();
            _loopbackSource?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "録音停止中にエラー");
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            var sessionFolder = GetCurrentSessionFolder();
            Cleanup();

            // WAVヘッダ修復（クラッシュ時の不正ヘッダを自動修復）
            if (sessionFolder != null)
                WavHeaderRepairService.RepairSessionWavFiles(sessionFolder);

            SetState(RecorderState.Idle);
        }
    }

    private void OnMicDataAvailable(object? sender, AudioDataEventArgs e)
    {
        // レベル計算（軽量処理のみ）
        var level = AudioLevelMeter.Calculate(e.Buffer, e.BytesRecorded);
        lock (_levelLock) { _latestMicLevel = level; }
        _micHealthMonitor.OnDataReceived(level);

        // WAVへの書き込み（一時停止中はスキップ）
        if (!_isPaused)
        {
            lock (_writeLock)
            {
                try
                {
                    _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }
    }

    private void OnSpeakerDataAvailable(object? sender, AudioDataEventArgs e)
    {
        var level = AudioLevelMeter.Calculate(e.Buffer, e.BytesRecorded);
        lock (_levelLock) { _latestSpeakerLevel = level; }
        _speakerHealthMonitor.OnDataReceived(level);

        if (!_isPaused)
        {
            lock (_writeLock)
            {
                try
                {
                    _speakerWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }
    }

    private void OnMicStopped(object? sender, CaptureStoppedEventArgs e)
    {
        if (e.Exception != null)
            ErrorOccurred?.Invoke(this, e.Exception);
    }

    private void OnSpeakerStopped(object? sender, CaptureStoppedEventArgs e)
    {
        if (e.Exception != null)
            ErrorOccurred?.Invoke(this, e.Exception);
    }

    private void OnLevelTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // ヘルスチェック（特にLoopbackの無音検知）
        _micHealthMonitor.CheckForStall();
        _speakerHealthMonitor.CheckForStall();

        AudioLevelData micLevel, speakerLevel;
        lock (_levelLock)
        {
            micLevel = _latestMicLevel;
            speakerLevel = _latestSpeakerLevel;
        }

        LevelsUpdated?.Invoke(this, new DualLevelEventArgs
        {
            MicLevel = micLevel,
            SpeakerLevel = speakerLevel
        });

        HealthChanged?.Invoke(this, new HealthChangedEventArgs
        {
            MicHealth = _micHealthMonitor.CurrentStatus,
            SpeakerHealth = _speakerHealthMonitor.CurrentStatus
        });
    }

    private void OnFlushTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_writeLock)
        {
            try
            {
                _micWriter?.Flush();
                _speakerWriter?.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WAV フラッシュに失敗（次回で回復を期待）");
            }
        }
    }

    private void Cleanup()
    {
        _levelTimer?.Stop();
        _levelTimer?.Dispose();
        _levelTimer = null;

        _flushTimer?.Stop();
        _flushTimer?.Dispose();
        _flushTimer = null;

        if (_micSource != null)
        {
            _micSource.DataAvailable -= OnMicDataAvailable;
            _micSource.Stopped -= OnMicStopped;
            _micSource.Dispose();
            _micSource = null;
        }

        if (_loopbackSource != null)
        {
            _loopbackSource.DataAvailable -= OnSpeakerDataAvailable;
            _loopbackSource.Stopped -= OnSpeakerStopped;
            _loopbackSource.Dispose();
            _loopbackSource = null;
        }

        lock (_writeLock)
        {
            _micWriter?.Dispose();
            _micWriter = null;
            _speakerWriter?.Dispose();
            _speakerWriter = null;
        }
    }

    private string? _currentSessionFolder;

    private string? GetCurrentSessionFolder() => _currentSessionFolder;

    private void SetState(RecorderState newState)
    {
        _logger.LogInformation("録音状態変更: {State}", newState);
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        if (State != RecorderState.Idle)
            StopRecording(); // 内部で Cleanup() が呼ばれる
        else
            Cleanup(); // Idle 状態の場合のみ直接 Cleanup
    }
}
