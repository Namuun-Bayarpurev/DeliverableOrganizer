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

        // 保存済みの PAT を PasswordBox に表示する（PasswordBox はバインドできない）
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                PatBox.Password = vm.Pat;
            }
        };

        // 終了時に、最後に入力した PAT を「保存する」の状態に合わせて保存または削除する
        Closing += (_, _) => (DataContext as MainViewModel)?.PersistPat();
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
