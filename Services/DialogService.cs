using System.Diagnostics;
using System.IO;
using System.Windows;
using DeliverableOrganizer.ViewModels;
using DeliverableOrganizer.Views;
using Microsoft.Win32;

namespace DeliverableOrganizer.Services;

public sealed class DialogService : IDialogService
{
    private const string AppTitle = "納品物整理ツール";

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public bool ShowPreview(PreviewViewModel viewModel)
    {
        var window = new PreviewWindow
        {
            DataContext = viewModel,
            Owner = Application.Current.MainWindow,
        };
        return window.ShowDialog() == true;
    }

    public void ShowError(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);

    public void OpenInExplorer(string folder) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
}
