using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>Azure DevOps REST API の呼び出しに失敗した。</summary>
public sealed class AzureDevOpsException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>HTTP ステータス（通信エラーなど応答がない場合は <see langword="null"/>）。</summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>認証エラーか。PAT が無効だと 401／403 のほか、サインイン画面付きの 203 が返る。</summary>
    public bool IsAuthError => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NonAuthoritativeInformation;
}

/// <summary>Azure DevOps REST API（api-version 7.1、Basic 認証）の呼び出し。通信エラー・5xx は 3 回まで試す。</summary>
public sealed class AzureDevOpsClient
{
    private const string ApiVersion = "7.1";
    private const int MaxAttempts = 3;
    private const int ChangesPageSize = 2000;
    private const int CommitsPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly TimeSpan _retryDelay;

    /// <param name="http">共有する HttpClient。</param>
    /// <param name="pat">PAT。<see langword="null"/> なら認証ヘッダーを付けない。</param>
    /// <param name="retryDelay">再試行の待ち時間（n 回目は n 倍）。既定 1 秒。</param>
    public AzureDevOpsClient(HttpClient http, string? pat, TimeSpan? retryDelay = null)
    {
        _http = http;
        _authorization = pat is null ? null : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
    }

    internal Task<PullRequestDto> GetPullRequestAsync(PrRef pr, CancellationToken ct) =>
        GetJsonAsync<PullRequestDto>(PrUrl(pr, ""), ct);

    internal async Task<IReadOnlyList<IterationDto>> GetIterationsAsync(PrRef pr, CancellationToken ct) =>
        (await GetJsonAsync<ListDto<IterationDto>>(PrUrl(pr, "/iterations"), ct)).Value ?? [];

    /// <summary>イテレーションの変更ファイル（PR の分岐元との比較。フォルダは除く）。</summary>
    internal async Task<IReadOnlyList<FileChange>> GetIterationChangesAsync(PrRef pr, int iterationId, CancellationToken ct)
    {
        var changes = new List<FileChange>();
        int skip = 0;
        int top = ChangesPageSize;
        while (top > 0)
        {
            var page = await GetJsonAsync<ChangesDto>(PrUrl(pr, $"/iterations/{iterationId}/changes", $"$compareTo=0&$top={top}&$skip={skip}"), ct);
            foreach (var entry in page.ChangeEntries ?? [])
            {
                if (entry.Item is { Path: { } path } item && !item.IsFolder && item.GitObjectType != "tree")
                {
                    var type = ToChangeType(entry.ChangeType);
                    changes.Add(new FileChange(path, type == ChangeType.Rename ? entry.OriginalPath ?? entry.SourceServerItem : null, type));
                }
            }

            // 続きがあれば nextSkip / nextTop が返る
            (skip, top) = (page.NextSkip, page.NextTop);
        }

        return changes;
    }

    /// <summary>PR のコミット（API の並びのまま。新しい順）。</summary>
    internal async Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(PrRef pr, CancellationToken ct)
    {
        var commits = new List<CommitInfo>();
        string? token = null;
        do
        {
            string query = $"$top={CommitsPageSize}" + (token is null ? "" : "&continuationToken=" + Uri.EscapeDataString(token));
            using var response = await SendAsync(PrUrl(pr, "/commits", query), ct);
            var page = await ReadJsonAsync<ListDto<CommitDto>>(response, ct);
            commits.AddRange((page.Value ?? []).Select(c => new CommitInfo(c.CommitId, c.Author?.Name ?? "", c.Author?.Date ?? default, c.Comment ?? "")));

            // 続きがあれば継続トークンがヘッダーで返る
            token = response.Headers.TryGetValues("x-ms-continuationtoken", out var values) ? values.FirstOrDefault() : null;
        }
        while (!string.IsNullOrEmpty(token));

        return commits;
    }

