using System.Collections.Concurrent;
using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>
/// PR・修正前後 SHA・変更一覧・コミット履歴を <see cref="PrData"/> にまとめる（C1）。
/// 同じ PR は 1 回だけ取得するので、1 回の操作（確認・実行）ごとに作り直す。
/// </summary>
public sealed class PrLoader(AzureDevOpsClient client)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<PrData>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>PR を取得する。同じ PR（URL の書き方が違っても）は前回の結果を返す。</summary>
    /// <exception cref="AzureDevOpsException">取得に失敗した。<see cref="AzureDevOpsException.IsAuthError"/> なら処理全体を止めること。</exception>
    public Task<PrData> LoadAsync(PrRef pr, CancellationToken ct = default) =>
        _cache.GetOrAdd(
            $"{pr.BaseUrl}/{pr.Project}/{pr.Repository}/{pr.PullRequestId}",
            _ => new Lazy<Task<PrData>>(() => LoadCoreAsync(pr, ct))).Value;

    private async Task<PrData> LoadCoreAsync(PrRef pr, CancellationToken ct)
    {
        var info = await client.GetPullRequestAsync(pr, ct);

        // 最新イテレーションの commonRefCommit が分岐元（修正前）、sourceRefCommit が PR の最新（修正後）
        var iteration = (await client.GetIterationsAsync(pr, ct)).MaxBy(i => i.Id)
            ?? throw new AzureDevOpsException($"PR {pr.PullRequestId} にイテレーションがありません。");
        var changes = await client.GetIterationChangesAsync(pr, iteration.Id, ct);
        var commits = await client.GetCommitsAsync(pr, ct);

        return new PrData
        {
            Ref = pr,
            Title = info.Title ?? "",
            SourceBranch = ShortBranchName(info.SourceRefName),
            TargetBranch = ShortBranchName(info.TargetRefName),
            Status = info.Status ?? "",
            BaseSha = iteration.CommonRefCommit?.CommitId ?? "",
            HeadSha = iteration.SourceRefCommit?.CommitId ?? "",
            Changes = changes,

            // _PR情報.txt は古い順に書くため、ここで古い順にしておく（API は新しい順）
            Commits = commits.Reverse().ToList(),
        };
    }

    private static string ShortBranchName(string? refName) =>
        refName is not null && refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..] : refName ?? "";
}
