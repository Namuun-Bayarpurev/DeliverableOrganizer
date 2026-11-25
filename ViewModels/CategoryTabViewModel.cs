using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeliverableOrganizer.Models;

namespace DeliverableOrganizer.ViewModels;

/// <summary>機能タブ（④）1 つ分。URL・対象パターン・出力範囲・PR 一覧を持つ。</summary>
public partial class CategoryTabViewModel : ObservableObject
{
    public CategoryTabViewModel(Category category, string name)
    {
        Category = category;
        Name = name;
    }

    public Category Category { get; }

    public string Name { get; }

    /// <summary>⑧ 対象パターンは仕様書・テストタブのみ。</summary>
    public bool ShowPatterns => Category != Category.Source;

    /// <summary>⑨ 出力範囲はテストタブのみ。</summary>
    public bool ShowScope => Category == Category.Test;

    /// <summary>⑤ PR URL 入力（1 行 1 URL）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Count), nameof(Header))]
    private string _urlText = "";

    /// <summary>⑧ 対象パターン（カンマ区切り）。</summary>
    [ObservableProperty]
    private string _patterns = "";

    /// <summary>⑨ 出力範囲。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScopeChanged), nameof(IsScopeAll))]
    private TestScope _testScope = TestScope.Changed;

    public bool IsScopeChanged
    {
        get => TestScope == TestScope.Changed;
        set { if (value) TestScope = TestScope.Changed; }
    }

    public bool IsScopeAll
    {
        get => TestScope == TestScope.All;
        set { if (value) TestScope = TestScope.All; }
    }

    /// <summary>⑦ PR 一覧。</summary>
    public ObservableCollection<PrItemViewModel> PrItems { get; } = [];

    /// <summary>登録件数（空行・重複を除く）。</summary>
    public int Count => GetUrls().Count;

    /// <summary>タブ名（件数付き）。</summary>
    public string Header => $"{Name} ({Count})";

    /// <summary>入力された URL を 1 行 1 件で返す。空行・重複は無視。</summary>
    public IReadOnlyList<string> GetUrls() =>
        UrlText.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>対象パターンをカンマ区切りで分解して返す。</summary>
    public IReadOnlyList<string> GetPatterns() =>
        Patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>タブの UI Automation 名（読み上げ）に使われる。</summary>
    public override string ToString() => Header;
}