    /// <summary>コミット時点のファイル内容（バイト列のまま。文字コード・改行は変換しない）。</summary>
    /// <param name="path">リポジトリ内パス（例: /src/a.cs）</param>
    /// <param name="commitSha">取得するコミットの SHA</param>
    internal async Task<byte[]> GetItemContentAsync(PrRef pr, string path, string commitSha, CancellationToken ct)
    {
        string url = $"{RepoUrl(pr)}/items?path={Uri.EscapeDataString(path)}"
            + $"&versionDescriptor.version={Uri.EscapeDataString(commitSha)}&versionDescriptor.versionType=commit"
            + $"&$format=octetStream&download=true&api-version={ApiVersion}";
        using var response = await SendAsync(url, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static ChangeType ToChangeType(string? changeType)
    {
        // "edit, rename" のように組み合わせで返ることがある
        string value = changeType ?? "";
        return value.Contains("rename", StringComparison.OrdinalIgnoreCase) ? ChangeType.Rename
            : value.Contains("delete", StringComparison.OrdinalIgnoreCase) ? ChangeType.Delete
            : value.Contains("add", StringComparison.OrdinalIgnoreCase) ? ChangeType.Add
            : ChangeType.Edit;
    }

    private static string RepoUrl(PrRef pr) =>
        $"{pr.BaseUrl}/{Uri.EscapeDataString(pr.Project)}/_apis/git/repositories/{Uri.EscapeDataString(pr.Repository)}";

    private static string PrUrl(PrRef pr, string path, string query = "") =>
        $"{RepoUrl(pr)}/pullRequests/{pr.PullRequestId}{path}?{(query.Length == 0 ? "" : query + "&")}api-version={ApiVersion}";

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await SendAsync(url, ct);
        return await ReadJsonAsync<T>(response, ct);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct)
            ?? throw new AzureDevOpsException("応答が空です。");
    }

    /// <summary>GET を送り、成功（200）の応答を返す。通信エラー・タイムアウト・5xx・429 は再試行する。</summary>
    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = _authorization;
                response = await _http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                if (attempt < MaxAttempts)
                {
                    await Task.Delay(_retryDelay * attempt, ct);
                    continue;
                }

                throw new AzureDevOpsException($"通信に失敗しました（{MaxAttempts} 回試行）: {ex.Message}", null, ex);
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response;
            }

            var status = response.StatusCode;
            response.Dispose();
            if (attempt < MaxAttempts && ((int)status >= 500 || status == HttpStatusCode.TooManyRequests))
            {
                await Task.Delay(_retryDelay * attempt, ct);
                continue;
            }

            string where = url[..url.IndexOf('?')];
            string message = status switch
            {
                HttpStatusCode.NotFound when where.EndsWith("/items", StringComparison.Ordinal) => "ファイルが見つかりません（404）",
                HttpStatusCode.NotFound => $"PR が見つかりません（404）: {where}",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NonAuthoritativeInformation
                    => $"認証に失敗しました（{(int)status}）。PAT の権限（Code: Read）を確認してください。",
                _ => $"Azure DevOps がエラーを返しました（{(int)status} {status}）: {where}",
            };
            throw new AzureDevOpsException(message, status);
        }
    }
}

// ---- REST API の応答（必要な項目だけ） ----

internal sealed record PullRequestDto(string? Title, string? SourceRefName, string? TargetRefName, string? Status);

internal sealed record IterationDto(int Id, CommitRefDto? SourceRefCommit, CommitRefDto? CommonRefCommit);

internal sealed record CommitRefDto(string? CommitId);

internal sealed record ListDto<T>(List<T>? Value);

internal sealed record ChangesDto(List<ChangeEntryDto>? ChangeEntries, int NextSkip, int NextTop);

internal sealed record ChangeEntryDto(ItemDto? Item, string? ChangeType, string? OriginalPath, string? SourceServerItem);

internal sealed record ItemDto(string? Path, bool IsFolder, string? GitObjectType);

internal sealed record CommitDto(string CommitId, AuthorDto? Author, string? Comment);

internal sealed record AuthorDto(string? Name, DateTimeOffset Date);
