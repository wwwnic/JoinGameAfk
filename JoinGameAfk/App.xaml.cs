using System.Windows;
using System.Windows.Media.Animation;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
using JoinGameAfk.Presentation.Controller;
using JoinGameAfk.Presentation.View;
using JoinGameAfk.Services;
using JoinGameAfk.Theme;

namespace JoinGameAfk
{
    public partial class App : Application
    {
        private MainWindow? fMainWindow;
        private PhaseProgressionPage? fDashboardPage;
        private LogsPage? fLogsPage;
        private PhaseController? fPhaseController;
        private static readonly Duration ThemePreviewTransitionDuration = new(TimeSpan.FromMilliseconds(140));

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                var champSelectSettings = ChampSelectSettings.Load();
                var overlaySettings = OverlaySettings.Load();
                champSelectSettings.ThemeKey = AppThemeManager.NormalizeThemeKey(champSelectSettings.ThemeKey);
                AppThemeManager.ApplyTheme(champSelectSettings.ThemeKey);

                ChampionTileSeedCacheResult? bundledTileSeedResult = null;
                string? bundledTileSeedError = null;
                try
                {
                    bundledTileSeedResult = ChampionTileCatalog.InstallBundledSeedCacheIfNeeded();
                }
                catch (Exception ex)
                {
                    bundledTileSeedError = FormatException(ex);
                }

                fMainWindow = CreateMainWindow(champSelectSettings, overlaySettings);
                MainWindow = fMainWindow;
                fMainWindow.Show();
                LogBundledChampionTileSeedResult(bundledTileSeedResult, bundledTileSeedError);

                if (overlaySettings.PickBanOverlayOpenOnStartup)
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

        private void LogBundledChampionTileSeedResult(ChampionTileSeedCacheResult? result, string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                fLogsPage?.WriteErrorLine($"Bundled champion picture cache install failed. Existing local picture cache was kept. {error}");
                return;
            }

            if (result?.Installed == true)
            {
                fLogsPage?.WriteLine($"Bundled champion picture cache installed from release assets. Version: {FormatDataDragonVersion(result.DataDragonVersion)}; copied {result.ImportedCount} jpg files to {result.CacheDirectory}.");
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            fPhaseController?.Dispose();
            fPhaseController = null;
        }

        private MainWindow CreateMainWindow(
            ChampSelectSettings champSelectSettings,
            OverlaySettings overlaySettings,
            int activeTabIndex = 0,
            string? selectedThemeKey = null,
            bool themePickerExpanded = false)
        {
            fDashboardPage = new PhaseProgressionPage(champSelectSettings);
            var logsPage = new LogsPage();
            fLogsPage = logsPage;
            fDashboardPage.SetLogsPage(logsPage);

            fPhaseController = new PhaseController(fDashboardPage, logsPage, champSelectSettings);

            var championPrioritiesPage = new ChampionPrioritiesPage(champSelectSettings);
            var settingsPage = new SettingsPage(
                champSelectSettings,
                overlaySettings,
                ReloadUiForTheme,
                logsPage.WriteLine,
                logsPage.WriteErrorLine,
                selectedThemeKey,
                themePickerExpanded);

            var mainWindow = new MainWindow(fDashboardPage, logsPage, championPrioritiesPage, settingsPage, champSelectSettings, overlaySettings);
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
            bool championPicturesNeedUpdate = tileSyncInfo.CachedTileCount <= 0
                || !IsDataDragonVersionCurrent(tileSyncInfo.DataDragonVersion, latestDataDragonVersion);
            if (championPicturesNeedUpdate)
            {
                try
                {
                    fLogsPage?.WriteLine($"Champion picture update available. Local version: {FormatDataDragonVersion(tileSyncInfo.DataDragonVersion)}; cached files: {tileSyncInfo.CachedTileCount}; latest version: {latestDataDragonVersion}. Installing the archive because the version changed or the picture cache is empty.");
                    var result = await ChampionTileCatalog.InstallDataDragonArchiveAsync(
                        latestDataDragonVersion,
                        CreateChampionTileArchiveLogProgress(),
                        optimizeForLocalCache: !champSelectSettings.DownloadRawChampionPictures);
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

        private void ReloadUiForTheme(
            ChampSelectSettings champSelectSettings,
            OverlaySettings overlaySettings,
            string? themeKey,
            bool themePickerExpanded)
        {
            try
            {
                bool restartWatcher = fPhaseController?.IsRunning == true;
                int activeTabIndex = fMainWindow?.ActiveTabIndex ?? 0;
                var previousWindow = fMainWindow;

                fPhaseController?.Stop();

                string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey ?? champSelectSettings.ThemeKey);
                AppThemeManager.ApplyTheme(normalizedThemeKey);

                var newWindow = CreateMainWindow(
                    champSelectSettings,
                    overlaySettings,
                    activeTabIndex,
                    normalizedThemeKey,
                    themePickerExpanded);
                if (previousWindow is not null)
                    CopyWindowPlacement(previousWindow, newWindow);

                fMainWindow = newWindow;
                MainWindow = newWindow;
                ShowThemedWindow(newWindow, previousWindow, restartWatcher);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Theme Reload Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowThemedWindow(MainWindow newWindow, Window? previousWindow, bool restartWatcher)
        {
            if (previousWindow is null)
            {
                newWindow.Show();
                if (restartWatcher)
                    fPhaseController?.Start();
                return;
            }

            newWindow.Opacity = 0;
            newWindow.Show();
            newWindow.Activate();

            var fadeIn = new DoubleAnimation(0, 1, ThemePreviewTransitionDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            fadeIn.Completed += (_, _) =>
            {
                newWindow.BeginAnimation(Window.OpacityProperty, null);
                newWindow.Opacity = 1;
                previousWindow.Close();

                if (restartWatcher)
                    fPhaseController?.Start();
            };

            newWindow.BeginAnimation(Window.OpacityProperty, fadeIn);
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
