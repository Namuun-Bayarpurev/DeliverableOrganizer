using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>
/// ローカルリポジトリから <c>git cat-file blob {SHA}:{path}</c> で取得する（C3）。
/// コミットがなければ PR のリポジトリを指すリモートから <c>git fetch</c> し、それでも取れなければ <paramref name="fallback"/>（API）で取得する。
/// </summary>
/// <remarks>
/// SHA 指定で取り出すので、別のリポジトリを指定されても内容を取り違えることはない（コミットが見つからず API に切り替わるだけ）。
/// 作業ツリー・現在のブランチ・HEAD は変更しない（fetch はリモート追跡の情報とオブジェクトを増やすだけ）。
/// </remarks>
/// <param name="log">ログ出力。複数スレッドから呼ばれる。</param>
public sealed class GitContentSource(string repository, IContentSource fallback, Action<LogLevel, string> log) : IContentSource
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(3);

    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _commits = new(StringComparer.OrdinalIgnoreCase);
    private int _fromGit;
    private int _fromApi;
    private int _gitUnavailable;

    /// <summary>git から取得したファイル数。</summary>
    public int FromGit => _fromGit;

    /// <summary>API に切り替えて取得したファイル数。</summary>
    public int FromApi => _fromApi;

    /// <summary>git リポジトリ（作業ツリーまたは bare）か。git が見つからない場合も false。</summary>
    public static async Task<bool> IsRepositoryAsync(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return false;
        }

        try
        {
            var result = await RunGitAsync(folder, ["rev-parse", "--git-dir"], TimeSpan.FromSeconds(30), CancellationToken.None);
            return result.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public async Task<byte[]> GetAsync(PrRef pr, string commitSha, string path, CancellationToken ct)
    {
        if (Volatile.Read(ref _gitUnavailable) == 0)
        {
            try
            {
                if (await EnsureCommitAsync(pr, commitSha, ct))
                {
                    var result = await RunGitAsync(repository, ["cat-file", "blob", $"{commitSha}:{path.TrimStart('/')}"], TimeSpan.FromMinutes(1), ct);
                    if (result.ExitCode == 0)
                    {
                        Interlocked.Increment(ref _fromGit);
                        return result.Output;
                    }
                }
            }
            catch (Win32Exception ex)
            {
                // git が見つからない。以降は API だけを使う
                if (Interlocked.Exchange(ref _gitUnavailable, 1) == 0)
                {
                    log(LogLevel.Info, $"git を実行できないため API で取得します: {ex.Message}");
                }
            }
        }

        var content = await fallback.GetAsync(pr, commitSha, path, ct);
        Interlocked.Increment(ref _fromApi);
        return content;
    }

    /// <summary>コミットがローカルにあるか確かめ、なければ fetch する。SHA ごとに 1 回だけ行う。</summary>
    private Task<bool> EnsureCommitAsync(PrRef pr, string sha, CancellationToken ct) =>
        _commits.GetOrAdd(sha, _ => new Lazy<Task<bool>>(() => EnsureCommitCoreAsync(pr, sha, ct))).Value;

    private async Task<bool> EnsureCommitCoreAsync(PrRef pr, string sha, CancellationToken ct)
    {
        if (await HasCommitAsync(sha, ct))
        {
            return true;
        }

        string? remote = await FindRemoteAsync(pr, ct);
        if (remote is null)
        {
            log(LogLevel.Info, $"PR {pr.PullRequestId}: ローカルリポジトリに {pr.Repository} を指すリモートがないため、コミット {Short(sha)} を API で取得します");
            return false;
        }

        log(LogLevel.Info, $"PR {pr.PullRequestId}: コミット {Short(sha)} がローカルにないため git fetch {remote} しています...");
        var fetch = await RunGitAsync(repository, ["fetch", "--no-tags", "--quiet", remote, sha], FetchTimeout, ct);
        if (fetch.ExitCode == 0 && await HasCommitAsync(sha, ct))
        {
            return true;
        }

        log(LogLevel.Info, $"PR {pr.PullRequestId}: コミット {Short(sha)} を fetch できなかったため API で取得します: {fetch.Error.Trim()}");
        return false;
    }

    private async Task<bool> HasCommitAsync(string sha, CancellationToken ct) =>
        (await RunGitAsync(repository, ["cat-file", "-e", $"{sha}^{{commit}}"], TimeSpan.FromSeconds(30), ct)).ExitCode == 0;

    /// <summary>PR のリポジトリ（URL の末尾が /{リポジトリ名}）を指すリモート。origin を優先する。</summary>
    private async Task<string?> FindRemoteAsync(PrRef pr, CancellationToken ct)
    {
        var result = await RunGitAsync(repository, ["remote", "-v"], TimeSpan.FromSeconds(30), ct);
        var matches = Encoding.UTF8.GetString(result.Output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && RemoteMatches(parts[1], pr.Repository))
            .Select(parts => parts[0])
            .Distinct()
            .ToList();
        return matches.Contains("origin") ? "origin" : matches.FirstOrDefault();
    }

    /// <summary>リモート URL が指定のリポジトリを指すか（https・ssh 両方の形式）。</summary>
    internal static bool RemoteMatches(string url, string repository)
    {
        string path = Uri.UnescapeDataString(url).TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        return path.EndsWith("/" + repository, StringComparison.OrdinalIgnoreCase);
    }

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;

    private sealed record GitResult(int ExitCode, byte[] Output, string Error);

    /// <exception cref="Win32Exception">git が見つからない。</exception>
    private static async Task<GitResult> RunGitAsync(string workingDirectory, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(workingDirectory);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // 端末でのパスワード入力待ちで止まらないようにする（資格情報マネージャーの画面は出てよい）
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi) ?? throw new Win32Exception("git を起動できませんでした。");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var output = new MemoryStream();
            var readOut = process.StandardOutput.BaseStream.CopyToAsync(output, timeoutCts.Token);
            var readErr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(readOut, readErr);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new GitResult(process.ExitCode, output.ToArray(), readErr.Result);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 既に終了している
            }

            ct.ThrowIfCancellationRequested();
            return new GitResult(-1, [], $"git {string.Join(' ', args)} が {timeout.TotalSeconds:0} 秒で終わりませんでした");
        }
    }
}
