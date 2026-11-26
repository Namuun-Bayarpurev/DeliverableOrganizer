using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.Services;

/// <summary>Azure DevOps の PR URL を解析する（dev.azure.com／*.visualstudio.com）。</summary>
public static class PrUrlParser
{
    /// <summary>
    /// PR URL から組織（BaseUrl）・プロジェクト・リポジトリ・PR 番号を取り出す。形式が正しくなければ <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 対応する形式（クエリ・フラグメント・末尾の / は無視）:
    /// <list type="bullet">
    /// <item>https://dev.azure.com/{組織}/{プロジェクト}/_git/{リポジトリ}/pullrequest/{番号}</item>
    /// <item>https://{組織}.visualstudio.com/[DefaultCollection/]{プロジェクト}/_git/{リポジトリ}/pullrequest/{番号}</item>
    /// <item>プロジェクトを省いた形式（…/_git/{リポジトリ}/…）。リポジトリ名をプロジェクト名とみなす</item>
    /// </list>
    /// </remarks>
    public static PrRef? TryParse(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();

        string baseUrl;
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            // 先頭のセグメントが組織
            if (segments.Count == 0)
            {
                return null;
            }

            baseUrl = $"{uri.Scheme}://{uri.Host}/{Uri.EscapeDataString(segments[0])}";
            segments.RemoveAt(0);
        }
        else if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"{uri.Scheme}://{uri.Host}";
            if (segments.Count != 0 && segments[0].Equals("DefaultCollection", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl += "/" + segments[0];
                segments.RemoveAt(0);
            }
        }
        else
        {
            return null;
        }

        // 残りは [{プロジェクト}/]_git/{リポジトリ}/pullrequest/{番号}
        int git = segments.FindIndex(s => s.Equals("_git", StringComparison.OrdinalIgnoreCase));
        if (git is < 0 or > 1
            || segments.Count != git + 4
            || !segments[git + 2].Equals("pullrequest", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(segments[git + 3], out int id)
            || id <= 0)
        {
            return null;
        }

        string repository = segments[git + 1];
        string project = git == 1 ? segments[0] : repository;
        return new PrRef(baseUrl, project, repository, id, url.Trim());
    }
}
