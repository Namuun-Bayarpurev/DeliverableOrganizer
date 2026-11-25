using System.Windows;

namespace DeliverableOrganizer.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow() => InitializeComponent();

    private void Run_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
