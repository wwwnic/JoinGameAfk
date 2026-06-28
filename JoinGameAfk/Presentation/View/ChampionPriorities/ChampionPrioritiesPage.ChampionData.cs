using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private static readonly IReadOnlyList<RegionLocaleSuggestion> PlatformSuggestions =
        [
            new(RegionLocale.DefaultPlatformId, "Global (latest)"),
            new("NA1", "North America"),
            new("BR1", "Brazil"),
            new("EUN1", "Europe Nordic and East"),
            new("EUW1", "Europe West"),
            new("JP1", "Japan"),
            new("LA1", "Latin America North"),
            new("LA2", "Latin America South"),
            new("ME1", "Middle East"),
            new("OC1", "Oceania"),
            new("PH2", "Philippines"),
            new("RU", "Russia"),
            new("SG2", "Singapore"),
            new("TH2", "Thailand"),
            new("TR1", "Turkey"),
            new("TW2", "Taiwan"),
            new("VN2", "Vietnam"),
            new("PBE1", "Public Beta Environment")
        ];

        private static readonly IReadOnlyList<RegionLocaleSuggestion> LocaleSuggestions =
        [
            new("en_US", "English (United States)"),
            new("en_GB", "English (United Kingdom)"),
            new("en_AU", "English (Australia)"),
            new("en_PH", "English (Philippines)"),
            new("en_SG", "English (Singapore)"),
            new("pt_BR", "Portuguese (Brazil)"),
            new("es_MX", "Spanish (Latin America)"),
            new("es_AR", "Spanish (Argentina)"),
            new("es_ES", "Spanish (Spain)"),
            new("fr_FR", "French"),
            new("de_DE", "German"),
            new("it_IT", "Italian"),
            new("pl_PL", "Polish"),
            new("ro_RO", "Romanian"),
            new("cs_CZ", "Czech"),
            new("el_GR", "Greek"),
            new("hu_HU", "Hungarian"),
            new("tr_TR", "Turkish"),
            new("ar_AE", "Arabic"),
            new("ru_RU", "Russian"),
            new("ja_JP", "Japanese"),
            new("ko_KR", "Korean"),
            new("zh_CN", "Chinese (Simplified)"),
            new("zh_TW", "Chinese (Traditional)"),
            new("zh_MY", "Chinese (Malaysia)"),
            new("id_ID", "Indonesian"),
            new("th_TH", "Thai"),
            new("vi_VN", "Vietnamese")
        ];

        public void OpenChampionDataManager()
        {
            if (ChampionPicturePickerOverlay.Visibility == Visibility.Visible || IsChampionPictureEditMode)
                SetChampionPictureEditMode(false);

            ApplyChampionDataSettingsToControls();
            RefreshChampionCatalogSyncStatus();
            RefreshChampionDataActionStates();
            ChampionDataOverlay.Visibility = Visibility.Visible;
            ChampionDataCloseButton.Focus();
            _ = RefreshChampionCatalogVersionWarningAsync();
        }

        private void OpenChampionDataButton_Click(object sender, RoutedEventArgs e)
        {
            OpenChampionDataManager();
            e.Handled = true;
        }

        private void CloseChampionDataManager()
        {
            ChampionDataOverlay.Visibility = Visibility.Collapsed;
            FocusPriorityPage();
        }

        private void ChampionDataCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseChampionDataManager();
            e.Handled = true;
        }

        private void ChampionDataOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, ChampionDataOverlay))
                return;

            CloseChampionDataManager();
            e.Handled = true;
        }

        private void ChampionDataSettings_Saved()
        {
            Dispatcher.InvokeAsync(() =>
            {
                ApplyChampionDataSettingsToControls();
                RefreshChampionDataActionStates();
                if (ChampionDataOverlay.Visibility == Visibility.Visible)
                    _ = RefreshChampionCatalogVersionWarningAsync();
            });
        }

        private void ApplyChampionDataSettingsToControls()
        {
            _isApplyingChampionDataSettings = true;
            try
            {
                EnsureConfiguredOption(_platformOptions, _championDataSettings.PlatformId);
                EnsureConfiguredOption(_localeOptions, _championDataSettings.Locale);
                ChampionDataPlatformIdBox.SelectedValue = _championDataSettings.PlatformId;
                ChampionDataLocaleBox.SelectedValue = _championDataSettings.Locale;
                ChampionDataAutoUpdateCatalogCheckBox.IsChecked = _championDataSettings.AutoUpdateChampionCatalogOnStartup;
                ChampionDataDownloadNewChampionPicturesCheckBox.IsChecked =
                    _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate;
            }
            finally
            {
                _isApplyingChampionDataSettings = false;
            }
        }

        private void ChampionDataSettingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingChampionDataSettings)
                return;

            if (ReferenceEquals(sender, ChampionDataAutoUpdateCatalogCheckBox)
                && ChampionDataAutoUpdateCatalogCheckBox.IsChecked == true
                && !ConfirmChampionCatalogAutoUpdate())
            {
                _isApplyingChampionDataSettings = true;
                try
                {
                    ChampionDataAutoUpdateCatalogCheckBox.IsChecked = false;
                }
                finally
                {
                    _isApplyingChampionDataSettings = false;
                }

                return;
            }

            SaveChampionDataSettingsFromControls();
        }

        private void ChampionDataRegionLocale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingChampionDataSettings)
                return;

            SaveChampionDataSettingsFromControls();
        }

        private void SaveChampionDataSettingsFromControls()
        {
            string platformId = ChampionDataPlatformIdBox.SelectedValue?.ToString() ?? string.Empty;
            string locale = ChampionDataLocaleBox.SelectedValue?.ToString() ?? string.Empty;
            if (!RegionLocale.TryNormalizePlatformId(platformId, out string normalizedPlatformId)
                || !RegionLocale.TryNormalizeLocale(locale, out string normalizedLocale))
            {
                ApplyChampionDataSettingsToControls();
                return;
            }

            string previousPlatformId = _championDataSettings.PlatformId;
            string previousLocale = _championDataSettings.Locale;
            bool previousAutoUpdate = _championDataSettings.AutoUpdateChampionCatalogOnStartup;
            bool previousDownloadNewChampionPictures = _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate;

            _championDataSettings.PlatformId = normalizedPlatformId;
            _championDataSettings.Locale = normalizedLocale;
            _championDataSettings.AutoUpdateChampionCatalogOnStartup = ChampionDataAutoUpdateCatalogCheckBox.IsChecked == true;
            _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate =
                ChampionDataDownloadNewChampionPicturesCheckBox.IsChecked == true;

            try
            {
                _championDataSettings.Save();
            }
            catch (Exception ex)
            {
                _championDataSettings.PlatformId = previousPlatformId;
                _championDataSettings.Locale = previousLocale;
                _championDataSettings.AutoUpdateChampionCatalogOnStartup = previousAutoUpdate;
                _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate = previousDownloadNewChampionPictures;
                ApplyChampionDataSettingsToControls();
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Unable to save champion data settings: {ex.Message}",
                    "Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void EnsureConfiguredOption(
            ICollection<RegionLocaleSuggestion> options,
            string configuredCode)
        {
            if (options.Any(option => string.Equals(option.Code, configuredCode, StringComparison.OrdinalIgnoreCase)))
                return;

            options.Add(new RegionLocaleSuggestion(configuredCode, "Custom value from settings file"));
        }

        private async void RefreshChampionCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionDataOperationInProgress || _isChampionPictureDownloadInProgress)
                return;

            if (!ConfirmChampionCatalogRefresh())
            {
                SetChampionCatalogRefreshStatus("Update canceled.", "TextSoftBrush", Brushes.SlateGray);
                return;
            }

            SetChampionDataOperationInProgress(true);
            SetChampionCatalogRefreshStatus("Updating champion list from Riot Data Dragon...", "TextSoftBrush", Brushes.SlateGray);
            SetChampionPictureDownloadStatus(string.Empty, "TextSoftBrush", Brushes.SlateGray);
            ChampionPictureDownloadProgressBar.Visibility = Visibility.Collapsed;

            try
            {
                var existingChampionKeys = ChampionCatalog.All
                    .Select(champion => champion.Key)
                    .ToHashSet();
                var remoteCatalog = await _championCatalogRemoteService.FetchLatestChampionCatalogAsync();
                var newChampionKeys = remoteCatalog.Champions
                    .Where(champion => champion.Key > 0 && !existingChampionKeys.Contains(champion.Key))
                    .Select(champion => champion.Key)
                    .Distinct()
                    .OrderBy(championKey => championKey)
                    .ToList();
                var result = ChampionCatalog.RefreshFromDataDragon(remoteCatalog);
                _checkedChampionCatalogPlatformId = _championDataSettings.PlatformId;
                _latestConfiguredRegionDataDragonVersion = result.DataDragonVersion;
                RefreshChampionCatalogSyncStatus(result);
                UpdateChampionCatalogVersionWarning();
                SetChampionCatalogRefreshStatus(
                    CreateChampionCatalogRefreshStatusMessage(newChampionKeys),
                    "AccentGreenTextBrush",
                    Brushes.ForestGreen);

                if (_championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate
                    && newChampionKeys.Count > 0)
                {
                    var pictureResult = await DownloadNewChampionPicturesAsync(newChampionKeys);
                    SetChampionPictureDownloadStatus(
                        CreateNewChampionPictureDownloadStatusMessage(pictureResult),
                        pictureResult.FailedChampionCount == 0 && pictureResult.FailedTileCount == 0
                            ? "AccentGreenTextBrush"
                            : "TextSoftBrush",
                        pictureResult.FailedChampionCount == 0 && pictureResult.FailedTileCount == 0
                            ? Brushes.ForestGreen
                            : Brushes.SlateGray);
                }
                else if (newChampionKeys.Count > 0)
                {
                    SetChampionPictureDownloadStatus(
                        $"Detected {FormatChampionList(newChampionKeys)}. Champion picture download is off, so existing local pictures were kept.",
                        "TextSoftBrush",
                        Brushes.SlateGray);
                }
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
                SetChampionDataOperationInProgress(false);
            }
        }

        private async Task<NewChampionPictureDownloadResult> DownloadNewChampionPicturesAsync(
            IReadOnlyList<int> championKeys)
        {
            int downloadedTileCount = 0;
            int unchangedTileCount = 0;
            int failedTileCount = 0;
            int failedChampionCount = 0;

            ChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            ChampionPictureDownloadProgressBar.IsIndeterminate = false;
            ChampionPictureDownloadProgressBar.Minimum = 0;
            ChampionPictureDownloadProgressBar.Maximum = championKeys.Count;
            ChampionPictureDownloadProgressBar.Value = 0;

            for (int index = 0; index < championKeys.Count; index++)
            {
                int championKey = championKeys[index];
                if (!ChampionCatalog.TryGetByKey(championKey, out var champion) || champion is null)
                {
                    failedChampionCount++;
                    ChampionPictureDownloadProgressBar.Value = index + 1;
                    continue;
                }

                SetChampionPictureDownloadStatus(
                    $"Downloading pictures for new champion {champion.Name} ({index + 1}/{championKeys.Count})...",
                    "TextSoftBrush",
                    Brushes.SlateGray);

                var progress = new Progress<ChampionTileDownloadProgress>(snapshot =>
                {
                    if (snapshot.Message.StartsWith("Unable to download ", StringComparison.OrdinalIgnoreCase))
                        return;

                    SetChampionPictureDownloadStatus(
                        snapshot.Message,
                        "TextSoftBrush",
                        Brushes.SlateGray);
                });

                try
                {
                    var result = await ChampionTileCatalog.DownloadAllImagesForChampionAsync(
                        champion,
                        progress,
                        optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures,
                        preferredLocale: _championDataSettings.Locale);
                    downloadedTileCount += result.DownloadedTileCount;
                    unchangedTileCount += result.UnchangedTileCount;
                    failedTileCount += result.FailedTileCount;
                    LogMessage(
                        $"Downloaded pictures for new champion {result.ChampionName}. Downloaded {result.DownloadedTileCount}; unchanged {result.UnchangedTileCount}; failed {result.FailedTileCount}.");
                }
                catch (Exception ex)
                {
                    failedChampionCount++;
                    LogErrorMessage(
                        $"Unable to download pictures for new champion {champion.Name}. Existing local pictures were kept. {FormatException(ex)}");
                }
                finally
                {
                    ChampionPictureDownloadProgressBar.Value = index + 1;
                }
            }

            ChampionPictureDownloadProgressBar.IsIndeterminate = false;
            return new NewChampionPictureDownloadResult(
                championKeys.Count,
                downloadedTileCount,
                unchangedTileCount,
                failedTileCount,
                failedChampionCount);
        }

        private static string CreateChampionCatalogRefreshStatusMessage(IReadOnlyList<int> newChampionKeys)
        {
            if (newChampionKeys.Count == 0)
                return "Champion list updated. No new champions detected.";

            string championNoun = newChampionKeys.Count == 1 ? "new champion" : "new champions";
            return $"Champion list updated. Detected {newChampionKeys.Count} {championNoun}: {FormatChampionList(newChampionKeys)}.";
        }

        private static string CreateNewChampionPictureDownloadStatusMessage(NewChampionPictureDownloadResult result)
        {
            string championNoun = result.ChampionCount == 1 ? "new champion" : "new champions";
            if (result.FailedChampionCount == 0 && result.FailedTileCount == 0)
            {
                if (result.DownloadedTileCount == 0)
                    return $"Pictures for {result.ChampionCount} {championNoun} are already up to date. {result.UnchangedTileCount} local pictures checked.";

                return $"Downloaded {result.DownloadedTileCount} pictures for {result.ChampionCount} {championNoun}; {result.UnchangedTileCount} already up to date.";
            }

            return $"Finished picture downloads for {result.ChampionCount} {championNoun}. Downloaded {result.DownloadedTileCount}; unchanged {result.UnchangedTileCount}; failed tile requests {result.FailedTileCount}; failed champions {result.FailedChampionCount}.";
        }

        private static string FormatChampionList(IReadOnlyList<int> championKeys)
        {
            return string.Join(
                ", ",
                championKeys.Select(championKey =>
                    ChampionCatalog.TryGetByKey(championKey, out var champion) && champion is not null
                        ? champion.Name
                        : $"champion {championKey}"));
        }

        private async void DownloadChampionPictureArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionDataOperationInProgress || _isChampionPictureDownloadInProgress)
                return;

            if (!ConfirmChampionPictureRefresh())
            {
                SetChampionPictureDownloadStatus(
                    "Data Dragon archive download canceled.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                LogMessage("Champion picture archive download canceled by user.");
                return;
            }

            LogMessage("Manual champion picture archive install started.");
            SetChampionDataOperationInProgress(true);
            ChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            ChampionPictureDownloadProgressBar.IsIndeterminate = true;
            ChampionPictureDownloadProgressBar.Value = 0;
            SetChampionPictureDownloadStatus(
                "Preparing Riot Data Dragon archive download...",
                "TextSoftBrush",
                Brushes.SlateGray);

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

                string regionalDataDragonVersion =
                    await _championCatalogRemoteService.FetchLatestDataDragonVersionAsync();
                var result = await ChampionTileCatalog.InstallDataDragonArchiveAsync(
                    regionalDataDragonVersion,
                    progress,
                    optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures);
                string archiveCleanupText = result.ArchiveDeleted
                    ? "then removed the archive"
                    : $"but could not remove the archive ({result.ArchiveDeleteError})";
                string pictureModeText = _championDataSettings.DownloadRawChampionPictures
                    ? "kept raw originals"
                    : "stored compact resized copies";
                SetChampionPictureDownloadStatus(
                    $"Data Dragon archive {result.DataDragonVersion} installed. Downloaded {FormatByteCount(result.ArchiveSizeBytes)}, checked {result.CheckedTileCount} champion tiles, updated {result.UpdatedTileCount}, unchanged {result.UnchangedTileCount}, {archiveCleanupText}, {pictureModeText}. Cache now has {result.CachedTileCount} jpg files.",
                    result.ArchiveDeleted ? "AccentGreenTextBrush" : "DangerTextBrush",
                    result.ArchiveDeleted ? Brushes.ForestGreen : Brushes.IndianRed);
                LogMessage($"Manual champion picture archive install completed for Riot Data Dragon {result.DataDragonVersion}. Downloaded {FormatByteCount(result.ArchiveSizeBytes)}; checked {result.CheckedTileCount} champion tiles; updated {result.UpdatedTileCount}; unchanged {result.UnchangedTileCount}; {pictureModeText}; cache now has {result.CachedTileCount} jpg files.");
                if (!result.ArchiveDeleted)
                    LogErrorMessage($"Champion picture archive cleanup failed after successful extraction. {result.ArchiveDeleteError}");
            }
            catch (Exception ex)
            {
                SetChampionPictureDownloadStatus(
                    $"Data Dragon archive install failed. Existing cache was kept. {ex.Message}",
                    "DangerTextBrush",
                    Brushes.IndianRed);
                LogErrorMessage($"Manual champion picture archive install failed. Existing cache was kept. {FormatException(ex)}");
            }
            finally
            {
                ChampionPictureDownloadProgressBar.IsIndeterminate = false;
                SetChampionDataOperationInProgress(false);
            }
        }

        private void SetChampionDataOperationInProgress(bool isInProgress)
        {
            _isChampionDataOperationInProgress = isInProgress;
            RefreshChampionDataActionStates();
            RefreshChampionPicturePickerActionStates();
        }

        private void RefreshChampionDataActionStates()
        {
            bool enabled = !_isChampionDataOperationInProgress && !_isChampionPictureDownloadInProgress;
            ChampionDataConfigurationPanel.IsEnabled = enabled;
            ChampionDataAutoUpdateCatalogCheckBox.IsEnabled = enabled;
            ChampionDataDownloadNewChampionPicturesCheckBox.IsEnabled = enabled;
            RefreshChampionCatalogButton.IsEnabled = enabled;
            DownloadChampionPictureArchiveButton.IsEnabled = enabled;
        }

        private bool ConfirmChampionCatalogRefresh()
        {
            string pictureDownloadText = _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate
                ? "If Riot lists champion IDs that are not in your local list, JoinGameAfk will also download tile images for only those new champions."
                : "Champion pictures are not downloaded. Enable Download pictures for new champions first if you want new champion tiles fetched after the list update.";

            var result = MessageBox.Show(
                Window.GetWindow(this),
                $"JoinGameAfk will connect to Riot to update the champion list for your selected game region and language.\n\n{pictureDownloadText}\n\nContinue?",
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
                "Check for champion-list updates when JoinGameAfk starts?\n\nWhen enabled, JoinGameAfk connects to Riot at startup and keeps the champion list current for your selected game region and language. Champion pictures are not downloaded automatically. Use the pencil in Role Plans to add or refresh pictures.",
                "Allow Startup Champion List Update Check",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private bool ConfirmChampionPictureRefresh()
        {
            string cacheModeText = _championDataSettings.DownloadRawChampionPictures
                ? "Full-resolution enthusiast mode is enabled. Every tile will keep Riot's original JPG. This usually has no noticeable visual benefit in JoinGameAfk and can add 500 MB+ of disk and RAM usage across a complete cache."
                : "Compact picture mode is enabled, so extracted jpg files are resized to 96px-wide cache copies at maximum JPEG quality.";

            var result = MessageBox.Show(
                Window.GetWindow(this),
                $"Official JoinGameAfk releases already include a prepared champion picture cache. This download is intended for self-built installations, repairing or replacing the full cache, or retrieving Riot's original-resolution pictures.\n\nJoinGameAfk will download the Riot Data Dragon dragontail archive currently deployed in your selected game region. The archive can exceed 2 GB. Afterward, the app extracts every champion tile into the local cache and removes the archive.\n\nFor a small update, cancel and use the pencil in Role Plans instead.\n\n{cacheModeText}\n\nDownload all champion images?",
                "Download Champion Images",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private void RefreshChampionCatalogSyncStatus(ChampionCatalogRefreshResult? refreshResult = null)
        {
            string? dataDragonVersion = refreshResult?.DataDragonVersion;
            string? locale = refreshResult?.Locale;
            int championCount = refreshResult?.ChampionCount ?? 0;
            DateTime? lastSyncedAtUtc = refreshResult?.LastSyncedAtUtc;

            if (refreshResult is null)
            {
                var syncInfo = ChampionCatalog.GetLocalSyncInfo();
                dataDragonVersion = syncInfo.DataDragonVersion;
                locale = syncInfo.Locale;
                championCount = syncInfo.ChampionCount;
                lastSyncedAtUtc = syncInfo.LastSyncedAtUtc;
            }

            SetChampionCatalogSyncStatus(
                string.IsNullOrWhiteSpace(dataDragonVersion)
                    ? "Champion list has never been synced with Riot Data Dragon."
                    : $"Synced with Riot Data Dragon {dataDragonVersion} in "
                        + $"{(string.IsNullOrWhiteSpace(locale) ? "an unknown language" : RegionLocale.NormalizeLocale(locale))} "
                        + $"({championCount} champions). Last sync: {FormatLastSyncedAt(lastSyncedAtUtc)}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private async Task RefreshChampionCatalogVersionWarningAsync()
        {
            string platformId = _championDataSettings.PlatformId;
            if (string.Equals(
                    _checkedChampionCatalogPlatformId,
                    platformId,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_latestConfiguredRegionDataDragonVersion))
            {
                UpdateChampionCatalogVersionWarning();
                return;
            }

            _checkedChampionCatalogPlatformId = null;
            _latestConfiguredRegionDataDragonVersion = null;
            _championCatalogVersionCheckCancellation?.Cancel();
            _championCatalogVersionCheckCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _championCatalogVersionCheckCancellation = cancellation;

            UpdateChampionCatalogVersionWarning();

            try
            {
                string latestVersion = await _championCatalogRemoteService
                    .FetchLatestDataDragonVersionAsync(cancellation.Token);
                if (cancellation.IsCancellationRequested
                    || !string.Equals(
                        platformId,
                        _championDataSettings.PlatformId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _checkedChampionCatalogPlatformId = platformId;
                _latestConfiguredRegionDataDragonVersion = latestVersion;
                UpdateChampionCatalogVersionWarning();
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (!cancellation.IsCancellationRequested)
                    UpdateChampionCatalogVersionWarning();
            }
            finally
            {
                if (ReferenceEquals(_championCatalogVersionCheckCancellation, cancellation))
                {
                    _championCatalogVersionCheckCancellation.Dispose();
                    _championCatalogVersionCheckCancellation = null;
                }
            }
        }

        private void UpdateChampionCatalogVersionWarning()
        {
            var syncInfo = ChampionCatalog.GetLocalSyncInfo();
            string? localVersion = syncInfo.DataDragonVersion?.Trim();
            string? localLocale = syncInfo.Locale;
            string? regionalVersion = _latestConfiguredRegionDataDragonVersion?.Trim();
            string configuredLocale = RegionLocale.NormalizeLocale(_championDataSettings.Locale);

            if (string.IsNullOrWhiteSpace(localVersion))
            {
                ShowChampionCatalogVersionWarning(
                    $"No Data Dragon version is recorded for the local champion list. Select Update Champion List to download the list for {_championDataSettings.PlatformId} in {configuredLocale}.");
                return;
            }

            bool localeUnknown = string.IsNullOrWhiteSpace(localLocale);
            bool localeMismatch = !localeUnknown
                && !string.Equals(
                    RegionLocale.NormalizeLocale(localLocale),
                    configuredLocale,
                    StringComparison.OrdinalIgnoreCase);
            bool versionMismatch = !string.IsNullOrWhiteSpace(regionalVersion)
                && !string.Equals(localVersion, regionalVersion, StringComparison.OrdinalIgnoreCase);

            if (!localeUnknown && !localeMismatch && !versionMismatch)
            {
                HideChampionCatalogVersionWarning();
                return;
            }

            var mismatchDescriptions = new List<string>();
            if (localeUnknown)
                mismatchDescriptions.Add("the downloaded champion-list language is unknown");
            else if (localeMismatch)
                mismatchDescriptions.Add(
                    $"the champion list is in {RegionLocale.NormalizeLocale(localLocale)}, while your configured language is {configuredLocale}");

            if (versionMismatch)
            {
                mismatchDescriptions.Add(
                    $"the champion list version is {localVersion}, while {_championDataSettings.PlatformId} currently uses {regionalVersion}");
            }

            ShowChampionCatalogVersionWarning(
                $"Champion list mismatch: {string.Join("; ", mismatchDescriptions)}. "
                + $"Select Update Champion List to download champion names in {configuredLocale}.");
        }

        private void ShowChampionCatalogVersionWarning(string message)
        {
            ChampionCatalogVersionWarningTextBlock.Text = message;
            ChampionCatalogVersionWarningBorder.Visibility = Visibility.Visible;
        }

        private void HideChampionCatalogVersionWarning()
        {
            ChampionCatalogVersionWarningTextBlock.Text = string.Empty;
            ChampionCatalogVersionWarningBorder.Visibility = Visibility.Collapsed;
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

        private void SetChampionPictureDownloadStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionPictureDownloadStatusLabel.Text = message;
            ChampionPictureDownloadStatusLabel.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            ChampionPictureDownloadStatusLabel.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
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

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }

        private sealed record RegionLocaleSuggestion(string Code, string Name);

        private sealed record NewChampionPictureDownloadResult(
            int ChampionCount,
            int DownloadedTileCount,
            int UnchangedTileCount,
            int FailedTileCount,
            int FailedChampionCount);
    }
}
