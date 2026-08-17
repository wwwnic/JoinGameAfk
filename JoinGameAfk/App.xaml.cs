using System.Windows;
using System.Windows.Media.Animation;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Presentation.Controller;
using JoinGameAfk.Presentation.View;
using JoinGameAfk.Presentation.View.ChampionPriorities;
using JoinGameAfk.Presentation.View.Dashboard;
using JoinGameAfk.Presentation.View.Settings;
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
        private GeneralSettings? fGeneralSettings;
        private ChampionDataSettings? fChampionDataSettings;
        private SoundSettings? fSoundSettings;
        private RolePlanSettings? fRolePlanSettings;
        private OverlaySettings? fOverlaySettings;
        private readonly LeagueClientConnectionContext fLeagueClientConnection = new();
        private ChampionCatalogSyncCoordinator? fChampionCatalogSyncCoordinator;
        private static readonly Duration ThemePreviewTransitionDuration = new(TimeSpan.FromMilliseconds(140));

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                AppStorage.EnsureStorageLayoutExists();
                var generalSettings = GeneralSettings.Load();
                var championDataSettings = ChampionDataSettings.Load();
                var soundSettings = SoundSettings.Load();
                var rolePlanSettings = RolePlanSettings.Load();
                var overlaySettings = OverlaySettings.Load();
                generalSettings.ThemeKey = AppThemeManager.NormalizeThemeKey(generalSettings.ThemeKey);
                AppThemeManager.ApplyTheme(generalSettings.ThemeKey);

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

                fChampionCatalogSyncCoordinator = new ChampionCatalogSyncCoordinator(
                    championDataSettings,
                    fLeagueClientConnection,
                    message => fLogsPage?.WriteLine(message));
                fLeagueClientConnection.Connected += LeagueClientConnection_Connected;
                fMainWindow = CreateMainWindow(generalSettings, championDataSettings, soundSettings, rolePlanSettings, overlaySettings);
                MainWindow = fMainWindow;
                fMainWindow.Show();
                ShowUsageNoticeIfNeeded();
                LogBundledChampionTileSeedResult(bundledTileSeedResult, bundledTileSeedError);

                if (overlaySettings.PickBanOverlayOpenOnStartup)
                    fMainWindow.ShowPickBanOverlayOnStartup();

                if (generalSettings.StartWatcherOnStartup)
                    fPhaseController?.Start();

                LogChampionTileArchiveCleanup(ChampionTileCatalog.DeleteDownloadedArchives());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "An error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private static void ShowUsageNoticeIfNeeded()
        {
            string appVersion = ReleaseUsageNoticeWindow.GetCurrentAppVersion();
            if (AppStorage.HasAcknowledgedUsageNoticeForVersion(appVersion))
                return;

            var notice = new ReleaseUsageNoticeWindow(appVersion)
            {
                Owner = Current.MainWindow
            };
            notice.ShowDialog();
            AppStorage.TryAcknowledgeUsageNoticeForVersion(appVersion);
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
            fLeagueClientConnection.Connected -= LeagueClientConnection_Connected;
            fPhaseController?.Dispose();
            fPhaseController = null;
        }

        private MainWindow CreateMainWindow(
            GeneralSettings generalSettings,
            ChampionDataSettings championDataSettings,
            SoundSettings soundSettings,
            RolePlanSettings rolePlanSettings,
            OverlaySettings overlaySettings,
            int activeTabIndex = 0,
            string? selectedThemeKey = null,
            bool themePickerExpanded = false)
        {
            fGeneralSettings = generalSettings;
            fChampionDataSettings = championDataSettings;
            fSoundSettings = soundSettings;
            fRolePlanSettings = rolePlanSettings;
            fOverlaySettings = overlaySettings;

            fDashboardPage = new PhaseProgressionPage(rolePlanSettings);
            var logsPage = new LogsPage();
            fLogsPage = logsPage;
            fDashboardPage.SetLogsPage(logsPage);

            fPhaseController = new PhaseController(
                fDashboardPage,
                logsPage,
                generalSettings,
                championDataSettings,
                rolePlanSettings,
                soundSettings,
                fLeagueClientConnection);

            var championPrioritiesPage = new ChampionPrioritiesPage(
                championDataSettings,
                rolePlanSettings,
                fLeagueClientConnection,
                fChampionCatalogSyncCoordinator
                    ?? throw new InvalidOperationException("Champion catalog synchronization is not initialized."),
                logsPage.WriteLine,
                logsPage.WriteErrorLine);
            var settingsPage = new SettingsPage(
                generalSettings,
                championDataSettings,
                soundSettings,
                overlaySettings,
                ReloadUiForTheme,
                selectedThemeKey,
                themePickerExpanded);

            var mainWindow = new MainWindow(fDashboardPage, logsPage, championPrioritiesPage, settingsPage, generalSettings, overlaySettings);
            settingsPage.OpenChampionDataRequested += mainWindow.OpenChampionDataManager;
            mainWindow.SetController(fPhaseController);
            fDashboardPage.PhaseChanged += mainWindow.UpdatePhaseIndicator;
            fDashboardPage.WatcherStateChanged += mainWindow.SetWatcherState;
            fDashboardPage.ClientConnectionChanged += mainWindow.SetClientConnection;
            fDashboardPage.ChampSelectSubPhaseChanged += mainWindow.UpdateChampSelectSubPhase;
            if (ChampionDataSourcePolicy.Resolve(championDataSettings.SourceMode)
                == ChampionDataSourceMode.LeagueClient)
            {
                fDashboardPage.UpdateRegionDisplay(null, null);
            }
            else
            {
                fDashboardPage.UpdateRegionDisplay(
                    championDataSettings.PlatformId,
                    championDataSettings.Locale);
            }
            mainWindow.UpdatePhaseIndicator(ClientPhase.Unknown);
            mainWindow.SetWatcherState(false);
            mainWindow.SetClientConnection(false);
            mainWindow.UpdateChampSelectSubPhase(string.Empty);
            mainWindow.ActivateTab(activeTabIndex);

            return mainWindow;
        }

        private async void LeagueClientConnection_Connected(object? sender, EventArgs e)
        {
            ChampionCatalogSyncCoordinator? coordinator = fChampionCatalogSyncCoordinator;
            if (coordinator is null)
                return;

            try
            {
                LeagueClientChampionCatalogAutoSyncResult? syncResult =
                    await coordinator.RefreshLeagueClientIfDueAsync();
                if (syncResult is null)
                    return;

                if (syncResult.Refreshed && syncResult.RefreshResult is { } refreshResult)
                {
                    fLogsPage?.WriteLine(
                        $"Automatic local champion-list sync completed in League Client language {refreshResult.Locale} "
                        + $"({refreshResult.ChampionCount} champions). Next check is allowed in 12 hours.");
                }
                else if (syncResult.NextRefreshAtUtc is DateTime nextRefreshAtUtc)
                {
                    fLogsPage?.WriteLine(
                        $"Skipped automatic League Client champion-list sync; the local list was checked within the last 12 hours. Next check: {nextRefreshAtUtc.ToLocalTime():g}.");
                }
            }
            catch (Exception ex)
            {
                fLogsPage?.WriteErrorLine(
                    "Automatic League Client champion-list sync failed. Existing local champion data was kept, and Data Dragon was not used as a fallback. "
                    + FormatException(ex));
            }
        }

        private static string FormatDataDragonVersion(string? dataDragonVersion)
        {
            return string.IsNullOrWhiteSpace(dataDragonVersion)
                ? "none"
                : dataDragonVersion.Trim();
        }


        private void LogChampionTileArchiveCleanup(ChampionTileArchiveCleanupResult cleanupResult)
        {
            if (cleanupResult.DeletedFileCount > 0)
                fLogsPage?.WriteLine($"Removed {cleanupResult.DeletedFileCount} stale Data Dragon archive file(s) from local storage.");

            foreach (var failure in cleanupResult.Failures)
                fLogsPage?.WriteErrorLine($"Unable to remove stale Data Dragon archive file '{failure.FilePath}'. {failure.ErrorMessage}");
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }

        private void ReloadUiForTheme(
            GeneralSettings generalSettings,
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

                string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey ?? generalSettings.ThemeKey);
                AppThemeManager.ApplyTheme(normalizedThemeKey);

                var newWindow = CreateMainWindow(
                    generalSettings,
                    fChampionDataSettings ?? ChampionDataSettings.Load(),
                    fSoundSettings ?? SoundSettings.Load(),
                    fRolePlanSettings ?? RolePlanSettings.Load(),
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
