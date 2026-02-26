using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Settings;

/// <summary>
/// アプリケーション設定の永続化を管理するサービス
/// </summary>
public interface ISettingsService
{
    /// <summary>現在の設定</summary>
    AppSettings Settings { get; }

    /// <summary>設定ファイルからロードする</summary>
    Task LoadAsync();

    /// <summary>設定ファイルに保存する</summary>
    Task SaveAsync();
}
