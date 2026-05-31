using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void AutoUpdateChampionCatalogOnStartupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingAutomationControls)
                return;

            if (ConfirmChampionCatalogAutoUpdate())
                return;

            _isUpdatingAutomationControls = true;
            try
            {
                AutoUpdateChampionCatalogOnStartupCheckBox.IsChecked = false;
            }
            finally
            {
                _isUpdatingAutomationControls = false;
            }
        }

        private async void RefreshChampionCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmChampionCatalogRefresh())
            {
                SetChampionCatalogRefreshStatus("Update canceled.", "TextSoftBrush", Brushes.SlateGray);
                return;
            }

            RefreshChampionCatalogButton.IsEnabled = false;
            SetChampionCatalogRefreshStatus("Updating champion list from Riot Data Dragon...", "TextSoftBrush", Brushes.SlateGray);

            try
            {
                var result = await ChampionCatalog.RefreshFromDataDragonAsync(_championCatalogRemoteService);
                RefreshChampionCatalogSyncStatus(result);
                SetChampionCatalogRefreshStatus(
                    "Champion list updated.",
                    "AccentGreenTextBrush",
                    Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetChampionCatalogRefreshStatus(
                    $"Champion list update failed. Existing local file was kept. {ex.Message}",
                    "DangerTextBrush",
                    Brushes.IndianRed);
            }
            finally
            {
                RefreshChampionCatalogButton.IsEnabled = true;
            }
        }

        private bool ConfirmChampionCatalogRefresh()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "This will use your internet connection to contact Riot Data Dragon at ddragon.leagueoflegends.com.\n\nThe app downloads Riot's public champion version and champion-name data, then updates only your local champions.json file.\n\nContinue?",
                "Update Champion List",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private bool ConfirmChampionCatalogAutoUpdate()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Check champion list updates on app startup?\n\nThis helps JoinGameAfk notice new champions automatically. At app startup, it checks Riot Data Dragon version and downloads the champion-name list when your local list is out of date.\n\nChampion pictures are not downloaded automatically. Use the manual picture options if you want to add or refresh images.",
                "Allow Startup Champion List Update Check",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private bool ConfirmChampionPictureRefresh()
        {
            string cacheModeText = IsRawChampionPictureDownloadSelected()
                ? "Raw picture mode is enabled, so extracted jpg files stay as Riot's original files."
                : "Compact picture mode is enabled, so extracted jpg files are resized to 96px-wide cache copies at maximum JPEG quality.";

            var result = MessageBox.Show(
                Window.GetWindow(this),
                $"This will download the latest Riot Data Dragon dragontail archive into local app storage. The archive can be very large.\n\nThis is not recommended for normal picture changes. Use it only if you really want to refresh the entire local champion image cache.\n\nAfter the download, JoinGameAfk extracts champion tile jpg files into the picture cache, then deletes the archive so only the champion tiles remain on disk.\n\n{cacheModeText}\n\nContinue?",
                "Download Data Dragon Archive",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private void RefreshChampionCatalogSyncStatus(ChampionCatalogRefreshResult? refreshResult = null)
        {
            string? dataDragonVersion = refreshResult?.DataDragonVersion;
            int championCount = refreshResult?.ChampionCount ?? 0;
            DateTime? lastSyncedAtUtc = refreshResult?.LastSyncedAtUtc;

            if (refreshResult is null)
            {
                var syncInfo = ChampionCatalog.GetLocalSyncInfo();
                dataDragonVersion = syncInfo.DataDragonVersion;
                championCount = syncInfo.ChampionCount;
                lastSyncedAtUtc = syncInfo.LastSyncedAtUtc;
            }

            if (string.IsNullOrWhiteSpace(dataDragonVersion))
            {
                SetChampionCatalogSyncStatus(
                    "Champion list has never been synced with Riot Data Dragon.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            SetChampionCatalogSyncStatus(
                $"Synced with Riot Data Dragon {dataDragonVersion} ({championCount} champions). Last sync: {FormatLastSyncedAt(lastSyncedAtUtc)}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private static string FormatLastSyncedAt(DateTime? lastSyncedAtUtc)
        {
            if (lastSyncedAtUtc is null)
                return "unknown";

            return DateTime.SpecifyKind(lastSyncedAtUtc.Value, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("g");
        }

        private static string FormatByteCount(long bytes)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        private void SetChampionCatalogSyncStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionCatalogSyncStatusTextBlock.Text = message;
            ChampionCatalogSyncStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
        }

        private void SetChampionCatalogRefreshStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionCatalogRefreshStatusLabel.Text = message;
            ChampionCatalogRefreshStatusLabel.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            ChampionCatalogRefreshStatusLabel.Visibility = Visibility.Visible;
        }

        private void RefreshChampionPictureCacheStatus(ChampionTileArchiveInstallResult? refreshResult = null)
        {
            if (refreshResult is not null)
            {
                string archiveCleanupText = refreshResult.ArchiveDeleted
                    ? "then removed the archive"
                    : $"but archive cleanup failed ({refreshResult.ArchiveDeleteError})";

                SetChampionPictureCacheStatus(
                    $"Picture cache synced with Riot Data Dragon {refreshResult.DataDragonVersion}. Downloaded {FormatByteCount(refreshResult.ArchiveSizeBytes)} archive, checked {refreshResult.CheckedTileCount} champion tiles, updated {refreshResult.UpdatedTileCount}, unchanged {refreshResult.UnchangedTileCount}, {archiveCleanupText}. Local folder currently has {refreshResult.CachedTileCount} jpg files. Last sync: {FormatLastSyncedAt(refreshResult.LastSyncedAtUtc)}.",
                    refreshResult.ArchiveDeleted ? "TextSoftBrush" : "DangerTextBrush",
                    refreshResult.ArchiveDeleted ? Brushes.SlateGray : Brushes.IndianRed);
                return;
            }

            var syncInfo = ChampionTileCatalog.GetCacheSyncInfo();
            int fileCount = ChampionTileCatalog.GetTileFileCount();
            if (string.IsNullOrWhiteSpace(syncInfo.DataDragonVersion))
            {
                SetChampionPictureCacheStatus(
                    $"Local picture cache has {fileCount} jpg files. It has not been synced with Riot Data Dragon yet.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            if (fileCount <= 0)
            {
                SetChampionPictureCacheStatus(
                    $"Picture cache has Riot Data Dragon {syncInfo.DataDragonVersion} recorded, but no champion tile jpg files were found. Use Download Archive to restore champion pictures.",
                    "DangerTextBrush",
                    Brushes.IndianRed);
                return;
            }

            if (!string.IsNullOrWhiteSpace(syncInfo.ArchiveFilePath))
            {
                SetChampionPictureCacheStatus(
                    $"Picture cache synced with Riot Data Dragon {syncInfo.DataDragonVersion}. Local folder currently has {fileCount} jpg files. Archive cleanup did not complete; {Path.GetFileName(syncInfo.ArchiveFilePath)} ({FormatByteCount(syncInfo.ArchiveSizeBytes)}) is still in local storage. Last sync: {FormatLastSyncedAt(syncInfo.LastSyncedAtUtc)}.",
                    "DangerTextBrush",
                    Brushes.IndianRed);
                return;
            }

            SetChampionPictureCacheStatus(
                $"Picture cache synced with Riot Data Dragon {syncInfo.DataDragonVersion}. Local folder currently has {fileCount} jpg files. Archive files are removed after extraction. Last sync: {FormatLastSyncedAt(syncInfo.LastSyncedAtUtc)}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private void SetChampionPictureCacheStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionPictureCacheStatusTextBlock.Text = message;
            ChampionPictureCacheStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
        }

        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshChampionCatalogSyncStatus();
            });
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshChampionPictureCacheStatus();
            });
        }

        private void OpenChampionPictureFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppStorage.EnsureChampionTileDirectoryExists();
                Process.Start(new ProcessStartInfo
                {
                    FileName = ChampionTileCatalog.TileDirectoryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open champion pictures folder: {ex.Message}", "Open Folder Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReloadChampionPicturesButton_Click(object sender, RoutedEventArgs e)
        {
            AppStorage.EnsureChampionTileDirectoryExists();
            ChampionTileCatalog.Reload();
            RefreshChampionPictureCacheStatus();
            ShowStatusMessage($"Champion pictures reloaded from local storage ({ChampionTileCatalog.GetTileFileCount()} files).", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private async void DownloadChampionPictureArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmChampionPictureRefresh())
            {
                ChampionPictureDownloadStatusLabel.Text = "Data Dragon archive download canceled.";
                ChampionPictureDownloadStatusLabel.Visibility = Visibility.Visible;
                LogMessage("Champion picture archive download canceled by user.");
                return;
            }

            LogMessage("Manual champion picture archive install started.");
            SetChampionPictureDownloadControlsEnabled(false);
            ChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            ChampionPictureDownloadProgressBar.IsIndeterminate = true;
            ChampionPictureDownloadProgressBar.Value = 0;
            ChampionPictureDownloadStatusLabel.Visibility = Visibility.Visible;
            ChampionPictureDownloadStatusLabel.Foreground = TryFindResource("TextSoftBrush") as Brush ?? Brushes.SlateGray;
            ChampionPictureDownloadStatusLabel.Text = "Preparing Riot Data Dragon archive download...";

            try
            {
                string? lastLoggedArchiveProgressMessage = null;
                var progress = new Progress<ChampionTileArchiveProgress>(snapshot =>
                {
                    ChampionPictureDownloadStatusLabel.Text = snapshot.Message;
                    if (ShouldLogChampionTileArchiveProgress(snapshot.Message)
                        && !string.Equals(snapshot.Message, lastLoggedArchiveProgressMessage, StringComparison.Ordinal))
                    {
                        lastLoggedArchiveProgressMessage = snapshot.Message;
                        if (IsChampionTileArchiveWarning(snapshot.Message))
                            LogErrorMessage(snapshot.Message);
                        else
                            LogMessage(snapshot.Message);
                    }

                    if (snapshot.TotalBytes is long totalBytes && totalBytes > 0)
                    {
                        ChampionPictureDownloadProgressBar.IsIndeterminate = false;
                        ChampionPictureDownloadProgressBar.Maximum = totalBytes;
                        ChampionPictureDownloadProgressBar.Value = Math.Min(snapshot.BytesCompleted, totalBytes);
                    }
                    else
                    {
                        ChampionPictureDownloadProgressBar.IsIndeterminate = true;
                    }
                });

                var result = await ChampionTileCatalog.InstallLatestDataDragonArchiveAsync(
                    progress,
                    optimizeForLocalCache: !IsRawChampionPictureDownloadSelected());
                RefreshChampionPictureCacheStatus(result);
                ChampionPictureDownloadStatusLabel.Foreground = result.ArchiveDeleted
                    ? TryFindResource("AccentGreenTextBrush") as Brush ?? Brushes.ForestGreen
                    : TryFindResource("DangerTextBrush") as Brush ?? Brushes.IndianRed;
                string archiveCleanupText = result.ArchiveDeleted
                    ? "then removed the archive"
                    : $"but could not remove the archive ({result.ArchiveDeleteError})";
                string pictureModeText = IsRawChampionPictureDownloadSelected()
                    ? "kept raw originals"
                    : "stored compact resized copies";
                ChampionPictureDownloadStatusLabel.Text =
                    $"Data Dragon archive {result.DataDragonVersion} installed. Downloaded {FormatByteCount(result.ArchiveSizeBytes)}, checked {result.CheckedTileCount} champion tiles, updated {result.UpdatedTileCount}, unchanged {result.UnchangedTileCount}, {archiveCleanupText}, {pictureModeText}. Cache now has {result.CachedTileCount} jpg files.";
                LogMessage($"Manual champion picture archive install completed for Riot Data Dragon {result.DataDragonVersion}. Downloaded {FormatByteCount(result.ArchiveSizeBytes)}; checked {result.CheckedTileCount} champion tiles; updated {result.UpdatedTileCount}; unchanged {result.UnchangedTileCount}; {pictureModeText}; cache now has {result.CachedTileCount} jpg files.");
                if (!result.ArchiveDeleted)
                    LogErrorMessage($"Champion picture archive cleanup failed after successful extraction. {result.ArchiveDeleteError}");
            }
            catch (Exception ex)
            {
                ChampionPictureDownloadStatusLabel.Foreground = TryFindResource("DangerTextBrush") as Brush ?? Brushes.IndianRed;
                ChampionPictureDownloadStatusLabel.Text = $"Data Dragon archive install failed. Existing cache was kept. {ex.Message}";
                LogErrorMessage($"Manual champion picture archive install failed. Existing cache was kept. {FormatException(ex)}");
            }
            finally
            {
                ChampionPictureDownloadProgressBar.IsIndeterminate = false;
                SetChampionPictureDownloadControlsEnabled(true);
            }
        }

        private void SetChampionPictureDownloadControlsEnabled(bool enabled)
        {
            DownloadChampionPictureArchiveButton.IsEnabled = enabled;
            ReloadChampionPicturesButton.IsEnabled = enabled;
            OpenChampionPictureFolderButton.IsEnabled = enabled;
            DownloadRawChampionPicturesCheckBox.IsEnabled = enabled;
        }

        private bool IsRawChampionPictureDownloadSelected()
        {
            return DownloadRawChampionPicturesCheckBox.IsChecked == true;
        }

        private void LogMessage(string message)
        {
            _logMessage?.Invoke(message);
        }

        private void LogErrorMessage(string message)
        {
            _logErrorMessage?.Invoke(message);
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
    }
}
