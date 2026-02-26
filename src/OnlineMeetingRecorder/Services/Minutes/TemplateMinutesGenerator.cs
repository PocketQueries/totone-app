using System.Text;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Minutes;

/// <summary>
/// テンプレートベースの議事録生成。文字起こし結果をマークダウン形式に整形する。
/// </summary>
public class TemplateMinutesGenerator : IMinutesGenerator
{
    public Task<MinutesResult> GenerateAsync(RecordingSession session, List<TranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# 議事録");
        sb.AppendLine();

        // セッション情報
        sb.AppendLine("## 基本情報");
        sb.AppendLine();
        sb.AppendLine($"- **日時**: {session.StartTime:yyyy/MM/dd HH:mm} 〜 {session.EndTime:yyyy/MM/dd HH:mm}");
        sb.AppendLine($"- **所要時間**: {session.Duration:hh\\:mm\\:ss}");
        sb.AppendLine($"- **マイク**: {session.InputDeviceName}");
        sb.AppendLine($"- **スピーカー**: {session.OutputDeviceName}");
        sb.AppendLine();

        // 発言録
        sb.AppendLine("## 発言録");
        sb.AppendLine();

        if (segments.Count == 0)
        {
            sb.AppendLine("（発言なし）");
        }
        else
        {
            sb.AppendLine("| 時刻 | 話者 | 発言内容 |");
            sb.AppendLine("|------|------|----------|");

            foreach (var segment in segments.OrderBy(s => s.Start))
            {
                var time = segment.Start.TotalHours >= 1
                    ? $"{(int)segment.Start.TotalHours}:{segment.Start:mm\\:ss}"
                    : $"{segment.Start:mm\\:ss}";
                var speakerLabel = segment.Speaker == "mic" ? "自分" : "相手";
                var text = segment.Text.Replace("|", "\\|").Replace("\n", " ");
                sb.AppendLine($"| {time} | {speakerLabel} | {text} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"*生成日時: {DateTime.Now:yyyy/MM/dd HH:mm}*");

        return Task.FromResult(new MinutesResult { Text = sb.ToString() });
    }
}
