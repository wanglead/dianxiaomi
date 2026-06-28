using System.ComponentModel;
using System.Windows;

namespace OrderAlert.App;

public partial class MainWindow : Window
{
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (AllowClose) return;
        eventArgs.Cancel = true;
        Hide();
    }
}
