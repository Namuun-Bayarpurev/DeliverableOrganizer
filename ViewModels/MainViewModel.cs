using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeliverableOrganizer.Models;
using DeliverableOrganizer.Services;

namespace DeliverableOrganizer.ViewModels;

/// <summary>既存フォルダ（⑪）のプルダウン項目。</summary>
public sealed record ExistingFolderOption(ExistingFolderMode Mode, string Label);

/// <summary>メイン画面。共通設定、実行／中止、進捗、ログを持つ。</summary>
public partial class MainViewModel : ObservableObject
{
    // HttpClient はアプリ全体で 1 つを使い回す（作り直すとソケットが枯渇する）
    private static readonly HttpClient Http = new();

    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        Tabs =
        [
            new CategoryTabViewModel(Category.Source, "ソースの整理"),
            new CategoryTabViewModel(Category.Spec, "仕様書の整理"),
            new CategoryTabViewModel(Category.Test, "テスト結果整理"),
        ];
        foreach (var tab in Tabs)
        {
            tab.PropertyChanged += OnTabPropertyChanged;
        }

        _selectedTab = Tabs[0];
    }

    // ---- 共通設定（①〜③） ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunOrCancelCommand), nameof(PreviewCommand))]
    private string _outputFolder = "";

    /// <summary>② PAT。PasswordBox からコードビハインド経由で設定する。保存・ログ出力はしない。</summary>
    public string Pat { get; set; } = "";

    [ObservableProperty]
    private bool _savePat;

    [ObservableProperty]
    private string _localRepository = "";

    // ---- 機能タブ（④〜⑨） ----

    public ObservableCollection<CategoryTabViewModel> Tabs { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private CategoryTabViewModel _selectedTab;

    public int TotalUrlCount => Tabs.Sum(t => t.Count);

    // ---- オプション（⑩⑪） ----

    [ObservableProperty]
    private bool _writePrInfo = true;

    public IReadOnlyList<ExistingFolderOption> ExistingFolderOptions { get; } =
    [
        new(ExistingFolderMode.Overwrite, "上書き"),
        new(ExistingFolderMode.Recreate, "削除して再作成"),
        new(ExistingFolderMode.Skip, "スキップ"),
    ];

    [ObservableProperty]
    private ExistingFolderMode _existingFolder = ExistingFolderMode.Recreate;

    // ---- 実行・進捗・ログ（⑫〜⑯） ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunButtonText))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand), nameof(ConfirmCommand), nameof(OpenOutputCommand))]
    private bool _isRunning;

    public string RunButtonText => IsRunning ? "中止" : "実行";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputCommand))]
    private bool _hasCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _progressMax = 1;

    public string ProgressText => IsRunning || HasCompleted ? $"{ProgressValue} / {ProgressMax}" : "";

    public ObservableCollection<LogEntry> Logs { get; } = [];

    public void Log(LogLevel level, string message) => Logs.Add(new LogEntry(DateTime.Now, level, message));

    // ---- コマンド ----

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        if (_dialogs.PickFolder("出力先フォルダを選択", OutputFolder) is { } folder)
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    private void BrowseLocalRepository()
    {
        if (_dialogs.PickFolder("ローカルリポジトリを選択", LocalRepository) is { } folder)
        {
            LocalRepository = folder;
        }
    }

    /// <summary>⑥ 確認。URL 形式チェックと PR 情報取得（C1）。結果を ⑦ PR 一覧に表示する。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var tab = SelectedTab;
        tab.PrItems.Clear();

        // 先に全行を出してから順に取得する（形式が不正な行はその場でエラー表示）
        var targets = new List<(PrItemViewModel Item, PrRef Pr)>();
        foreach (var url in tab.GetUrls())
        {
            var pr = PrUrlParser.TryParse(url);
            var item = new PrItemViewModel
            {
                Url = url,
                PullRequestId = pr?.PullRequestId,
                Title = pr is null ? "URL の形式が不正です" : "取得中...",
                IsError = pr is null,
            };
            tab.PrItems.Add(item);
            if (pr is not null)
            {
                targets.Add((item, pr));
            }
        }

        var loader = new PrLoader(new AzureDevOpsClient(Http, CredentialProvider.ResolvePat(Pat)));
        int loaded = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            var (item, pr) = targets[i];
            try
            {
                var data = await loader.LoadAsync(pr);
                item.Title = data.Title;
                item.Branch = $"{data.SourceBranch} → {data.TargetBranch}";
                item.Status = StatusText(data.Status);
                item.FileCount = data.Changes.Count;
                loaded++;
            }
            catch (AzureDevOpsException ex) when (ex.IsAuthError)
            {
                // 認証エラーは他の PR でも同じなので、その場で止める
                item.IsError = true;
                item.Title = ex.Message;
                foreach (var (rest, _) in targets.Skip(i + 1))
                {
                    rest.IsError = true;
                    rest.Title = "認証エラーのため取得を中止しました";
                }

                Log(LogLevel.Error, $"[{tab.Name}] {ex.Message}");
                _dialogs.ShowError(ex.Message);
                return;
            }
            catch (AzureDevOpsException ex)
            {
                item.IsError = true;
                item.Title = ex.Message;
                Log(LogLevel.Error, $"[{tab.Name}] PR {pr.PullRequestId}: {ex.Message}");
            }
        }

        int errors = tab.PrItems.Count - loaded;
        Log(errors == 0 ? LogLevel.Info : LogLevel.Warning,
            $"[{tab.Name}] {tab.PrItems.Count} 件中 {loaded} 件の PR 情報を取得しました（エラー {errors} 件）");
    }

    private static string StatusText(string status) => status switch
    {
        "active" => "アクティブ",
        "completed" => "完了",
        "abandoned" => "破棄",
        _ => status,
    };

    private bool CanConfirm() => !IsRunning && SelectedTab.Count > 0;

    /// <summary>⑫ プレビュー。出力予定のフォルダツリーを別画面で表示する。</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task PreviewAsync()
    {
        var preview = new PreviewViewModel(BuildPreviewTree(), "※ 変更ファイルは PR 情報取得の実装後に表示されます");
        if (_dialogs.ShowPreview(preview))
        {
            await RunAsync();
        }
    }

    /// <summary>⑬ 実行／中止。</summary>
    /// <remarks>実行中も [中止] を押せるよう同時実行を許可する（二重実行は IsRunning で防ぐ）。</remarks>
    [RelayCommand(CanExecute = nameof(CanRunOrCancel), AllowConcurrentExecutions = true)]
    private async Task RunOrCancelAsync()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            return;
        }

        await RunAsync();
    }

    private bool CanStart() => !IsRunning && !string.IsNullOrWhiteSpace(OutputFolder) && TotalUrlCount > 0;

    private bool CanRunOrCancel() => IsRunning || CanStart();

    /// <summary>⑭ 出力先を開く（完了後に有効）。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private void OpenOutput()
    {
        if (!Directory.Exists(OutputFolder))
        {
            _dialogs.ShowError($"出力先フォルダが見つかりません。\n{OutputFolder}");
            return;
        }

        _dialogs.OpenInExplorer(OutputFolder);
    }

    private bool CanOpenOutput() => HasCompleted && !IsRunning;

    // ---- 実行処理 ----

    /// <summary>
    /// 全タブの PR を一括処理する。
    /// 現時点では画面動作確認用の仮実装で、ファイルの取得・書き込みは行わない。
    /// </summary>
    private async Task RunAsync()
    {
        var jobs = Tabs.SelectMany(t => t.GetUrls().Select(u => (Tab: t, Url: u))).ToList();

        _cts = new CancellationTokenSource();
        IsRunning = true;
        HasCompleted = false;
        ProgressValue = 0;
        ProgressMax = Math.Max(jobs.Count, 1);
        Log(LogLevel.Info, $"=== 実行開始（{jobs.Count} 件）");
        Log(LogLevel.Warning, "出力処理は未実装のため、ファイルは書き込みません（画面動作の確認用）");

        try
        {
            foreach (var (tab, url) in jobs)
            {
                _cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(400, _cts.Token);
                Log(LogLevel.Info, $"[{tab.Name}] {url}");
                ProgressValue++;
            }

            Log(LogLevel.Info, "=== 完了しました（エラー 0 件）");
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warning, "中止しました");
        }
        finally
        {
            IsRunning = false;
            HasCompleted = true;
            OnPropertyChanged(nameof(ProgressText));
            _cts.Dispose();
            _cts = null;
        }
    }

    private TreeNode BuildPreviewTree()
    {
        var root = new TreeNode(OutputFolder);
        foreach (var tab in Tabs)
        {
            var urls = tab.GetUrls();
            if (urls.Count == 0)
            {
                continue;
            }

            var categoryNode = root.Add(CategoryFolderName(tab.Category));
            foreach (var url in urls)
            {
                var pr = PrUrlParser.TryParse(url);
                var prNode = categoryNode.Add(pr is null ? $"（URL 不正）{url}" : $"Pull Request {pr.PullRequestId}");
                if (tab.Category == Category.Source)
                {
                    prNode.Add("before");
                    prNode.Add("after");
                }

                if (WritePrInfo)
                {
                    prNode.Add("_PR情報.txt", isFolder: false);
                }
            }
        }

        root.Add("_log");
        return root;
    }

    public static string CategoryFolderName(Category category) => category switch
    {
        Category.Source => "コード",
        Category.Spec => "仕様書",
        Category.Test => "デバッグ",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategoryTabViewModel.Count))
        {
            OnPropertyChanged(nameof(TotalUrlCount));
            RunOrCancelCommand.NotifyCanExecuteChanged();
            PreviewCommand.NotifyCanExecuteChanged();
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }
}
