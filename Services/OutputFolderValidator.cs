using System.IO;

namespace DeliverableOrganizer.Services;

/// <summary>出力先フォルダの確認結果。</summary>
/// <param name="Error">使えない理由。使えるなら <see langword="null"/>。</param>
/// <param name="Created">フォルダがなかったので作成した。</param>
public sealed record OutputFolderCheck(string? Error, bool Created = false);

/// <summary>［実行］の前に出力先フォルダが使えるかを確かめる。</summary>
public static class OutputFolderValidator
{
    /// <summary>
    /// 絶対パスか・使えない文字がないか・ドライブがあるか・書き込めるかを確かめる。
    /// フォルダがなければ作成する（ドライブと書式が正しい場合だけ）。
    /// </summary>
    public static OutputFolderCheck Check(string folder)
    {
        folder = folder.Trim();
        if (folder.Length == 0)
        {
            return new("出力先フォルダを入力してください。");
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            return new($"出力先フォルダは C:\\… のような絶対パスで指定してください。\n{folder}");
        }

        // ルート（C:\ や \\server\share\）より後ろの各フォルダ名に、ファイル名に使えない文字がないか
        string root = Path.GetPathRoot(folder) ?? "";
        var invalid = Path.GetInvalidFileNameChars();
        var badName = folder[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(name => name.IndexOfAny(invalid) >= 0 || name.Trim().Length == 0);
        if (badName is not null)
        {
            return new($"出力先フォルダに使えない文字が含まれています（\\ / : * ? \" < > | は使えません）。\n{folder}");
        }

        if (!Directory.Exists(root))
        {
            return new($"出力先フォルダのドライブが見つかりません（{root}）。\n{folder}");
        }

        if (File.Exists(folder))
        {
            return new($"出力先にはファイルではなくフォルダを指定してください。\n{folder}");
        }

        bool created = false;
        try
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                created = true;
            }

            // 実際に書けるか試す（読み取り専用・権限なしを先に見つける）
            string probe = Path.Combine(folder, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new($"出力先フォルダに書き込めません: {ex.Message}\n{folder}", created);
        }

        return new(null, created);
    }
}
