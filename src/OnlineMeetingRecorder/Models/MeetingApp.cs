namespace OnlineMeetingRecorder.Models;

/// <summary>
/// 会議アプリの種類
/// </summary>
public enum MeetingApp
{
    Zoom,
    Teams,
    GoogleMeet,
    Webex
}

/// <summary>
/// 会議アプリのプロセス検知情報
/// </summary>
public static class MeetingAppInfo
{
    /// <summary>会議アプリの表示名を返す</summary>
    public static string GetDisplayName(MeetingApp app) => app switch
    {
        MeetingApp.Zoom => "Zoom",
        MeetingApp.Teams => "Microsoft Teams",
        MeetingApp.GoogleMeet => "Google Meet",
        MeetingApp.Webex => "Webex",
        _ => app.ToString()
    };

    /// <summary>検知対象のプロセス名一覧（Google Meet はブラウザタイトルで判定するため含まない）</summary>
    public static readonly (MeetingApp App, string[] ProcessNames)[] ProcessRules =
    [
        (MeetingApp.Zoom, ["Zoom"]),
        (MeetingApp.Teams, ["ms-teams"]),
        (MeetingApp.Webex, ["CiscoCollabHost", "webexmta"]),
    ];

    /// <summary>Google Meet 検知用のブラウザプロセス名</summary>
    public static readonly string[] BrowserProcessNames = ["chrome", "msedge", "firefox"];

    /// <summary>Google Meet 検知用のウィンドウタイトルキーワード</summary>
    public static readonly string[] GoogleMeetTitleKeywords = ["Meet -", "Google Meet", "meet.google.com"];
}
