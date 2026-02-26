using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Audio;

namespace OnlineMeetingRecorder.ViewModels;

/// <summary>
/// 音声再生・波形表示・文字起こし同期の ViewModel。
/// セッション選択時にロードされ、再生操作とUI状態を管理する。
/// </summary>
public partial class PlaybackViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService _playbackService;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _currentPositionSeconds;

    [ObservableProperty]
    private double _totalDurationSeconds;

    [ObservableProperty]
    private string _positionText = "00:00";

    [ObservableProperty]
    private string _durationText = "00:00";

    /// <summary>現在選択中のトラック</summary>
    [ObservableProperty]
    private PlaybackTrack _selectedTrack = PlaybackTrack.Mic;

    /// <summary>現在ロード中のセッション</summary>
    [ObservableProperty]
    private RecordingSession? _currentSession;

    /// <summary>文字起こしセグメント一覧</summary>
    public ObservableCollection<TranscriptSegment> Segments { get; } = new();

    /// <summary>再生位置に対応するアクティブなセグメントのインデックス</summary>
    [ObservableProperty]
    private int _highlightedSegmentIndex = -1;

    /// <summary>WAVファイルのパス（波形描画用）</summary>
    [ObservableProperty]
    private string? _currentWavPath;

    private bool _isSeeking;

    public PlaybackViewModel(IAudioPlaybackService playbackService)
    {
        _playbackService = playbackService;
        _playbackService.PositionChanged += OnPositionChanged;
        _playbackService.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// セッションと文字起こしデータをロードする
    /// </summary>
    public void LoadSession(RecordingSession session, IReadOnlyList<TranscriptSegment> segments)
    {
        Stop();

        CurrentSession = session;
        Segments.Clear();
        foreach (var seg in segments)
            Segments.Add(seg);

        HighlightedSegmentIndex = -1;

        // デフォルトトラックをロード
        LoadTrack(SelectedTrack);
    }

    /// <summary>指定トラックのWAVファイルをロードする</summary>
    private void LoadTrack(PlaybackTrack track)
    {
        if (CurrentSession == null) return;

        var fileName = track == PlaybackTrack.Mic ? "mic.wav" : "speaker.wav";
        var wavPath = Path.Combine(CurrentSession.FolderPath, "audio", fileName);

        if (!File.Exists(wavPath))
        {
            IsLoaded = false;
            CurrentWavPath = null;
            return;
        }

        _playbackService.Load(wavPath);
        CurrentWavPath = wavPath;
        TotalDurationSeconds = _playbackService.TotalDuration.TotalSeconds;
        DurationText = FormatTime(_playbackService.TotalDuration);
        CurrentPositionSeconds = 0;
        PositionText = "00:00";
        IsLoaded = true;
    }

    partial void OnSelectedTrackChanged(PlaybackTrack value)
    {
        var wasPlaying = IsPlaying;
        Stop();
        LoadTrack(value);
        if (wasPlaying)
            Play();
    }

    /// <summary>
    /// Slider のドラッグによるシーク（UIからバインド用）。
    /// PositionChangedイベントとの循環を防ぐためフラグで制御する。
    /// </summary>
    partial void OnCurrentPositionSecondsChanged(double value)
    {
        if (_isSeeking) return;
        _isSeeking = true;
        _playbackService.Seek(TimeSpan.FromSeconds(value));
        PositionText = FormatTime(TimeSpan.FromSeconds(value));
        UpdateHighlightedSegment(TimeSpan.FromSeconds(value));
        _isSeeking = false;
    }

    [RelayCommand]
    private void Play()
    {
        if (!IsLoaded) return;
        _playbackService.Play();
    }

    [RelayCommand]
    private void Pause()
    {
        _playbackService.Pause();
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!IsLoaded) return;

        if (IsPlaying)
            Pause();
        else
            Play();
    }

    [RelayCommand]
    private void Stop()
    {
        _playbackService.Stop();
        HighlightedSegmentIndex = -1;
    }

    /// <summary>セグメントクリック時に該当位置へシークする</summary>
    [RelayCommand]
    private void SeekToSegment(TranscriptSegment segment)
    {
        if (!IsLoaded) return;

        _playbackService.Seek(segment.Start);

        if (!IsPlaying)
            Play();
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_isSeeking) return;

        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            _isSeeking = true;
            CurrentPositionSeconds = position.TotalSeconds;
            PositionText = FormatTime(position);
            UpdateHighlightedSegment(position);
            _isSeeking = false;
        });
    }

    private void OnStateChanged(object? sender, PlaybackState state)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            IsPlaying = state == PlaybackState.Playing;
            IsPaused = state == PlaybackState.Paused;
        });
    }

    /// <summary>再生位置に対応するセグメントを二分探索でハイライトする</summary>
    private void UpdateHighlightedSegment(TimeSpan position)
    {
        // セグメントは Start 昇順ソート済み → 二分探索で O(log n)
        int lo = 0, hi = Segments.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (position < Segments[mid].Start)
                hi = mid - 1;
            else if (position > Segments[mid].End)
                lo = mid + 1;
            else
            {
                HighlightedSegmentIndex = mid;
                return;
            }
        }
        HighlightedSegmentIndex = -1;
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }

    /// <summary>セッションをアンロードしてファイルハンドルを解放する</summary>
    public void UnloadSession()
    {
        Stop();
        _playbackService.Unload();
        CurrentSession = null;
        CurrentWavPath = null;
        IsLoaded = false;
        Segments.Clear();
        HighlightedSegmentIndex = -1;
    }

    public void Dispose()
    {
        Stop();
        _playbackService.PositionChanged -= OnPositionChanged;
        _playbackService.StateChanged -= OnStateChanged;
    }
}

/// <summary>再生トラック選択</summary>
public enum PlaybackTrack
{
    Mic,
    Speaker
}
