using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Session;

/// <summary>
/// セッションメタデータの永続化を管理するサービス
/// </summary>
public interface ISessionService
{
    /// <summary>設定またはデフォルトのセッション保存ルートを返す</summary>
    string GetSessionsRoot();

    /// <summary>セッションパスがセッションルート配下かを検証する</summary>
    bool IsValidSessionPath(string folderPath);

    /// <summary>新規セッションを作成し、session.json を初期保存する</summary>
    Task<RecordingSession> CreateSessionAsync(string sessionFolder, string inputDeviceName, string outputDeviceName);

    /// <summary>セッションを完了状態にし、session.json を更新する</summary>
    Task CompleteSessionAsync(RecordingSession session);

    /// <summary>セッションの状態を更新し、session.json を保存する</summary>
    Task UpdateSessionAsync(RecordingSession session);

    /// <summary>session.json からセッションを読み込む</summary>
    Task<RecordingSession?> LoadSessionAsync(string sessionFolder);

    /// <summary>保存済みの全セッションを読み込む（新しい順）</summary>
    Task<List<RecordingSession>> GetAllSessionsAsync();

    /// <summary>セッションとそのデータ（音声・文字起こし・議事録）をすべて削除する</summary>
    Task DeleteSessionAsync(RecordingSession session);
}
