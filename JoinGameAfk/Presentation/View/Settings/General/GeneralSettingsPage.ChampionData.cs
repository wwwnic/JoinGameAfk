using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using JoinGameAfk.Constant;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void ApplyChampionDataSettingsToControls()
        {
            _isApplyingChampionDataSettingsToControls = true;
            try
            {
                DownloadRawChampionPicturesCheckBox.IsChecked = _championDataSettings.DownloadRawChampionPictures;
            }
            finally
            {
                _isApplyingChampionDataSettingsToControls = false;
            }
        }

        private void DownloadRawChampionPicturesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingChampionDataSettingsToControls)
                return;

            bool previousValue = _championDataSettings.DownloadRawChampionPictures;
            _championDataSettings.DownloadRawChampionPictures = DownloadRawChampionPicturesCheckBox.IsChecked == true;

            try
            {
                _championDataSettings.Save();
                ShowStatusMessage("Champion picture setting saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                _championDataSettings.DownloadRawChampionPictures = previousValue;
                ApplyChampionDataSettingsToControls();
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Unable to save champion picture setting: {ex.Message}",
                    "Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Unable to open champion pictures folder: {ex.Message}",
                    "Open Folder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ReloadChampionPicturesButton_Click(object sender, RoutedEventArgs e)
        {
            AppStorage.EnsureChampionTileDirectoryExists();
            ChampionTileCatalog.Reload();
            RefreshChampionPictureCacheStatus();
            ShowStatusMessage(
                $"Champion pictures reloaded from local storage ({ChampionTileCatalog.GetTileFileCount()} files).",
                "AccentGreenTextBrush",
                Brushes.ForestGreen);
        }

        private void RefreshChampionPictureCacheStatus()
        {
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
                    $"Picture cache has Riot Data Dragon {syncInfo.DataDragonVersion} recorded, but no champion tile jpg files were found. Use Download Images in Role Plans to restore champion pictures.",
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
    }
}
