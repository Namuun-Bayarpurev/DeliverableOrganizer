using CommunityToolkit.Mvvm.ComponentModel;

namespace DeliverableOrganizer.ViewModels;

/// <summary>PR 一覧（⑦）の 1 行。</summary>
public partial class PrItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private int? _pullRequestId;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _branch = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private int? _fileCount;

    /// <summary>true のとき行を赤表示する。</summary>
    [ObservableProperty]
    private bool _isError;

    public string FileCountText => FileCount is int n ? $"{n} 件" : "";

    partial void OnFileCountChanged(int? value) => OnPropertyChanged(nameof(FileCountText));
}
