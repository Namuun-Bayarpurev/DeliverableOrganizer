using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using DeliverableOrganizer.ViewModels;

namespace DeliverableOrganizer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, e) =>
        {
            // 新しいログが追加されたら末尾までスクロールする
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // レイアウト確定後にスクロールしないと最終行の 1 つ手前で止まることがある
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (LogList.Items.Count > 0)
                    {
                        LogList.ScrollIntoView(LogList.Items[^1]);
                    }
                });
            }
        };
    }

    /// <summary>PasswordBox はバインドできないため、ここで ViewModel に渡す。</summary>
    private void PatBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Pat = PatBox.Password;
        }
    }
}
