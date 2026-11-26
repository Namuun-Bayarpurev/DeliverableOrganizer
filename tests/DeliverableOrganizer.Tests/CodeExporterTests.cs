using System.Net;
using System.Text;
using DeliverableOrganizer.Models;
using DeliverableOrganizer.Services;

namespace DeliverableOrganizer.Tests;

public sealed class CodeExporterTests : IDisposable
{
    private static readonly PrRef Pr = PrUrlParser.TryParse("https://dev.azure.com/Org/Proj/_git/Repo/pullrequest/42")!;

    private readonly string _out = Path.Combine(Path.GetTempPath(), "DeliverableOrganizerTests", Guid.NewGuid().ToString("N"));
    private readonly List<(LogLevel Level, string Message)> _logs = [];

    public void Dispose()
    {
        if (Directory.Exists(_out))
        {
            Directory.Delete(_out, recursive: true);
        }
    }

    /// <summary>"{sha}:{path}" の内容を返す偽の取得元。missing に入れたものは 404 にする。</summary>
    private sealed class FakeSource(params string[] missing) : IContentSource
    {
        public Task<byte[]> GetAsync(PrRef pr, string commitSha, string path, CancellationToken ct) =>
            missing.Contains(path)
                ? throw new AzureDevOpsException("ファイルが見つかりません（404）", HttpStatusCode.NotFound)
                : Task.FromResult(Encoding.UTF8.GetBytes($"{commitSha}:{path}"));
    }

    private CodeExporter Create(IContentSource source) => new(source, (l, m) => { lock (_logs) _logs.Add((l, m)); });

    private static PrData Data(params FileChange[] changes) => new()
    {
        Ref = Pr,
        Title = "タイトル",
        BaseSha = "base",
        HeadSha = "head",
        Changes = changes,
        Commits = [new CommitInfo("abcdef1234567890", "山田", new DateTimeOffset(2026, 11, 20, 1, 2, 0, TimeSpan.Zero), "最初のコミット\n詳細")],
    };

    private ExportOptions Options(ExistingFolderMode mode = ExistingFolderMode.Recreate, bool prInfo = true) =>
        new() { OutputFolder = _out, ExistingFolder = mode, WritePrInfo = prInfo };

    private string PrFolder => Path.Combine(_out, "コード", "Pull Request 42");

    private string Read(string side, string relative) => File.ReadAllText(Path.Combine(PrFolder, side, relative));

    private bool Exists(string side, string relative) => File.Exists(Path.Combine(PrFolder, side, relative));

    [Fact]
    public async Task 変更種別ごとにbeforeとafterへ振り分ける()
    {
        var data = Data(
            new FileChange("/src/edit.cs", null, ChangeType.Edit),
            new FileChange("/src/add.cs", null, ChangeType.Add),
            new FileChange("/src/del.cs", null, ChangeType.Delete),
            new FileChange("/src/new.cs", "/old/old.cs", ChangeType.Rename));

        var result = await Create(new FakeSource()).ExportAsync(data, Options(), CancellationToken.None);

        Assert.Equal(6, result.Files); // 修正 2 + 追加 1 + 削除 1 + 名前変更 2
        Assert.Equal(0, result.Warnings);
        Assert.Equal("base:/src/edit.cs", Read("before", @"src\edit.cs"));
        Assert.Equal("head:/src/edit.cs", Read("after", @"src\edit.cs"));
        Assert.Equal("head:/src/add.cs", Read("after", @"src\add.cs"));
        Assert.False(Exists("before", @"src\add.cs"));
        Assert.Equal("base:/src/del.cs", Read("before", @"src\del.cs"));
        Assert.False(Exists("after", @"src\del.cs"));
        Assert.Equal("base:/old/old.cs", Read("before", @"old\old.cs"));
        Assert.Equal("head:/src/new.cs", Read("after", @"src\new.cs"));
        Assert.True(File.Exists(Path.Combine(PrFolder, "_PR情報.txt")));
    }

    [Fact]
    public async Task 取得できないファイルは警告にして残りを出力する()
    {
        var data = Data(new FileChange("/a.cs", null, ChangeType.Add), new FileChange("/b.cs", null, ChangeType.Add));

        var result = await Create(new FakeSource("/a.cs")).ExportAsync(data, Options(), CancellationToken.None);

        Assert.Equal(1, result.Files);
        Assert.Equal(1, result.Warnings);
        Assert.True(Exists("after", "b.cs"));
        Assert.Contains(_logs, l => l.Level == LogLevel.Warning && l.Message.Contains("/a.cs"));
    }

