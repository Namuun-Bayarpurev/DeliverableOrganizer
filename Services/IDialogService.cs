using DeliverableOrganizer.ViewModels;

namespace DeliverableOrganizer.Services;

/// <summary>ViewModel から画面操作（ダイアログ・エクスプローラー）を行うための窓口。</summary>
public interface IDialogService
{
    /// <summary>フォルダ選択ダイアログ。キャンセル時は null。</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>プレビュー画面を表示する。[実行] が押されたら true。</summary>
    bool ShowPreview(PreviewViewModel viewModel);

    void ShowError(string message);

    void OpenInExplorer(string folder);
}
