using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
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

        private static readonly IReadOnlyList<ChampionDataSourceOption> ChampionDataSourceOptions =
        [
            new(ChampionDataSourceMode.LeagueClient, "LeagueClient (LCU) — local, automatic every 12 hours"),
            new(ChampionDataSourceMode.DataDragon, "DataDragon (Riot) — internet")
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
                ChampionDataSourceModeBox.SelectedValue = _championDataSettings.SourceMode;
                ChampionDataDownloadNewChampionPicturesCheckBox.IsChecked =
                    _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate;
                UpdateChampionDataSourceControlState();
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

            SaveChampionDataSettingsFromControls();
        }

        private void ChampionDataRegionLocale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingChampionDataSettings)
                return;

            SaveChampionDataSettingsFromControls();
            UpdateChampionDataSourceControlState();
            if (ReferenceEquals(sender, ChampionDataSourceModeBox)
                && _championDataSettings.SourceMode == ChampionDataSourceMode.LeagueClient
                && _leagueClientConnection.IsConnected)
            {
                _ = RefreshLeagueClientAfterSourceSelectionAsync();
            }
        }

        private async Task RefreshLeagueClientAfterSourceSelectionAsync()
        {
            try
            {
                LeagueClientChampionCatalogAutoSyncResult? syncResult =
                    await _championCatalogSyncCoordinator.RefreshLeagueClientIfDueAsync();
                UpdateChampionDataSourceControlState();
                if (syncResult?.Refreshed == true && syncResult.RefreshResult is { } refreshResult)
                {
                    RefreshChampionCatalogSyncStatus(refreshResult, ChampionDataSourceMode.LeagueClient);
                    SetChampionCatalogRefreshStatus(
                        "League Client champion list synchronized after changing the data source.",
                        "AccentGreenTextBrush",
                        Brushes.ForestGreen);
                    LogMessage("League Client champion list synchronized after changing the data source.");
                }
            }
            catch (Exception ex)
            {
                string message = "Automatic League Client champion-list sync failed after changing the data source. "
                    + $"Existing local data was kept. {FormatException(ex)}";
                SetChampionCatalogRefreshStatus(message, "DangerTextBrush", Brushes.IndianRed);
                LogErrorMessage(message);
            }
        }

        private void SaveChampionDataSettingsFromControls()
        {
            ChampionDataSourceMode sourceMode = ChampionDataSourceModeBox.SelectedValue is ChampionDataSourceMode selected
                ? selected
                : ChampionDataSourceMode.LeagueClient;
            string normalizedPlatformId = _championDataSettings.PlatformId;
            string normalizedLocale = _championDataSettings.Locale;
            if (sourceMode == ChampionDataSourceMode.DataDragon)
            {
                string platformId = ChampionDataPlatformIdBox.SelectedValue?.ToString() ?? string.Empty;
                string locale = ChampionDataLocaleBox.SelectedValue?.ToString() ?? string.Empty;
                if (!RegionLocale.TryNormalizePlatformId(platformId, out normalizedPlatformId)
                    || !RegionLocale.TryNormalizeLocale(locale, out normalizedLocale))
                {
                    ApplyChampionDataSettingsToControls();
                    return;
                }
            }

            string previousPlatformId = _championDataSettings.PlatformId;
            string previousLocale = _championDataSettings.Locale;
            bool previousDownloadNewChampionPictures = _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate;
            ChampionDataSourceMode previousSourceMode = _championDataSettings.SourceMode;

            _championDataSettings.PlatformId = normalizedPlatformId;
            _championDataSettings.Locale = normalizedLocale;
            _championDataSettings.SourceMode = sourceMode;
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
                _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate = previousDownloadNewChampionPictures;
                _championDataSettings.SourceMode = previousSourceMode;
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

        private void UpdateChampionDataSourceControlState()
        {
            ChampionDataSourceMode source = ChampionDataSourceModeBox.SelectedValue is ChampionDataSourceMode selected
                ? selected
                : ChampionDataSourceMode.LeagueClient;
            ChampionDataLeagueClientConfigurationPanel.Visibility = source == ChampionDataSourceMode.LeagueClient
                ? Visibility.Visible
                : Visibility.Collapsed;
            ChampionDataDataDragonConfigurationPanel.Visibility = source == ChampionDataSourceMode.DataDragon
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (_leagueClientConnection.TryGetRegionLocale(out LeagueClientRegionLocaleInfo? regionLocale)
                && regionLocale is not null)
            {
                ChampionDataLeagueClientRegionLocaleTextBlock.Text =
                    $"Detected from the connected League Client: {regionLocale.PlatformId} · {regionLocale.Locale}. "
                    + "Data Dragon settings are ignored while LCU is selected.";
            }
            else
            {
                ChampionDataLeagueClientRegionLocaleTextBlock.Text =
                    "Start the watcher to detect the League Client region and language. "
                    + "Data Dragon settings are ignored while LCU is selected.";
            }
        }

        private ChampionDataSourceMode ResolveChampionDataSource()
        {
            ChampionDataSourceMode source = ChampionDataSourcePolicy.Resolve(
                _championDataSettings.SourceMode);
            if (source == ChampionDataSourceMode.LeagueClient && !_leagueClientConnection.IsConnected)
            {
                throw new InvalidOperationException(
                    "LeagueClient (LCU) is selected, but the watcher is not connected to the League Client.");
            }

            return source;
        }

        private Task<ChampionCatalogRefreshResult> RefreshChampionCatalogAsync(
            ChampionDataSourceMode source,
            CancellationToken cancellationToken = default)
        {
            return _championCatalogSyncCoordinator.RefreshAsync(source, cancellationToken);
        }

        private async Task<ChampionTileDownloadResult> DownloadChampionImagesAsync(
            ChampionInfo champion,
            ChampionDataSourceMode source,
            IProgress<ChampionTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (source == ChampionDataSourceMode.DataDragon)
            {
                return await ChampionTileCatalog.DownloadAllImagesForChampionAsync(
                    champion,
                    _activeRolePlanMode,
                    progress,
                    cancellationToken,
                    optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures,
                    preferredLocale: _championDataSettings.Locale);
            }

            using var http = _leagueClientConnection.CreateHttpClient(LogMessage);
            ChampionTileDownloadResult result = await LeagueClientChampionTileDownloadService
                .DownloadChampionTilesAsync(
                    http,
                    champion,
                    _activeRolePlanMode,
                    ChampionTileCatalog.TileDirectoryPath,
                    progress,
                    cancellationToken,
                    optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures);
            ChampionTileCatalog.Reload();
            return result;
        }

        private static string GetChampionDataSourceName(ChampionDataSourceMode source)
        {
            return source == ChampionDataSourceMode.LeagueClient
                ? "League Client (LCU)"
                : "Riot Data Dragon";
        }

        private async void RefreshChampionCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionDataOperationInProgress || _isChampionPictureDownloadInProgress)
                return;

            ChampionDataSourceMode source;
            try
            {
                source = ResolveChampionDataSource();
            }
            catch (Exception ex)
            {
                string message = $"Champion list update failed before starting. {ex.Message}";
                SetChampionCatalogRefreshStatus(message, "DangerTextBrush", Brushes.IndianRed);
                LogErrorMessage(message);
                return;
            }

            if (!ConfirmChampionCatalogRefresh(source))
            {
                SetChampionCatalogRefreshStatus("Update canceled.", "TextSoftBrush", Brushes.SlateGray);
                return;
            }

            SetChampionDataOperationInProgress(true);
            SetChampionCatalogRefreshStatus($"Updating champion list from {GetChampionDataSourceName(source)}...", "TextSoftBrush", Brushes.SlateGray);
            SetChampionPictureDownloadStatus(string.Empty, "TextSoftBrush", Brushes.SlateGray);
            ChampionPictureDownloadProgressBar.Visibility = Visibility.Collapsed;

            try
            {
                var existingChampionKeys = ChampionCatalog.All
                    .Select(champion => champion.Key)
                    .ToHashSet();
                var result = await RefreshChampionCatalogAsync(source);
                UpdateChampionDataSourceControlState();
                var newChampionKeys = ChampionCatalog.All
                    .Where(champion => champion.Key > 0 && !existingChampionKeys.Contains(champion.Key))
                    .Select(champion => champion.Key)
                    .Distinct()
                    .OrderBy(championKey => championKey)
                    .ToList();
                _checkedChampionCatalogPlatformId = source == ChampionDataSourceMode.DataDragon
                    ? _championDataSettings.PlatformId
                    : null;
                _latestConfiguredRegionDataDragonVersion = source == ChampionDataSourceMode.DataDragon
                    ? result.DataDragonVersion
                    : null;
                RefreshChampionCatalogSyncStatus(result, source);
                UpdateChampionCatalogVersionWarning();
                SetChampionCatalogRefreshStatus(
                    CreateChampionCatalogRefreshStatusMessage(newChampionKeys),
                    "AccentGreenTextBrush",
                    Brushes.ForestGreen);

                if (_championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate
                    && newChampionKeys.Count > 0)
                {
                    var pictureResult = await DownloadNewChampionPicturesAsync(newChampionKeys, source);
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
                LogErrorMessage(
                    $"Champion list update from {GetChampionDataSourceName(source)} failed. Existing local file was kept. {FormatException(ex)}");
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
            IReadOnlyList<int> championKeys,
            ChampionDataSourceMode source)
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
                    var result = await DownloadChampionImagesAsync(champion, source, progress);
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

        private async void DownloadDefaultChampionPicturesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionDataOperationInProgress || _isChampionPictureDownloadInProgress)
                return;

            ChampionDataSourceMode source;
            try
            {
                source = ResolveChampionDataSource();
            }
            catch (Exception ex)
            {
                string message = $"Default picture download failed before starting. {ex.Message}";
                SetChampionPictureDownloadStatus(message, "DangerTextBrush", Brushes.IndianRed);
                LogErrorMessage(message);
                return;
            }

            if (!ConfirmDefaultChampionPictureDownload(source))
            {
                SetChampionPictureDownloadStatus(
                    "LoL and League Classic default picture download canceled.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            IReadOnlyList<ChampionInfo> champions = ChampionCatalog.All
                .Where(champion => champion.Key > 0)
                .OrderBy(champion => champion.Key)
                .ToList();
            int expectedDefaultTileCount = champions.Count
                + champions.Count(champion => champion.SupportsLeagueClassic == true);
            SetChampionDataOperationInProgress(true);
            ChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            ChampionPictureDownloadProgressBar.IsIndeterminate = false;
            ChampionPictureDownloadProgressBar.Minimum = 0;
            ChampionPictureDownloadProgressBar.Maximum = Math.Max(1, expectedDefaultTileCount);
            ChampionPictureDownloadProgressBar.Value = 0;
            SetChampionPictureDownloadStatus(
                $"Preparing up to {expectedDefaultTileCount} LoL and League Classic default pictures from {GetChampionDataSourceName(source)}...",
                "TextSoftBrush",
                Brushes.SlateGray);

            try
            {
                var progress = new Progress<ChampionDefaultTileDownloadProgress>(snapshot =>
                {
                    ChampionPictureDownloadProgressBar.Maximum = Math.Max(1, snapshot.TotalTileCount);
                    ChampionPictureDownloadProgressBar.Value = snapshot.CheckedTileCount;
                    SetChampionPictureDownloadStatus(
                        snapshot.Message,
                        snapshot.FailedTileCount == 0 ? "TextSoftBrush" : "DangerTextBrush",
                        snapshot.FailedTileCount == 0 ? Brushes.SlateGray : Brushes.IndianRed);
                    if (snapshot.Message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase))
                        LogErrorMessage(snapshot.Message);
                });

                ChampionDefaultTileDownloadResult result;
                if (source == ChampionDataSourceMode.DataDragon)
                {
                    result = await ChampionTileCatalog.DownloadDefaultImagesFromDataDragonAsync(
                        champions,
                        progress,
                        optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures,
                        preferredLocale: _championDataSettings.Locale);
                }
                else
                {
                    using var http = _leagueClientConnection.CreateHttpClient(LogMessage);
                    result = await LeagueClientChampionTileDownloadService.DownloadDefaultChampionTilesAsync(
                        http,
                        champions,
                        ChampionTileCatalog.TileDirectoryPath,
                        progress,
                        optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures);
                    ChampionTileCatalog.Reload();
                }

                ChampionPictureDownloadProgressBar.Maximum = Math.Max(1, result.TileCount);
                ChampionPictureDownloadProgressBar.Value = result.TileCount;
                string message =
                    $"LoL and League Classic default pictures completed. Updated {result.DownloadedTileCount}; "
                    + $"unchanged {result.UnchangedTileCount}; failed {result.FailedTileCount}.";
                SetChampionPictureDownloadStatus(
                    message,
                    result.FailedTileCount == 0 ? "AccentGreenTextBrush" : "DangerTextBrush",
                    result.FailedTileCount == 0 ? Brushes.ForestGreen : Brushes.IndianRed);
                LogMessage(
                    $"LoL and League Classic default pictures completed from {GetChampionDataSourceName(source)}. "
                    + $"Checked {result.TileCount}; updated {result.DownloadedTileCount}; "
                    + $"unchanged {result.UnchangedTileCount}; failed {result.FailedTileCount}.");
            }
            catch (Exception ex)
            {
                string message =
                    $"Default champion picture download failed. Existing pictures were kept. {FormatException(ex)}";
                SetChampionPictureDownloadStatus(message, "DangerTextBrush", Brushes.IndianRed);
                LogErrorMessage(message);
            }
            finally
            {
                ChampionPictureDownloadProgressBar.IsIndeterminate = false;
                SetChampionDataOperationInProgress(false);
            }
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
            ChampionDataDownloadNewChampionPicturesCheckBox.IsEnabled = enabled;
            RefreshChampionCatalogButton.IsEnabled = enabled;
            DownloadDefaultChampionPicturesButton.IsEnabled = enabled;
        }

        private bool ConfirmDefaultChampionPictureDownload(ChampionDataSourceMode source)
        {
            if (source == ChampionDataSourceMode.LeagueClient)
                return true;

            MessageBoxResult result = MessageBox.Show(
                Window.GetWindow(this),
                "JoinGameAfk will connect to Riot Data Dragon and download one LoL default tile for every champion, "
                    + "plus one Classic default tile for every champion available in League Classic. "
                    + "It will not download the full archive or additional skins.\n\nContinue?",
                "Download Default Champion Pictures",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);
            return result == MessageBoxResult.OK;
        }

        private bool ConfirmChampionCatalogRefresh(ChampionDataSourceMode source)
        {
            if (source == ChampionDataSourceMode.LeagueClient)
                return true;

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

        private void RefreshChampionCatalogSyncStatus(
            ChampionCatalogRefreshResult? refreshResult = null,
            ChampionDataSourceMode? source = null)
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

            string sourceName = source is not null
                ? GetChampionDataSourceName(source.Value)
                : string.Equals(
                    dataDragonVersion,
                    LeagueClientChampionCatalogService.LocalCatalogVersion,
                    StringComparison.OrdinalIgnoreCase)
                    ? GetChampionDataSourceName(ChampionDataSourceMode.LeagueClient)
                    : string.IsNullOrWhiteSpace(dataDragonVersion)
                        ? "champion data"
                        : GetChampionDataSourceName(ChampionDataSourceMode.DataDragon);
            string versionText = string.Equals(
                    dataDragonVersion,
                    LeagueClientChampionCatalogService.LocalCatalogVersion,
                    StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" {dataDragonVersion}";
            SetChampionCatalogSyncStatus(
                string.IsNullOrWhiteSpace(dataDragonVersion)
                    ? "Champion list has never been synced."
                    : $"Synced from {sourceName}{versionText} in "
                        + $"{(string.IsNullOrWhiteSpace(locale) ? "an unknown language" : RegionLocale.NormalizeLocale(locale))} "
                        + $"({championCount} champions). Last sync: {FormatLastSyncedAt(lastSyncedAtUtc)}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private Task RefreshChampionCatalogVersionWarningAsync()
        {
            ChampionDataSourceMode source = ChampionDataSourcePolicy.Resolve(
                _championDataSettings.SourceMode);
            if (source == ChampionDataSourceMode.LeagueClient)
            {
                HideChampionCatalogVersionWarning();
                return Task.CompletedTask;
            }

            // Opening the manager must not make an external request. A Data Dragon
            // version is checked only after the user-approved update operation.
            UpdateChampionCatalogVersionWarning();
            return Task.CompletedTask;
        }

        private void UpdateChampionCatalogVersionWarning()
        {
            if (ChampionDataSourcePolicy.Resolve(_championDataSettings.SourceMode)
                == ChampionDataSourceMode.LeagueClient)
            {
                HideChampionCatalogVersionWarning();
                return;
            }

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

        private static string FormatLastSyncedAt(DateTime? lastSyncedAtUtc)
        {
            if (lastSyncedAtUtc is null)
                return "unknown";

            return DateTime.SpecifyKind(lastSyncedAtUtc.Value, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("g");
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }

        private sealed record RegionLocaleSuggestion(string Code, string Name);

        private sealed record ChampionDataSourceOption(ChampionDataSourceMode Mode, string Name);

        private sealed record NewChampionPictureDownloadResult(
            int ChampionCount,
            int DownloadedTileCount,
            int UnchangedTileCount,
            int FailedTileCount,
            int FailedChampionCount);
    }
}
