using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Minutes;

/// <summary>
/// 文字起こし結果から議事録を生成するサービス
/// </summary>
public interface IMinutesGenerator
{
    /// <summary>文字起こしセグメントから議事録マークダウンを生成する</summary>
    Task<MinutesResult> GenerateAsync(RecordingSession session, List<TranscriptSegment> segments, CancellationToken cancellationToken = default);

    /// <summary>追加コンテキスト情報を使って議事録を再生成する（ととのえ機能用）</summary>
    Task<MinutesResult> GenerateWithContextAsync(RecordingSession session, List<TranscriptSegment> segments, TotonoeContext context, CancellationToken cancellationToken = default)
        => GenerateAsync(session, segments, cancellationToken);
}
