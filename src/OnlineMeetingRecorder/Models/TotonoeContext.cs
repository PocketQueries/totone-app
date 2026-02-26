namespace OnlineMeetingRecorder.Models;

/// <summary>
/// 「ととのえ」機能で議事録を再生成する際の追加コンテキスト情報
/// </summary>
public class TotonoeContext
{
    /// <summary>お客様の会社名</summary>
    public string CustomerCompany { get; set; } = string.Empty;

    /// <summary>お客様の参加者名（カンマ区切り）</summary>
    public string CustomerParticipants { get; set; } = string.Empty;

    /// <summary>自社の参加者名（カンマ区切り）</summary>
    public string OurParticipants { get; set; } = string.Empty;

    /// <summary>会議の内容・目的</summary>
    public string MeetingPurpose { get; set; } = string.Empty;

    /// <summary>専門用語やドメイン特有の知識</summary>
    public string DomainKnowledge { get; set; } = string.Empty;

    /// <summary>いずれかのフィールドに値が入っているか</summary>
    public bool HasAnyContent() =>
        !string.IsNullOrWhiteSpace(CustomerCompany) ||
        !string.IsNullOrWhiteSpace(CustomerParticipants) ||
        !string.IsNullOrWhiteSpace(OurParticipants) ||
        !string.IsNullOrWhiteSpace(MeetingPurpose) ||
        !string.IsNullOrWhiteSpace(DomainKnowledge);
}
