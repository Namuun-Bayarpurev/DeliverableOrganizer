using System.Collections.ObjectModel;

namespace DeliverableOrganizer.ViewModels;

/// <summary>プレビュー画面のツリー 1 ノード。</summary>
public sealed class TreeNode(string name, bool isFolder = true)
{
    public string Name { get; } = name;

    public bool IsFolder { get; } = isFolder;

    public ObservableCollection<TreeNode> Children { get; } = [];

    public TreeNode Add(string name, bool isFolder = true)
    {
        var node = new TreeNode(name, isFolder);
        Children.Add(node);
        return node;
    }

    public int CountFiles() => (IsFolder ? 0 : 1) + Children.Sum(c => c.CountFiles());
}

/// <summary>プレビュー画面（出力予定のフォルダツリーとファイル数）。</summary>
public sealed class PreviewViewModel(TreeNode root, string? note = null)
{
    public ObservableCollection<TreeNode> Roots { get; } = [root];

    public string Summary => $"出力予定ファイル数: {root.CountFiles()} 件";

    public string? Note { get; } = note;
}
