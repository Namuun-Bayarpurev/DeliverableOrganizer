using System.Windows;
using DeliverableOrganizer.Services;
using DeliverableOrganizer.ViewModels;
using DeliverableOrganizer.Views;

namespace DeliverableOrganizer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.DataContext = new MainViewModel(new DialogService(), new WindowsCredentialPatStore());
        window.Show();
    }
}
