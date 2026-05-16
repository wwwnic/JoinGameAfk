using System.Windows;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.MVP.Controller;
using JoinGameAfk.Plugin.Services;
using JoinGameAfk.Services;
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

                if (champSelectSettings.PickBanOverlayOpenOnStartup)
                    fMainWindow.ShowPickBanOverlayOnStartup();

                if (champSelectSettings.StartWatcherOnStartup)
                    fPhaseController?.Start();

                LogChampionTileArchiveCleanup(ChampionTileCatalog.DeleteDownloadedArchives());
                _ = AutoSyncChampionDataOnStartupAsync(champSelectSettings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "An error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            fPhaseController?.Dispose();
            fPhaseController = null;
        }

        private MainWindow CreateMainWindow(ChampSelectSettings champSelectSettings, int activeTabIndex = 0)
        {
            fDashboardPage = new PhaseProgressionPage();
            var logsPage = new LogsPage();
            fLogsPage = logsPage;
            fDashboardPage.SetLogsPage(logsPage);

            fPhaseController = new PhaseController(fDashboardPage, logsPage, champSelectSettings);

            var championPrioritiesPage = new ChampionPrioritiesPage(champSelectSettings);
            var settingsPage = new SettingsPage(champSelectSettings, ReloadUiForTheme, logsPage.WriteLine, logsPage.WriteErrorLine);

            var mainWindow = new MainWindow(fDashboardPage, logsPage, championPrioritiesPage, settingsPage, champSelectSettings);
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

        private async Task AutoSyncChampionDataOnStartupAsync(ChampSelectSettings champSelectSettings)
        {
            if (!champSelectSettings.AutoUpdateChampionCatalogOnStartup)
                return;

            var remoteService = new DataDragonChampionCatalogService();
            string latestDataDragonVersion;

            fLogsPage?.WriteLine("Champion data startup update check is enabled. Checking the latest Riot Data Dragon version.");

            try
            {
                latestDataDragonVersion = await remoteService.FetchLatestDataDragonVersionAsync();
            }
            catch (Exception ex)
            {
                fLogsPage?.WriteErrorLine($"Champion data startup update check failed. Existing local champion data was kept. {FormatException(ex)}");
                return;
            }

            var catalogSyncInfo = ChampionCatalog.GetLocalSyncInfo();
            bool championListNeedsUpdate = !IsDataDragonVersionCurrent(catalogSyncInfo.DataDragonVersion, latestDataDragonVersion);
            if (championListNeedsUpdate)
            {
                try
                {
                    fLogsPage?.WriteLine($"Champion list update available. Local version: {FormatDataDragonVersion(catalogSyncInfo.DataDragonVersion)}; latest version: {latestDataDragonVersion}.");
                    var remoteCatalog = await remoteService.FetchChampionCatalogAsync(latestDataDragonVersion);
                    var result = ChampionCatalog.RefreshFromDataDragon(remoteCatalog);
                    fLogsPage?.WriteLine($"Champion list updated to Riot Data Dragon {result.DataDragonVersion} ({result.ChampionCount} champions). Last sync: {result.LastSyncedAtUtc.ToLocalTime():g}.");
                }
                catch (Exception ex)
                {
                    fLogsPage?.WriteErrorLine($"Champion list update failed. Existing local champion list was kept. {FormatException(ex)}");
                }
            }
            else
            {
                fLogsPage?.WriteLine($"Champion list is already current with Riot Data Dragon {latestDataDragonVersion}.");
            }

            var tileSyncInfo = ChampionTileCatalog.GetCacheSyncInfo();
            bool championPicturesNeedUpdate = !IsDataDragonVersionCurrent(tileSyncInfo.DataDragonVersion, latestDataDragonVersion);
            if (championPicturesNeedUpdate)
            {
                try
                {
                    fLogsPage?.WriteLine($"Champion picture update available. Local version: {FormatDataDragonVersion(tileSyncInfo.DataDragonVersion)}; latest version: {latestDataDragonVersion}. Installing the archive only because the version changed.");
                    var result = await ChampionTileCatalog.InstallDataDragonArchiveAsync(latestDataDragonVersion, CreateChampionTileArchiveLogProgress());
                    string archiveCleanupText = result.ArchiveDeleted
                        ? "archive removed after extraction"
                        : $"archive cleanup failed ({result.ArchiveDeleteError})";
                    fLogsPage?.WriteLine($"Champion pictures updated to Riot Data Dragon {result.DataDragonVersion}. Downloaded archive: {FormatMegabytes(result.ArchiveSizeBytes)}; {archiveCleanupText}. Checked {result.CheckedTileCount} champion tiles; updated {result.UpdatedTileCount}; unchanged {result.UnchangedTileCount}; cache now has {result.CachedTileCount} jpg files.");
                }
                catch (Exception ex)
                {
                    fLogsPage?.WriteErrorLine($"Champion picture update failed. Existing local picture cache was kept. {FormatException(ex)}");
                }
            }
            else
            {
                fLogsPage?.WriteLine($"Champion pictures are already current with Riot Data Dragon {latestDataDragonVersion}.");
            }
        }

        private static bool IsDataDragonVersionCurrent(string? localVersion, string latestVersion)
        {
            return !string.IsNullOrWhiteSpace(localVersion)
                && string.Equals(localVersion.Trim(), latestVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatDataDragonVersion(string? dataDragonVersion)
        {
            return string.IsNullOrWhiteSpace(dataDragonVersion)
                ? "none"
                : dataDragonVersion.Trim();
        }

        private static string FormatMegabytes(long bytes)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        private IProgress<ChampionTileArchiveProgress> CreateChampionTileArchiveLogProgress()
        {
            string? lastLoggedMessage = null;

            return new Progress<ChampionTileArchiveProgress>(snapshot =>
            {
                if (!ShouldLogChampionTileArchiveProgress(snapshot.Message)
                    || string.Equals(snapshot.Message, lastLoggedMessage, StringComparison.Ordinal))
                {
                    return;
                }

                lastLoggedMessage = snapshot.Message;
                if (IsChampionTileArchiveWarning(snapshot.Message))
                    fLogsPage?.WriteErrorLine(snapshot.Message);
                else
                    fLogsPage?.WriteLine(snapshot.Message);
            });
        }

        private void LogChampionTileArchiveCleanup(ChampionTileArchiveCleanupResult cleanupResult)
        {
            if (cleanupResult.DeletedFileCount > 0)
                fLogsPage?.WriteLine($"Removed {cleanupResult.DeletedFileCount} stale Data Dragon archive file(s) from local storage.");

            foreach (var failure in cleanupResult.Failures)
                fLogsPage?.WriteErrorLine($"Unable to remove stale Data Dragon archive file '{failure.FilePath}'. {failure.ErrorMessage}");
        }

        private static bool ShouldLogChampionTileArchiveProgress(string message)
        {
            return !string.IsNullOrWhiteSpace(message)
                && !message.StartsWith("Downloading Data Dragon archive:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChampionTileArchiveWarning(string message)
        {
            return message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
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
