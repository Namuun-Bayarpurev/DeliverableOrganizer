namespace DeliverableOrganizer.Models;

/// <summary>PR URL の解析結果。</summary>
/// <param name="BaseUrl">例: https://dev.azure.com/Sysmac または https://xxx.visualstudio.com</param>
public sealed record PrRef(string BaseUrl, string Project, string Repository, int PullRequestId, string SourceUrl);

/// <summary>PR 内の変更ファイル 1 件。</summary>
/// <param name="Path">リポジトリ内パス（修正後）</param>
/// <param name="OriginalPath">名前変更時の旧パス</param>
public sealed record FileChange(string Path, string? OriginalPath, ChangeType ChangeType);

/// <summary>PR のコミット 1 件。</summary>
public sealed record CommitInfo(string Sha, string Author, DateTimeOffset Date, string Message);

/// <summary>PR の取得結果（C1）。</summary>
public sealed class PrData
{
    public required PrRef Ref { get; init; }
    public string Title { get; init; } = "";
    public string SourceBranch { get; init; } = "";
    public string TargetBranch { get; init; } = "";
    public string Status { get; init; } = "";

    /// <summary>修正前 SHA（merge-base / commonRefCommit）</summary>
    public string BaseSha { get; init; } = "";

    /// <summary>修正後 SHA（PR 最新コミット / sourceRefCommit）</summary>
    public string HeadSha { get; init; } = "";

    public IReadOnlyList<FileChange> Changes { get; init; } = [];
    public IReadOnlyList<CommitInfo> Commits { get; init; } = [];
}

/// <summary>出力条件。</summary>
public sealed class ExportOptions
{
    public string OutputFolder { get; init; } = "";
    public string? LocalRepository { get; init; }
    public IReadOnlyList<string> SpecPatterns { get; init; } = [];
    public IReadOnlyList<string> TestPatterns { get; init; } = [];
    public TestScope TestScope { get; init; } = TestScope.Changed;
    public bool WritePrInfo { get; init; } = true;
    public ExistingFolderMode ExistingFolder { get; init; } = ExistingFolderMode.Recreate;
    public string PrFolderFormat { get; init; } = "Pull Request {id}";
    public int MaxParallel { get; init; } = 4;
}

/// <summary>画面ログ 1 行。</summary>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string Display => Level switch
    {
        LogLevel.Warning => $"[警告] {Message}",
        LogLevel.Error => $"[エラー] {Message}",
        _ => Message,
    };
}
