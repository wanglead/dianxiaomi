using System.Media;
using Hardcodet.Wpf.TaskbarNotification;
using OrderAlert.Core.Models;

namespace OrderAlert.App.Notifications;

public sealed record AlertSummary(
    int DueSoon,
    int Critical,
    int Overdue,
    int AffectedStores,
    int Failures);

public sealed class WindowsAlertService
{
    private readonly TaskbarIcon _trayIcon;

    public WindowsAlertService(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
    }

    public void Show(AlertSummary summary, AppSettings settings)
    {
        if (settings.PopupEnabled)
        {
            _trayIcon.ShowBalloonTip(
                "订单临期预警",
                $"临期 {summary.DueSoon}，紧急 {summary.Critical}，超时 {summary.Overdue}，"
                + $"涉及店铺 {summary.AffectedStores}，检查异常 {summary.Failures}",
                summary.Overdue > 0 || summary.Failures > 0
                    ? BalloonIcon.Error
                    : BalloonIcon.Warning);
        }

        if (!settings.SoundEnabled) return;
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // Notification display and dashboard counts must survive audio failures.
        }
    }

    public static void PreviewSound() => SystemSounds.Exclamation.Play();
}
