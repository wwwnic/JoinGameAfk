using System.Windows;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.MVP.Controller;
using JoinGameAfk.Theme;
using JoinGameAfk.View;

namespace JoinGameAfk
{
    public partial class App : Application
    {
        private MainWindow? fMainWindow;
        private PhaseProgressionPage? fDashboardPage;
        private LogsPage? fLogsPage;
        private PhaseController? fPhaseController;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                var champSelectSettings = ChampSelectSettings.Load();
                champSelectSettings.ThemeKey = AppThemeManager.NormalizeThemeKey(champSelectSettings.ThemeKey);
                AppThemeManager.ApplyTheme(champSelectSettings.ThemeKey);

                fMainWindow = CreateMainWindow(champSelectSettings);
                MainWindow = fMainWindow;
                fMainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "An error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private MainWindow CreateMainWindow(ChampSelectSettings champSelectSettings, int activeTabIndex = 0)
        {
            fDashboardPage = new PhaseProgressionPage();
            fLogsPage = new LogsPage();
            fDashboardPage.SetLogsPage(fLogsPage);
            fDashboardPage.SetSettings(champSelectSettings);

            fPhaseController = new PhaseController(fDashboardPage, fLogsPage, champSelectSettings);
            fDashboardPage.SetController(fPhaseController);

            var champSelectPage = new ChampSelectSettingsPage(champSelectSettings);
            var settingsPage = new SettingsPage(champSelectSettings, ReloadUiForTheme);

            var mainWindow = new MainWindow(fDashboardPage, champSelectPage, settingsPage);
            fDashboardPage.PhaseChanged += mainWindow.UpdatePhaseIndicator;
            mainWindow.UpdatePhaseIndicator(ClientPhase.Unknown);
            mainWindow.ActivateTab(activeTabIndex);

            return mainWindow;
        }

        private void ReloadUiForTheme(ChampSelectSettings champSelectSettings)
        {
            try
            {
                bool restartWatcher = fPhaseController?.IsRunning == true;
                int activeTabIndex = fMainWindow?.ActiveTabIndex ?? 0;
                var previousWindow = fMainWindow;

                fPhaseController?.Stop();

                champSelectSettings.ThemeKey = AppThemeManager.NormalizeThemeKey(champSelectSettings.ThemeKey);
                AppThemeManager.ApplyTheme(champSelectSettings.ThemeKey);

                var newWindow = CreateMainWindow(champSelectSettings, activeTabIndex);
                if (previousWindow is not null)
                    CopyWindowPlacement(previousWindow, newWindow);

                fMainWindow = newWindow;
                MainWindow = newWindow;
                newWindow.Show();

                previousWindow?.Close();

                if (restartWatcher)
                    fPhaseController?.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Theme Reload Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CopyWindowPlacement(Window source, Window target)
        {
            target.WindowStartupLocation = WindowStartupLocation.Manual;

            Rect bounds = source.WindowState == WindowState.Normal
                ? new Rect(source.Left, source.Top, source.Width, source.Height)
                : source.RestoreBounds;

            target.Left = bounds.Left;
            target.Top = bounds.Top;
            target.Width = bounds.Width;
            target.Height = bounds.Height;

            target.WindowState = source.WindowState == WindowState.Minimized
                ? WindowState.Normal
                : source.WindowState;
        }
    }
}