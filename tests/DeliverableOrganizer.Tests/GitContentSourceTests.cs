using System.Diagnostics;
using System.Text;
using DeliverableOrganizer.Models;
using DeliverableOrganizer.Services;

namespace DeliverableOrganizer.Tests;

/// <summary>一時フォルダに本物の git リポジトリを作って試す（git が必要）。</summary>
public sealed class GitContentSourceTests : IDisposable
{
    private static readonly PrRef Pr = PrUrlParser.TryParse("https://dev.azure.com/Org/Proj/_git/Repo/pullrequest/42")!;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeliverableOrganizerTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = [];

    public GitContentSourceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // git のオブジェクトは読み取り専用なので属性を外してから消す
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>API の代わり。呼ばれたパスを記録して "api:{path}" を返す。</summary>
    private sealed class FakeApi : IContentSource
    {
        public List<string> Calls { get; } = [];

        public Task<byte[]> GetAsync(PrRef pr, string commitSha, string path, CancellationToken ct)
        {
            lock (Calls)
            {
                Calls.Add(path);
            }

            return Task.FromResult(Encoding.UTF8.GetBytes("api:" + path));
        }
    }

    private static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(dir);
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output.Trim();
    }

    /// <summary>ファイルを 1 つコミットしたリポジトリを作り、そのコミットの SHA を返す。</summary>
    private string CreateRepo(string dir, string path, byte[] content)
    {
        Directory.CreateDirectory(dir);
        Git(dir, "init", "--quiet");
        string file = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, content);
        Git(dir, "add", "--all");
        Git(dir, "-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "core.autocrlf=false", "commit", "--quiet", "-m", "init");
        return Git(dir, "rev-parse", "HEAD");
    }

    private GitContentSource Create(string repo, FakeApi api) => new(repo, api, (_, m) => { lock (_logs) _logs.Add(m); });

    [Fact]
    public async Task コミットがあればgitから内容をそのまま取得する()
    {
        // 改行コード CRLF と日本語を含む内容が変換されずに出ること
        byte[] content = Encoding.UTF8.GetBytes("行1\r\n行2\n");
        string repo = Path.Combine(_root, "local");
        string sha = CreateRepo(repo, "src/日本語.cs", content);
        var api = new FakeApi();
        var source = Create(repo, api);

        byte[] actual = await source.GetAsync(Pr, sha, "/src/日本語.cs", CancellationToken.None);

        Assert.Equal(content, actual);
        Assert.Empty(api.Calls);
        Assert.Equal(1, source.FromGit);
    }

    [Fact]
    public async Task gitで取れないファイルはAPIで取得する()
    {
        string repo = Path.Combine(_root, "local");
        string sha = CreateRepo(repo, "a.cs", [1]);
        var api = new FakeApi();
        var source = Create(repo, api);

        byte[] actual = await source.GetAsync(Pr, sha, "/missing.cs", CancellationToken.None);

        Assert.Equal("api:/missing.cs", Encoding.UTF8.GetString(actual));
        Assert.Equal(1, source.FromApi);
    }

    [Fact]
    public async Task コミットがなくPRのリモートもなければAPIで取得する()
    {
        string repo = Path.Combine(_root, "local");
        CreateRepo(repo, "a.cs", [1]);
        var api = new FakeApi();
        var source = Create(repo, api);

        await source.GetAsync(Pr, "0123456789abcdef0123456789abcdef01234567", "/a.cs", CancellationToken.None);

        Assert.Equal(["/a.cs"], api.Calls);
        Assert.Contains(_logs, l => l.Contains("リモートがない"));
    }

    [Fact]
    public async Task コミットがなければPRのリモートからfetchして取得する()
    {
        // リモート側（フォルダ名をリポジトリ名 Repo にする）にだけコミットがある
        string upstream = Path.Combine(_root, "Repo");
        string sha = CreateRepo(upstream, "b.cs", Encoding.UTF8.GetBytes("upstream"));
        Git(upstream, "config", "uploadpack.allowAnySHA1InWant", "true");

        string repo = Path.Combine(_root, "local");
        CreateRepo(repo, "a.cs", [1]);
        Git(repo, "remote", "add", "origin", upstream.Replace('\\', '/'));
        var api = new FakeApi();
        var source = Create(repo, api);

        byte[] actual = await source.GetAsync(Pr, sha, "/b.cs", CancellationToken.None);

        Assert.Equal("upstream", Encoding.UTF8.GetString(actual));
        Assert.Empty(api.Calls);
        Assert.Contains(_logs, l => l.Contains("git fetch origin"));
    }

    [Fact]
    public async Task gitリポジトリかどうかを判定する()
    {
        string repo = Path.Combine(_root, "local");
        CreateRepo(repo, "a.cs", [1]);
        string plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);

        Assert.True(await GitContentSource.IsRepositoryAsync(repo));
        Assert.False(await GitContentSource.IsRepositoryAsync(plain));
        Assert.False(await GitContentSource.IsRepositoryAsync(Path.Combine(_root, "none")));
    }

    [Theory]
    [InlineData("https://Sysmac@dev.azure.com/Sysmac/SDD_Projects/_git/CommonFramework", "CommonFramework", true)]
    [InlineData("https://dev.azure.com/Sysmac/SDD_Projects/_git/CommonFramework/", "commonframework", true)]
    [InlineData("git@ssh.dev.azure.com:v3/Sysmac/SDD_Projects/CommonFramework", "CommonFramework", true)]
    [InlineData("https://sysmac.visualstudio.com/DefaultCollection/P/_git/My%20Repo", "My Repo", true)]
    [InlineData("https://dev.azure.com/Sysmac/SDD_Projects/_git/CommonFramework2", "CommonFramework", false)]
    [InlineData("https://dev.azure.com/Sysmac/SDD_Projects/_git/OtherRepo", "CommonFramework", false)]
    public void リモートURLがPRのリポジトリを指すか判定する(string url, string repository, bool expected) =>
        Assert.Equal(expected, GitContentSource.RemoteMatches(url, repository));
}
