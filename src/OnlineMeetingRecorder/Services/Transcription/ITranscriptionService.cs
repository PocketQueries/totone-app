using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Transcription;

/// <summary>
/// 音声ファイルの文字起こしを行うサービス
/// </summary>
public interface ITranscriptionService
{
    /// <summary>サービス名（UI表示用）</summary>
    string Name { get; }

    /// <summary>利用可能かどうか（APIキー設定済み、モデルファイル存在等）</summary>
    bool IsAvailable { get; }

    /// <summary>WAVファイルを文字起こしする</summary>
    /// <param name="wavFilePath">入力WAVファイルパス</param>
    /// <param name="speaker">話者識別（"mic" or "speaker"）</param>
    /// <param name="language">言語コード（例: "ja"）</param>
    /// <param name="progress">進捗通知（0-100）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>文字起こしセグメントのリスト</returns>
    Task<List<TranscriptSegment>> TranscribeAsync(
        string wavFilePath,
        string speaker,
        string language,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
