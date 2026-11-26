using System.Net;
using System.Text;
using DeliverableOrganizer.Models;
using DeliverableOrganizer.Services;

namespace DeliverableOrganizer.Tests;

public class PrLoaderTests
{
    private static readonly PrRef Pr = PrUrlParser.TryParse("https://dev.azure.com/Org/My%20Proj/_git/Repo/pullrequest/42")!;

    /// <summary>URL のパスとクエリで応答を決める偽の HTTP ハンドラー。受けた要求を記録する。</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>正常な PR の応答。変更一覧は 2 ページ、コミットは継続トークンで 2 ページに分かれる。</summary>
    private static HttpResponseMessage NormalPr(HttpRequestMessage request)
    {
        string url = request.RequestUri!.ToString();
        if (url.Contains("/changes"))
        {
            return url.Contains("$skip=0")
                ? Json("""
                    {"changeEntries":[
                      {"changeType":"edit","item":{"path":"/src/a.cs","gitObjectType":"blob"}},
                      {"changeType":"add","item":{"path":"/src","isFolder":true,"gitObjectType":"tree"}},
                      {"changeType":"edit, rename","item":{"path":"/src/new.cs"},"originalPath":"/src/old.cs"}
                    ],"nextSkip":2,"nextTop":2000}
                    """)
                : Json("""
                    {"changeEntries":[
                      {"changeType":"delete","item":{"path":"/docs/gone.md"}},
                      {"changeType":"add","item":{"path":"/docs/new.xlsx"}}
                    ],"nextSkip":0,"nextTop":0}
                    """);
        }

        if (url.Contains("/iterations"))
        {
            return Json("""
                {"value":[
                  {"id":1,"sourceRefCommit":{"commitId":"old-head"},"commonRefCommit":{"commitId":"old-base"}},
                  {"id":2,"sourceRefCommit":{"commitId":"head-sha"},"commonRefCommit":{"commitId":"base-sha"}}
                ]}
                """);
        }

        if (url.Contains("/commits"))
        {
            if (!url.Contains("continuationToken="))
            {
                var first = Json("""{"value":[{"commitId":"c3","author":{"name":"A","date":"2026-11-03T00:00:00Z"},"comment":"third"}]}""");
                first.Headers.Add("x-ms-continuationtoken", "next page");
                return first;
            }

            return Json("""
                {"value":[
                  {"commitId":"c2","author":{"name":"B","date":"2026-11-02T00:00:00Z"},"comment":"second"},
                  {"commitId":"c1","author":{"name":"A","date":"2026-11-01T00:00:00Z"},"comment":"first"}
                ]}
                """);
        }

        return Json("""{"title":"Fix bug","sourceRefName":"refs/heads/feature/x","targetRefName":"refs/heads/main","status":"active"}""");
    }

    private static (PrLoader Loader, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? pat = "secret")
    {
        var handler = new FakeHandler(respond);
        var client = new AzureDevOpsClient(new HttpClient(handler), pat, TimeSpan.Zero);
        return (new PrLoader(client), handler);
    }

    [Fact]
    public async Task PR_情報と最新イテレーションの修正前後_SHA_と変更一覧とコミットをまとめる()
    {
        var (loader, handler) = Create(NormalPr);

        PrData data = await loader.LoadAsync(Pr);

        Assert.Equal("Fix bug", data.Title);
        Assert.Equal("feature/x", data.SourceBranch);
        Assert.Equal("main", data.TargetBranch);
        Assert.Equal("active", data.Status);
        Assert.Equal("base-sha", data.BaseSha);
        Assert.Equal("head-sha", data.HeadSha);

        // フォルダは除き、2 ページ目まで取得する
        Assert.Equal(
            [
                new FileChange("/src/a.cs", null, ChangeType.Edit),
                new FileChange("/src/new.cs", "/src/old.cs", ChangeType.Rename),
                new FileChange("/docs/gone.md", null, ChangeType.Delete),
                new FileChange("/docs/new.xlsx", null, ChangeType.Add),
            ],
            data.Changes);

        // 継続トークンで 2 ページ目まで取り、古い順に並べる
        Assert.Equal(["c1", "c2", "c3"], data.Commits.Select(c => c.Sha));
        Assert.Equal("first", data.Commits[0].Message);

        // 最新イテレーション（id=2）の変更を、分岐元と比較して取る
        Assert.Contains(handler.Requests, r => r.RequestUri!.ToString().Contains("/iterations/2/changes?$compareTo=0"));

        // URL はエスケープし、api-version と Basic 認証（":" + PAT）を付ける
        Assert.All(handler.Requests, r =>
        {
            Assert.StartsWith("https://dev.azure.com/Org/My%20Proj/_apis/git/repositories/Repo/pullRequests/42", r.RequestUri!.AbsoluteUri);
            Assert.Contains("api-version=7.1", r.RequestUri.Query);
            Assert.Equal("Basic", r.Headers.Authorization?.Scheme);
            Assert.Equal(":secret", Encoding.ASCII.GetString(Convert.FromBase64String(r.Headers.Authorization!.Parameter!)));
        });
    }

    [Fact]
    public async Task 同じ_PR_は書き方の違う_URL_でも_1_回だけ取得する()
    {
        var (loader, handler) = Create(NormalPr);

        await loader.LoadAsync(Pr);
        int count = handler.Requests.Count;
        await loader.LoadAsync(PrUrlParser.TryParse("https://dev.azure.com/org/my%20proj/_git/repo/pullrequest/42?_a=files")!);

        Assert.Equal(count, handler.Requests.Count);
    }

    [Fact]
    public async Task PAT_がなければ認証ヘッダーを付けない()
    {
        var (loader, handler) = Create(NormalPr, pat: null);

        await loader.LoadAsync(Pr);

        Assert.All(handler.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation)]
    public async Task 認証エラーは再試行せず_IsAuthError_で知らせる(HttpStatusCode status)
    {
        var (loader, handler) = Create(_ => Json("<html>sign in</html>", status));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() => loader.LoadAsync(Pr));

        Assert.True(ex.IsAuthError);
        Assert.Contains("Code: Read", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task 存在しない_PR_は再試行せず_404_で知らせる()
    {
        var (loader, handler) = Create(_ => Json("{}", HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() => loader.LoadAsync(Pr));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.False(ex.IsAuthError);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task サーバーエラーは_3_回まで試し_成功すれば続ける()
    {
        int failures = 0;
        var (loader, _) = Create(r => failures++ < 2 ? Json("{}", HttpStatusCode.ServiceUnavailable) : NormalPr(r));

        PrData data = await loader.LoadAsync(Pr);

        Assert.Equal("Fix bug", data.Title);
    }

    [Fact]
    public async Task 通信エラーが_3_回続いたら諦める()
    {
        var (loader, handler) = Create(_ => throw new HttpRequestException("network down"));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() => loader.LoadAsync(Pr));

        Assert.Null(ex.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }
}
