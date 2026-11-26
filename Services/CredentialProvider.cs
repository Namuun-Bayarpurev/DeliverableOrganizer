namespace DeliverableOrganizer.Services;

/// <summary>Azure DevOps の認証情報（PAT）を決める（C2）。</summary>
public static class CredentialProvider
{
    /// <summary>PAT を設定しておく環境変数。</summary>
    public const string PatEnvironmentVariable = "AZDO_PAT";

    /// <summary>
    /// 画面の PAT → 環境変数 <c>AZDO_PAT</c> の順に使う PAT を返す。どちらも空なら <see langword="null"/>（認証なしで呼ぶ）。
    /// </summary>
    /// <remarks>shortcut: 設計書 C2 の 3 番目（git credential fill）と資格情報マネージャーへの保存は未対応。PAT なしで使う人が出たら追加する</remarks>
    public static string? ResolvePat(string? screenPat)
    {
        if (!string.IsNullOrWhiteSpace(screenPat))
        {
            return screenPat.Trim();
        }

        string? env = Environment.GetEnvironmentVariable(PatEnvironmentVariable);
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }
}