    [Fact]
    public async Task 削除して再作成なら前回のファイルは残らない()
    {
        string stale = Path.Combine(PrFolder, "after", "stale.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");
        File.SetAttributes(stale, FileAttributes.ReadOnly);

        await Create(new FakeSource()).ExportAsync(Data(new FileChange("/a.cs", null, ChangeType.Add)), Options(), CancellationToken.None);

        Assert.False(File.Exists(stale));
        Assert.True(Exists("after", "a.cs"));
    }

    [Fact]
    public async Task 上書きなら前回のファイルを残して書き込む()
    {
        string stale = Path.Combine(PrFolder, "after", "stale.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");

        await Create(new FakeSource()).ExportAsync(Data(new FileChange("/a.cs", null, ChangeType.Add)), Options(ExistingFolderMode.Overwrite), CancellationToken.None);

        Assert.True(File.Exists(stale));
        Assert.True(Exists("after", "a.cs"));
    }

    [Fact]
    public async Task スキップならフォルダがあれば何もしない()
    {
        Directory.CreateDirectory(PrFolder);

        var result = await Create(new FakeSource()).ExportAsync(Data(new FileChange("/a.cs", null, ChangeType.Add)), Options(ExistingFolderMode.Skip), CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.False(Exists("after", "a.cs"));
    }

    [Fact]
    public async Task PR情報をオフにすると書かない()
    {
        await Create(new FakeSource()).ExportAsync(Data(new FileChange("/a.cs", null, ChangeType.Add)), Options(prInfo: false), CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(PrFolder, "_PR情報.txt")));
    }

    [Fact]
    public async Task 認証エラーは全体を止めるため投げ直す()
    {
        var source = new ThrowingSource(new AzureDevOpsException("認証", HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            Create(source).ExportAsync(Data(new FileChange("/a.cs", null, ChangeType.Add)), Options(), CancellationToken.None));
    }

    private sealed class ThrowingSource(Exception ex) : IContentSource
    {
        public Task<byte[]> GetAsync(PrRef pr, string commitSha, string path, CancellationToken ct) => throw ex;
    }

    [Theory]
    [InlineData("/src/a.cs", @"C:\out\src\a.cs")]
    [InlineData("/日本語/テスト 仕様書.xlsx", @"C:\out\日本語\テスト 仕様書.xlsx")]
    public void リポジトリ内パスをフォルダ構成のまま変換する(string repoPath, string expected) =>
        Assert.Equal(expected, CodeExporter.ToLocalPath(@"C:\out", repoPath));

    [Theory]
    [InlineData("/../evil.cs")]
    [InlineData("/src/./a.cs")]
    [InlineData("/src/a:b.cs")]
    [InlineData("/")]
    public void 出力先の外に出るパスや使えない名前は受け付けない(string repoPath) =>
        Assert.Throws<ArgumentException>(() => CodeExporter.ToLocalPath(@"C:\out", repoPath));

    [Fact]
    public void PR情報にコミット履歴と変更一覧を書く()
    {
        string text = PrInfoWriter.Format(Data(
            new FileChange("/b.cs", null, ChangeType.Edit),
            new FileChange("/a.cs", "/old.cs", ChangeType.Rename)));

        Assert.Contains("タイトル: タイトル\r\n", text);
        Assert.Contains("abcdef12  山田  最初のコミット\r\n", text);
        Assert.DoesNotContain("詳細", text);
        Assert.Contains("■ 変更ファイル（2 件）\r\n名前変更  /old.cs → /a.cs\r\n修正  /b.cs\r\n", text);
    }

    [Fact]
    public async Task ファイル取得はItemsAPIをコミット指定で呼ぶ()
    {
        HttpRequestMessage? sent = null;
        var http = new HttpClient(new Handler(r =>
        {
            sent = r;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }));
        var client = new AzureDevOpsClient(http, "pat", TimeSpan.Zero);

        byte[] content = await client.GetItemContentAsync(Pr, "/src/日本.cs", "abc123", CancellationToken.None);

        Assert.Equal([1, 2, 3], content);
        string url = sent!.RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://dev.azure.com/Org/Proj/_apis/git/repositories/Repo/items?", url);
        Assert.Contains("path=%2Fsrc%2F%E6%97%A5%E6%9C%AC.cs", url);
        Assert.Contains("versionDescriptor.version=abc123&versionDescriptor.versionType=commit", url);
        Assert.Contains("$format=octetStream", url);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
