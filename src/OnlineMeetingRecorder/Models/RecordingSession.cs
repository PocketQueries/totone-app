namespace OnlineMeetingRecorder.Models;

/// <summary>
/// 録音セッションのメタデータ
/// </summary>
public class RecordingSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;

    /// <summary>表示用セッション名（Title未設定時は日時を返す）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Title)
        ? StartTime.ToString("yyyy/MM/dd HH:mm")
        : Title;
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>使用したマイクデバイス名</summary>
    public string InputDeviceName { get; set; } = string.Empty;

    /// <summary>使用したスピーカーデバイス名</summary>
    public string OutputDeviceName { get; set; } = string.Empty;

    /// <summary>セッション状態</summary>
    public SessionStatus Status { get; set; } = SessionStatus.Recording;

    // 将来のカレンダー連携用
    public string? CalendarEventTitle { get; set; }
    public List<string>? Participants { get; set; }
}

public enum SessionStatus
{
    Recording,
    Completed,
    Transcribing,
    Transcribed,
    Error
}
