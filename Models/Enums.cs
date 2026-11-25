namespace DeliverableOrganizer.Models;

/// <summary>機能タブの種別（F1〜F3）。</summary>
public enum Category
{
    /// <summary>F1 ソースの整理 → コード\</summary>
    Source,
    /// <summary>F2 仕様書の整理 → 仕様書\</summary>
    Spec,
    /// <summary>F3 テスト結果整理 → デバッグ\</summary>
    Test,
}

/// <summary>既存の PR フォルダがある場合の扱い（C6）。</summary>
public enum ExistingFolderMode
{
    Overwrite,
    Recreate,
    Skip,
}

/// <summary>F3 の出力範囲。</summary>
public enum TestScope
{
    /// <summary>PR の変更分</summary>
    Changed,
    /// <summary>PR 時点の全テスト仕様書</summary>
    All,
}

/// <summary>PR 内のファイル変更種別。</summary>
public enum ChangeType
{
    Edit,
    Add,
    Delete,
    Rename,
}

/// <summary>ログの重要度。</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
}
