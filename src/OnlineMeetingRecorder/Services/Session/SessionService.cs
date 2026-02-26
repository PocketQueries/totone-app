using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Settings;

namespace OnlineMeetingRecorder.Services.Session;

public class SessionService : ISessionService
{
    private readonly ISettingsService _settingsService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SessionService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>設定またはデフォルトのセッション保存ルートを返す。不正なパスはデフォルトにフォールバック。</summary>
    public string GetSessionsRoot()
    {
        var customPath = _settingsService.Settings.SessionStoragePath;
        if (!string.IsNullOrWhiteSpace(customPath) && IsValidStoragePath(customPath))
            return Path.GetFullPath(customPath);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OnlineMeetingRecorder", "Sessions");
    }

    /// <summary>
    /// セッションパスがセッションルート配下かを検証する。
    /// パストラバーサル攻撃を防止する。
    /// </summary>
    public bool IsValidSessionPath(string folderPath)
    {
        try
        {
            var sessionsRoot = Path.GetFullPath(GetSessionsRoot());
            var normalizedPath = Path.GetFullPath(folderPath);
            return normalizedPath.StartsWith(sessionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Equals(sessionsRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ストレージパスが安全なローカル絶対パスかを検証する。
    /// UNCパスを拒否する。
    /// </summary>
    private static bool IsValidStoragePath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);

            // UNCパスを拒否
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            // ルート付きの絶対パスであることを確認
            if (!Path.IsPathRooted(fullPath))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RecordingSession> CreateSessionAsync(string sessionFolder, string inputDeviceName, string outputDeviceName)
    {
        var now = DateTime.Now;
        var session = new RecordingSession
        {
            FolderPath = sessionFolder,
            StartTime = now,
            Title = $"会議 {now:MM/dd HH:mm}",
            InputDeviceName = inputDeviceName,
            OutputDeviceName = outputDeviceName,
            Status = SessionStatus.Recording
        };

        await SaveJsonAsync(session);
        return session;
    }

    public async Task CompleteSessionAsync(RecordingSession session)
    {
        session.EndTime = DateTime.Now;
        session.Status = SessionStatus.Completed;
        await SaveJsonAsync(session);
    }

    public async Task UpdateSessionAsync(RecordingSession session)
    {
        await SaveJsonAsync(session);
    }

    public async Task<RecordingSession?> LoadSessionAsync(string sessionFolder)
    {
        var path = Path.Combine(sessionFolder, "session.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        var session = JsonSerializer.Deserialize<RecordingSession>(json, JsonOptions);

        // デシリアライズ後、FolderPathを実際のディレクトリパスで上書き
        // （session.json内のFolderPathが改竄されていても安全）
        if (session != null)
            session.FolderPath = Path.GetFullPath(sessionFolder);

        return session;
    }

    public async Task<List<RecordingSession>> GetAllSessionsAsync()
    {
        var sessionsRoot = GetSessionsRoot();

        if (!Directory.Exists(sessionsRoot))
            return new List<RecordingSession>();

        var dirs = Directory.GetDirectories(sessionsRoot);
        var tasks = dirs.Select(dir => LoadSessionAsync(dir));
        var results = await Task.WhenAll(tasks);
        return results.Where(s => s != null).OrderByDescending(s => s!.StartTime).ToList()!;
    }

    public Task DeleteSessionAsync(RecordingSession session)
    {
        // パストラバーサル防止: セッションルート配下であることを検証
        if (!IsValidSessionPath(session.FolderPath))
            throw new InvalidOperationException(
                $"セッションパスがセッションルート外のため削除できません: {session.FolderPath}");

        if (Directory.Exists(session.FolderPath))
            Directory.Delete(session.FolderPath, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task SaveJsonAsync(RecordingSession session)
    {
        var path = Path.Combine(session.FolderPath, "session.json");
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }
}
