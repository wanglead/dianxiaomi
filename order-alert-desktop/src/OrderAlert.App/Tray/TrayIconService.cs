using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using OrderAlert.App.ViewModels;

namespace OrderAlert.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _pauseItem;

    public TrayIconService(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _pauseItem = new MenuItem { Header = "暂停监控" };
        _pauseItem.Click += (_, _) => TogglePause();
        _icon = new TaskbarIcon
        {
            ToolTipText = "订单临期预警",
            Icon = SystemIcons.Application,
            ContextMenu = CreateMenu()
        };
        _icon.TrayLeftMouseUp += (_, _) => ShowWindow();
    }

    public void Dispose() => _icon.Dispose();

    private ContextMenu CreateMenu()
    {
        var open = new MenuItem { Header = "打开" };
        open.Click += (_, _) => ShowWindow();
        var check = new MenuItem { Header = "立即检查", Command = _viewModel.CheckNowCommand };
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) =>
        {
            _window.AllowClose = true;
            _window.Close();
            Application.Current.Shutdown();
        };
        return new ContextMenu
        {
            Items = { open, check, _pauseItem, new Separator(), exit }
        };
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void TogglePause()
    {
        var paused = _viewModel.RunState == "已暂停";
        _viewModel.RunState = paused ? "监控中" : "已暂停";
        _pauseItem.Header = paused ? "暂停监控" : "恢复监控";
    }
}
