using System.IO;
using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>1 つの PR の出力結果。</summary>
/// <param name="Folder">PR フォルダ</param>
/// <param name="Files">書き込んだファイル数（_PR情報.txt を除く）</param>
/// <param name="Warnings">取得・書き込みできずに飛ばしたファイル数</param>
/// <param name="Skipped">既存フォルダがあったので何もしなかった</param>
public sealed record ExportResult(string Folder, int Files, int Warnings, bool Skipped);

/// <summary>
/// F1 ソースの整理。<c>&lt;出力先&gt;\コード\Pull Request {id}\before</c>／<c>after</c> に、リポジトリ内のフォルダ構成のまま書き出す。
/// 修正＝両方、追加＝after のみ、削除＝before のみ、名前変更＝旧パスで before／新パスで after。
/// </summary>
/// <param name="log">ログ出力。複数スレッドから呼ばれる。</param>
public sealed class CodeExporter(IContentSource source, Action<LogLevel, string> log)
{
    public const string CategoryFolder = "コード";

    /// <summary>PR 1 件を書き出す。1 ファイルの失敗は警告にして続ける。</summary>
    /// <exception cref="AzureDevOpsException">認証エラー（<see cref="AzureDevOpsException.IsAuthError"/>）。処理全体を止めること。</exception>
    /// <exception cref="IOException">PR フォルダを作成・削除できない（ファイルを開いている等）。</exception>
    /// <exception cref="OperationCanceledException">中止された。取得済みのファイルは書き終えてから止まる。</exception>
    public async Task<ExportResult> ExportAsync(PrData pr, ExportOptions options, CancellationToken ct)
    {
        int id = pr.Ref.PullRequestId;
        string prFolder = Path.Combine(options.OutputFolder, CategoryFolder, PrFolderName(options.PrFolderFormat, id));

        // C6 既存フォルダの扱い
        if (Directory.Exists(prFolder))
        {
            switch (options.ExistingFolder)
            {
                case ExistingFolderMode.Skip:
                    log(LogLevel.Info, $"PR {id}: フォルダが既にあるためスキップしました（{prFolder}）");
                    return new ExportResult(prFolder, 0, 0, Skipped: true);
                case ExistingFolderMode.Recreate:
                    DeleteFolder(prFolder);
                    break;
            }
        }

        Directory.CreateDirectory(prFolder);

        var jobs = new List<(string Sha, string RepoPath, string Side)>();
        foreach (var change in pr.Changes)
        {
            if (change.ChangeType is ChangeType.Edit or ChangeType.Delete)
            {
                jobs.Add((pr.BaseSha, change.Path, "before"));
            }
            else if (change.ChangeType == ChangeType.Rename)
            {
                jobs.Add((pr.BaseSha, change.OriginalPath ?? change.Path, "before"));
            }

            if (change.ChangeType != ChangeType.Delete)
            {
                jobs.Add((pr.HeadSha, change.Path, "after"));
            }
        }

        int files = 0;
        int warnings = 0;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.MaxParallel), CancellationToken = ct };
        await Parallel.ForEachAsync(jobs, parallel, async (job, token) =>
        {
            try
            {
                string dest = ToLocalPath(Path.Combine(prFolder, job.Side), job.RepoPath);
                byte[] content = await source.GetAsync(pr.Ref, job.Sha, job.RepoPath, token);

                // 取得できたファイルは中止されても書き終える
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await File.WriteAllBytesAsync(dest, content, CancellationToken.None);
                Interlocked.Increment(ref files);
            }
            catch (AzureDevOpsException ex) when (ex.IsAuthError)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref warnings);
                log(LogLevel.Warning, $"PR {id}: {job.Side} {job.RepoPath} を出力できませんでした: {ex.Message}");
            }
        });

        if (options.WritePrInfo)
        {
            await PrInfoWriter.WriteAsync(prFolder, pr);
        }

        return new ExportResult(prFolder, files, warnings, Skipped: false);
    }

    /// <summary>PR フォルダ名（既定 <c>Pull Request {id}</c>）。</summary>
    public static string PrFolderName(string format, int id) => format.Replace("{id}", id.ToString());

    /// <summary>
    /// リポジトリ内パス（/src/a.cs）を <paramref name="root"/> 配下のパスにする。
    /// <c>..</c> などで <paramref name="root"/> の外に出るパスは受け付けない。
    /// </summary>
    /// <exception cref="ArgumentException">使えないパス。</exception>
    public static string ToLocalPath(string root, string repoPath)
    {
        var segments = repoPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var invalid = Path.GetInvalidFileNameChars();
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.IndexOfAny(invalid) >= 0))
        {
            throw new ArgumentException($"Windows で使えないパスです: {repoPath}");
        }

        return Path.Combine([root, .. segments]);
    }

    /// <summary>読み取り専用のファイルがあっても消せるように属性を外してから消す。</summary>
    private static void DeleteFolder(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }
}
