using System.IO;
using System.Text;
using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>_PR情報.txt（C4）。PR の概要・コミット履歴（古い順）・変更一覧を書く。</summary>
public static class PrInfoWriter
{
    public const string FileName = "_PR情報.txt";

    /// <summary>UTF-8（BOM 付き）・CRLF で書く。メモ帳やサクラエディタでそのまま開けるように。</summary>
    public static Task WriteAsync(string folder, PrData pr) =>
        File.WriteAllTextAsync(Path.Combine(folder, FileName), Format(pr), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public static string Format(PrData pr)
    {
        var sb = new StringBuilder();
        sb.Append("Pull Request ").Append(pr.Ref.PullRequestId).Append("\r\n");
        sb.Append("タイトル: ").Append(pr.Title).Append("\r\n");
        sb.Append("URL: ").Append(pr.Ref.SourceUrl).Append("\r\n");
        sb.Append("ブランチ: ").Append(pr.SourceBranch).Append(" → ").Append(pr.TargetBranch).Append("\r\n");
        sb.Append("状態: ").Append(StatusText(pr.Status)).Append("\r\n");
        sb.Append("修正前: ").Append(pr.BaseSha).Append("（分岐元）\r\n");
        sb.Append("修正後: ").Append(pr.HeadSha).Append("（PR の最新）\r\n");

        sb.Append("\r\n■ コミット履歴（古い順、").Append(pr.Commits.Count).Append(" 件）\r\n");
        foreach (var c in pr.Commits)
        {
            string firstLine = c.Message.Split('\n')[0].TrimEnd('\r');
            string sha = c.Sha.Length > 8 ? c.Sha[..8] : c.Sha;
            sb.Append(c.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("  ")
              .Append(sha).Append("  ").Append(c.Author).Append("  ").Append(firstLine).Append("\r\n");
        }

        sb.Append("\r\n■ 変更ファイル（").Append(pr.Changes.Count).Append(" 件）\r\n");
        foreach (var f in pr.Changes.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            sb.Append(ChangeTypeText(f.ChangeType)).Append("  ");
            if (f.ChangeType == ChangeType.Rename && f.OriginalPath is not null)
            {
                sb.Append(f.OriginalPath).Append(" → ");
            }

            sb.Append(f.Path).Append("\r\n");
        }

        return sb.ToString();
    }

    public static string StatusText(string status) => status switch
    {
        "active" => "アクティブ",
        "completed" => "完了",
        "abandoned" => "破棄",
        _ => status,
    };

    public static string ChangeTypeText(ChangeType type) => type switch
    {
        ChangeType.Add => "追加",
        ChangeType.Delete => "削除",
        ChangeType.Rename => "名前変更",
        _ => "修正",
    };
}
