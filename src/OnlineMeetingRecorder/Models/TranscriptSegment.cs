namespace OnlineMeetingRecorder.Models;

/// <summary>
/// タイムスタンプ付き文字起こしセグメント
/// </summary>
public class TranscriptSegment
{
    /// <summary>発話開始時刻</summary>
    public TimeSpan Start { get; set; }

    /// <summary>発話終了時刻</summary>
    public TimeSpan End { get; set; }

    /// <summary>文字起こしテキスト</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>話者識別 ("mic" = 自分, "speaker" = 相手)</summary>
    public string Speaker { get; set; } = string.Empty;

    public override string ToString()
    {
        var label = Speaker == "mic" ? "自分" : "相手";
        return $"[{Start:mm\\:ss} - {End:mm\\:ss}] {label}: {Text}";
    }
}
