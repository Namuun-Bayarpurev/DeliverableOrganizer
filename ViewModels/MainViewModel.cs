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
    private readonly IPatStore _patStore;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private CancellationTokenSource? _cts;

    public MainViewModel(IDialogService dialogs, IPatStore patStore)
    {
        _dialogs = dialogs;
        _patStore = patStore;

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

        // 保存済みの PAT があれば復元し、「保存する」をオンにしておく（変更通知で消し直さないようフィールドに直接入れる）
        try
        {
            if (_patStore.Load() is { Length: > 0 } saved)
            {
                Pat = saved;
                _savePat = true;
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, $"保存済みの PAT を読み込めませんでした: {ex.Message}");
        }
    }

    // ---- 共通設定（①〜③） ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunOrCancelCommand), nameof(PreviewCommand))]
    private string _outputFolder = "";

    /// <summary>② PAT。PasswordBox とはコードビハインド経由でやり取りする。ログには出さない。</summary>
    public string Pat { get; set; } = "";

    /// <summary>「保存する」。オンにした時点と ［確認］［実行］・終了時に資格情報マネージャーへ保存し、オフにした時点で消す。</summary>
    [ObservableProperty]
    private bool _savePat;

    partial void OnSavePatChanged(bool value) => PersistPat();

    /// <summary>「保存する」の状態に合わせて PAT を保存または削除する。PAT が空なら保存済みのものも消す。</summary>
    public void PersistPat()
    {
        try
        {
            if (SavePat && !string.IsNullOrWhiteSpace(Pat))
            {
                _patStore.Save(Pat.Trim());
            }
            else
            {
                _patStore.Delete();
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, $"PAT を資格情報マネージャーに{(SavePat ? "保存" : "削除")}できませんでした: {ex.Message}");
        }
    }

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

    /// <summary>例: 1 / 2（50%）。実行前は空。</summary>
    public string ProgressText => IsRunning || HasCompleted
        ? $"{ProgressValue} / {ProgressMax}（{(ProgressMax == 0 ? 0 : ProgressValue * 100 / ProgressMax)}%）"
        : "";

    public ObservableCollection<LogEntry> Logs { get; } = [];

    public void Log(LogLevel level, string message) => Logs.Add(new LogEntry(DateTime.Now, level, message));

    /// <summary>並列で取得している途中（別スレッド）から呼べるログ。画面のスレッドに戻して追加する。</summary>
    private void LogFromAnyThread(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            lock (Logs)
            {
                Logs.Add(entry);
            }
        }
        else
        {
            _ui.Post(_ => Logs.Add(entry), null);
        }
    }

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
        PersistPat();
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
                item.Status = PrInfoWriter.StatusText(data.Status);
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
    /// 全タブの PR を一括処理する。1 件の PR・1 ファイルの失敗では止めず、認証エラーだけは全体を止める。
    /// </summary>
    /// <remarks>shortcut: 仕様書・テストタブ（F2・F3）は未実装のため、警告を出してスキップする</remarks>
    private async Task RunAsync()
    {
        PersistPat();

        // 出力先が使えなければ何も始めない
        var check = OutputFolderValidator.Check(OutputFolder);
        if (check.Error is not null)
        {
            Log(LogLevel.Error, check.Error.Replace('\n', ' '));
            _dialogs.ShowError(check.Error);
            return;
        }

        if (check.Created)
        {
            Log(LogLevel.Info, $"出力先フォルダを作成しました: {OutputFolder.Trim()}");
        }

        var options = new ExportOptions
        {
            OutputFolder = OutputFolder.Trim(),
            LocalRepository = string.IsNullOrWhiteSpace(LocalRepository) ? null : LocalRepository.Trim(),
            WritePrInfo = WritePrInfo,
            ExistingFolder = ExistingFolder,
        };
        var jobs = Tabs.SelectMany(t => t.GetUrls().Select(u => (Tab: t, Url: u))).ToList();

        // 1 回の実行につき PrLoader を 1 つ使う（同じ PR を何度も取得しない）
        var client = new AzureDevOpsClient(Http, CredentialProvider.ResolvePat(Pat));
        var loader = new PrLoader(client);
        var codeExporter = new CodeExporter(new ApiContentSource(client), LogFromAnyThread);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        HasCompleted = false;
        ProgressValue = 0;
        ProgressMax = Math.Max(jobs.Count, 1);
        Log(LogLevel.Info, $"=== 実行開始（{jobs.Count} 件）→ {options.OutputFolder}");

        int exported = 0, skipped = 0, errors = 0, warnings = 0;
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (tab, url) in jobs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var pr = PrUrlParser.TryParse(url);
                    if (pr is null)
                    {
                        errors++;
                        Log(LogLevel.Error, $"[{tab.Name}] URL の形式が不正です: {url}");
                        continue;
                    }

                    if (tab.Category != Category.Source)
                    {
                        warnings++;
                        Log(LogLevel.Warning, $"[{tab.Name}] PR {pr.PullRequestId}: このタブの出力はまだ実装していないためスキップしました");
                        continue;
                    }

                    // 書き方の違う同じ PR の URL は 1 回だけ出力する
                    if (!done.Add($"{tab.Category}|{pr.BaseUrl}/{pr.Project}/{pr.Repository}/{pr.PullRequestId}"))
                    {
                        continue;
                    }

                    var data = await loader.LoadAsync(pr, ct);
                    Log(LogLevel.Info, $"[{tab.Name}] PR {pr.PullRequestId}: 出力中（変更ファイル {data.Changes.Count} 件）");
                    var result = await codeExporter.ExportAsync(data, options, ct);
                    warnings += result.Warnings;
                    if (result.Skipped)
                    {
                        skipped++;
                    }
                    else
                    {
                        exported++;
                        Log(LogLevel.Info, $"[{tab.Name}] PR {pr.PullRequestId}: {result.Files} ファイルを出力しました → {result.Folder}");
                    }
                }
                catch (AzureDevOpsException ex) when (ex.IsAuthError)
                {
                    // 認証エラーは他の PR でも同じなので全体を止める
                    errors++;
                    Log(LogLevel.Error, $"[{tab.Name}] {ex.Message}");
                    Log(LogLevel.Error, "=== 認証エラーのため中断しました");
                    _dialogs.ShowError(ex.Message);
                    return;
                }
                catch (AzureDevOpsException ex)
                {
                    errors++;
                    Log(LogLevel.Error, $"[{tab.Name}] {url}: {ex.Message}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors++;
                    Log(LogLevel.Error, $"[{tab.Name}] {url}: PR フォルダに書き込めませんでした（ファイルを開いていないか確認してください）: {ex.Message}");
                }
                finally
                {
                    ProgressValue++;
                }
            }

            Log(errors > 0 ? LogLevel.Error : warnings > 0 ? LogLevel.Warning : LogLevel.Info,
                $"=== 完了しました（出力 {exported} 件、スキップ {skipped} 件、エラー {errors} 件、警告 {warnings} 件）");
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warning, $"=== 中止しました（出力 {exported} 件、エラー {errors} 件、警告 {warnings} 件）");
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
