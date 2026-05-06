using System.Windows;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.MVP.Controller;
using JoinGameAfk.Plugin.Services;
using JoinGameAfk.Theme;
using JoinGameAfk.View;

namespace JoinGameAfk
{
    public partial class App : Application
    {
        private static readonly TimeSpan ChampionCatalogAutoUpdateInterval = TimeSpan.FromHours(24);

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

                _ = AutoUpdateChampionCatalogOnStartupAsync(champSelectSettings);
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

            fPhaseController = new PhaseController(fDashboardPage, fLogsPage, champSelectSettings);

            var championPrioritiesPage = new ChampionPrioritiesPage(champSelectSettings);
            var settingsPage = new SettingsPage(champSelectSettings, ReloadUiForTheme);

            var mainWindow = new MainWindow(fDashboardPage, fLogsPage, championPrioritiesPage, settingsPage);
            mainWindow.SetController(fPhaseController);
            fDashboardPage.PhaseChanged += mainWindow.UpdatePhaseIndicator;
            fDashboardPage.WatcherStateChanged += mainWindow.SetWatcherState;
            fDashboardPage.ClientConnectionChanged += mainWindow.SetClientConnection;
            fDashboardPage.ChampSelectSubPhaseChanged += mainWindow.UpdateChampSelectSubPhase;
            mainWindow.UpdatePhaseIndicator(ClientPhase.Unknown);
            mainWindow.SetWatcherState(false);
            mainWindow.SetClientConnection(false);
            mainWindow.UpdateChampSelectSubPhase(string.Empty);
            mainWindow.ActivateTab(activeTabIndex);

            return mainWindow;
        }

        private async Task AutoUpdateChampionCatalogOnStartupAsync(ChampSelectSettings champSelectSettings)
        {
            if (!champSelectSettings.AutoUpdateChampionCatalogOnStartup)
                return;

            var syncInfo = ChampionCatalog.GetLocalSyncInfo();
            if (!ShouldAutoUpdateChampionCatalog(syncInfo.LastSyncedAtUtc, out DateTime nextUpdateAtUtc))
            {
                fLogsPage?.WriteLine($"Champion list auto-update skipped. Last sync was less than 24 hours ago; next automatic check is after {nextUpdateAtUtc.ToLocalTime():g}. Use Settings > Update Champion List to sync manually.");
                return;
            }

            fLogsPage?.WriteLine("Champion list auto-update is enabled. Contacting Riot Data Dragon to refresh champion names.");

            try
            {
                var result = await ChampionCatalog.RefreshFromDataDragonAsync(new DataDragonChampionCatalogService());
                fLogsPage?.WriteLine($"Champion list auto-update completed. Riot Data Dragon {result.DataDragonVersion} ({result.ChampionCount} champions). Last sync: {result.LastSyncedAtUtc.ToLocalTime():g}.");
            }
            catch (Exception ex)
            {
                fLogsPage?.WriteErrorLine($"Champion list auto-update failed. Existing local champion list was kept. {ex.Message}");
            }
        }

        private static bool ShouldAutoUpdateChampionCatalog(DateTime? lastSyncedAtUtc, out DateTime nextUpdateAtUtc)
        {
            nextUpdateAtUtc = DateTime.MinValue;

            if (lastSyncedAtUtc is null)
                return true;

            DateTime normalizedLastSyncedAtUtc = DateTime.SpecifyKind(lastSyncedAtUtc.Value, DateTimeKind.Utc);
            nextUpdateAtUtc = normalizedLastSyncedAtUtc.Add(ChampionCatalogAutoUpdateInterval);

            return DateTime.UtcNow >= nextUpdateAtUtc;
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
