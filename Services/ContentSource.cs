using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>コミット時点のファイル内容を取り出す（C3）。</summary>
public interface IContentSource
{
    /// <summary>ファイル内容をバイト列のまま返す。</summary>
    /// <param name="path">リポジトリ内パス（例: /src/a.cs）</param>
    /// <exception cref="AzureDevOpsException">取得に失敗した。</exception>
    Task<byte[]> GetAsync(PrRef pr, string commitSha, string path, CancellationToken ct);
}

/// <summary>Azure DevOps の Items API から取得する。</summary>
/// <remarks>shortcut: 設計書 C3 の GitContentSource（ローカルリポジトリから git cat-file で取得し、失敗したら API）は未対応。件数の多い PR で遅ければ追加する</remarks>
public sealed class ApiContentSource(AzureDevOpsClient client) : IContentSource
{
    public Task<byte[]> GetAsync(PrRef pr, string commitSha, string path, CancellationToken ct) =>
        client.GetItemContentAsync(pr, path, commitSha, ct);
}
