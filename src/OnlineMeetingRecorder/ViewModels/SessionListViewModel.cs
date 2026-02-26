using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Session;
using OnlineMeetingRecorder.Views;

namespace OnlineMeetingRecorder.ViewModels;

public partial class SessionListViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    private readonly PlaybackViewModel _playback;
    private readonly ILogger<SessionListViewModel> _logger;

    public ObservableCollection<RecordingSession> Sessions { get; } = new();

    [ObservableProperty]
    private RecordingSession? _selectedSession;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>インライン編集中のセッション</summary>
    [ObservableProperty]
    private RecordingSession? _editingSession;

    /// <summary>編集中のセッション名</summary>
    [ObservableProperty]
    private string _editingTitle = string.Empty;

    /// <summary>セッション選択時に発火</summary>
    public event EventHandler<RecordingSession>? SessionSelected;

    public SessionListViewModel(ISessionService sessionService, PlaybackViewModel playback, ILogger<SessionListViewModel> logger)
    {
        _sessionService = sessionService;
        _playback = playback;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        IsLoading = true;
        try
        {
            var sessions = await _sessionService.GetAllSessionsAsync();
            Sessions.Clear();
            foreach (var s in sessions)
                Sessions.Add(s);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectSession(RecordingSession session)
    {
        SelectedSession = session;
        SessionSelected?.Invoke(this, session);
    }

    [RelayCommand]
    private void OpenSessionFolder(RecordingSession session)
    {
        if (!_sessionService.IsValidSessionPath(session.FolderPath))
            return;

        if (Directory.Exists(session.FolderPath))
            Process.Start(new ProcessStartInfo
            {
                FileName = session.FolderPath,
                UseShellExecute = true
            });
    }

    /// <summary>セッション名のインライン編集を開始</summary>
    [RelayCommand]
    private void BeginEditTitle(RecordingSession session)
    {
        EditingSession = session;
        EditingTitle = string.IsNullOrEmpty(session.Title)
            ? session.StartTime.ToString("yyyy/MM/dd HH:mm")
            : session.Title;
    }

    /// <summary>セッション名の編集を確定して保存</summary>
    [RelayCommand]
    private async Task CommitEditTitleAsync()
    {
        if (EditingSession == null) return;

        var trimmed = EditingTitle.Trim();
        EditingSession.Title = trimmed;
        await _sessionService.UpdateSessionAsync(EditingSession);

        // UI更新のためにリスト再読み込み
        var idx = Sessions.IndexOf(EditingSession);
        if (idx >= 0)
        {
            Sessions[idx] = EditingSession;
        }

        // 選択中セッションの場合はプロパティ更新通知
        if (SelectedSession == EditingSession)
            OnPropertyChanged(nameof(SelectedSession));

        EditingSession = null;
    }

    /// <summary>セッション名の編集をキャンセル</summary>
    [RelayCommand]
    private void CancelEditTitle()
    {
        EditingSession = null;
        EditingTitle = string.Empty;
    }

    /// <summary>セッションを削除（カスタム確認ダイアログ付き）</summary>
    [RelayCommand]
    private async Task DeleteSessionAsync(RecordingSession session)
    {
        var confirmed = ConfirmDialog.Show(
            Application.Current?.MainWindow,
            "セッションの削除",
            $"セッション「{session.DisplayName}」を削除しますか？\n\n音声データ・文字起こし・議事録がすべて削除されます。\nこの操作は取り消せません。",
            confirmText: "削除する",
            cancelText: "キャンセル",
            icon: "🗑");

        if (!confirmed) return;

        try
        {
            // 再生中のファイルハンドルを解放してからセッションを削除
            if (_playback.CurrentSession?.FolderPath == session.FolderPath)
                _playback.UnloadSession();

            await _sessionService.DeleteSessionAsync(session);
            Sessions.Remove(session);

            if (SelectedSession == session)
            {
                SelectedSession = null;
                SessionSelected?.Invoke(this, null!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "セッション削除に失敗: {SessionFolder}", session.FolderPath);
            ConfirmDialog.Show(
                Application.Current?.MainWindow,
                "エラー",
                $"削除に失敗しました:\n{ex.Message}",
                confirmText: "OK",
                cancelText: "",
                icon: "❌",
                showCopyButton: true);
        }
    }
}
