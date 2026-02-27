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

    /// <summary>
    /// 検知対象のプロセス名一覧（Google Meet はブラウザタイトルで判定するため含まない）。
    /// TitleKeywords が指定されている場合、プロセス存在に加えてウィンドウタイトル一致も要求する。
    /// null の場合はプロセス存在のみで検知する。
    /// </summary>
    public static readonly (MeetingApp App, string[] ProcessNames, string[]? TitleKeywords)[] ProcessRules =
    [
        (MeetingApp.Zoom, ["Zoom"], ["Zoom Meeting", "Zoom Webinar", "Zoom ミーティング", "Zoom ウェビナー"]),
        (MeetingApp.Teams, ["ms-teams"], null),  // Teams はタイトルだけでは会議中の区別が困難
        (MeetingApp.Webex, ["CiscoCollabHost", "webexmta"], null),  // 会議専用プロセスのため不要
    ];

    /// <summary>Google Meet 検知用のブラウザプロセス名</summary>
    public static readonly string[] BrowserProcessNames = ["chrome", "msedge", "firefox"];

    /// <summary>Google Meet 検知用のウィンドウタイトルキーワード</summary>
    public static readonly string[] GoogleMeetTitleKeywords = ["Meet -", "Google Meet", "meet.google.com"];
}
