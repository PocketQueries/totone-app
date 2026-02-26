using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.MeetingDetection;

/// <summary>
/// 会議アプリ検知サービスのインターフェース
/// </summary>
public interface IMeetingDetectionService : IDisposable
{
    /// <summary>監視を開始する</summary>
    void StartMonitoring();

    /// <summary>監視を停止する</summary>
    void StopMonitoring();

    /// <summary>録音状態を通知する（録音中は新規検知をスキップする）</summary>
    void SetRecordingActive(bool isActive);

    /// <summary>会議アプリを検知した時に発火する</summary>
    event EventHandler<MeetingApp>? MeetingDetected;

    /// <summary>検知済みの会議アプリが終了した時に発火する</summary>
    event EventHandler<MeetingApp>? MeetingEnded;
}

/// <summary>
/// プロセス監視ベースの会議検知サービス。
/// DispatcherTimer で 5 秒間隔にポーリングし、会議アプリのプロセスを検知する。
/// </summary>
public class ProcessMeetingDetectionService : IMeetingDetectionService
{
    private readonly DispatcherTimer _pollTimer;
    private readonly HashSet<MeetingApp> _detectedApps = [];
    private readonly HashSet<MeetingApp> _recordingApps = [];
    private readonly object _stateLock = new();
    private bool _isRecordingActive;
    private bool _isDisposed;

    public event EventHandler<MeetingApp>? MeetingDetected;
    public event EventHandler<MeetingApp>? MeetingEnded;

    // Win32 API for window enumeration
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpWindowName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public ProcessMeetingDetectionService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _pollTimer.Tick += (_, _) => PollProcesses();
    }

    public void StartMonitoring()
    {
        if (_isDisposed) return;
        _pollTimer.Start();
    }

    public void StopMonitoring()
    {
        _pollTimer.Stop();
        lock (_stateLock)
        {
            _detectedApps.Clear();
        }
    }

    public void SetRecordingActive(bool isActive)
    {
        lock (_stateLock)
        {
            _isRecordingActive = isActive;
            if (isActive)
            {
                // 録音開始時に検知済みアプリをスナップショット
                _recordingApps.Clear();
                foreach (var app in _detectedApps)
                    _recordingApps.Add(app);
            }
            else
            {
                _recordingApps.Clear();
            }
        }
    }

    private void PollProcesses()
    {
        lock (_stateLock)
        {
            if (_isRecordingActive)
            {
                // 録音中は録音開始時に検知済みだったアプリの終了のみ検知する
                // 新規アプリの開始チェックはスキップ
                PollRecordingApps();
            }
            else
            {
                PollAllApps();
            }
        }
    }

    /// <summary>
    /// 録音中: 録音開始時に検知済みだったアプリのみプロセスチェックし、終了を検知する。
    /// </summary>
    private void PollRecordingApps()
    {
        var endedApps = new List<MeetingApp>();
        foreach (var app in _recordingApps)
        {
            bool running;
            if (app == MeetingApp.GoogleMeet)
            {
                running = IsGoogleMeetRunning();
            }
            else
            {
                var processNames = MeetingAppInfo.ProcessRules
                    .Where(r => r.App == app)
                    .SelectMany(r => r.ProcessNames)
                    .ToArray();
                running = processNames.Length > 0 && IsProcessRunning(processNames);
            }

            if (!running)
            {
                endedApps.Add(app);
            }
        }

        foreach (var app in endedApps)
        {
            _recordingApps.Remove(app);
            _detectedApps.Remove(app);
            MeetingEnded?.Invoke(this, app);
        }
    }

    /// <summary>
    /// 通常時: 全アプリのプロセスをチェックし、開始・終了を検知する。
    /// </summary>
    private void PollAllApps()
    {
        var currentlyRunning = new HashSet<MeetingApp>();

        // プロセス名ベースの検知（Zoom, Teams, Webex）
        foreach (var (app, processNames) in MeetingAppInfo.ProcessRules)
        {
            if (IsProcessRunning(processNames))
            {
                currentlyRunning.Add(app);
            }
        }

        // Google Meet: ブラウザのウィンドウタイトルで検知
        if (IsGoogleMeetRunning())
        {
            currentlyRunning.Add(MeetingApp.GoogleMeet);
        }

        // 新しく検知されたアプリ → MeetingDetected イベント発火
        foreach (var app in currentlyRunning)
        {
            if (_detectedApps.Add(app))
            {
                MeetingDetected?.Invoke(this, app);
            }
        }

        // 消えたアプリ → MeetingEnded イベント発火
        var endedApps = _detectedApps.Where(a => !currentlyRunning.Contains(a)).ToList();
        foreach (var app in endedApps)
        {
            _detectedApps.Remove(app);
            MeetingEnded?.Invoke(this, app);
        }
    }

    /// <summary>指定プロセス名のいずれかが実行中かどうか</summary>
    private static bool IsProcessRunning(string[] processNames)
    {
        foreach (var name in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            var found = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (found) return true;
        }
        return false;
    }

    /// <summary>
    /// ブラウザのウィンドウタイトルから Google Meet を検知する。
    /// Win32 EnumWindows API で全トップレベルウィンドウを走査し、
    /// ブラウザプロセスのウィンドウタイトルにキーワードが含まれるかチェックする。
    /// Process.MainWindowTitle ではアクティブタブしか取得できないため、
    /// EnumWindows で複数ウィンドウを確実に走査する。
    /// </summary>
    private static bool IsGoogleMeetRunning()
    {
        // ブラウザのプロセスIDを収集
        var browserPids = new HashSet<uint>();
        foreach (var browserName in MeetingAppInfo.BrowserProcessNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(browserName); }
            catch { continue; }

            foreach (var proc in processes)
            {
                try { browserPids.Add((uint)proc.Id); }
                catch { }
                finally { proc.Dispose(); }
            }
        }

        if (browserPids.Count == 0) return false;

        // 全ウィンドウを走査してブラウザのウィンドウタイトルをチェック
        bool found = false;
        EnumWindows((hWnd, _) =>
        {
            if (found) return false; // 既に見つかった場合は走査中止

            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (!browserPids.Contains(pid)) return true;

            int length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            foreach (var keyword in MeetingAppInfo.GoogleMeetTitleKeywords)
            {
                if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    return false; // 走査中止
                }
            }

            return true; // 次のウィンドウへ
        }, IntPtr.Zero);

        return found;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _pollTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
