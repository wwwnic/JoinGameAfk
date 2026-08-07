using System.Windows;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void ReviewUsageNoticeButton_Click(object sender, RoutedEventArgs e)
        {
            var notice = new ReleaseUsageNoticeWindow(ReleaseUsageNoticeWindow.GetCurrentAppVersion())
            {
                Owner = Window.GetWindow(this)
            };
            notice.ShowDialog();
        }
    }
}
