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

        private void LeagueClientConnection_ConnectionChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateChampionDataSourceControlState();
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

        private void SaveChampionDataSettingsFromControls()
        {
            bool previousDownloadNewChampionPictures = _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate;
            _championDataSettings.DownloadNewChampionPicturesAfterCatalogUpdate =
                ChampionDataDownloadNewChampionPicturesCheckBox.IsChecked == true;

            try
            {
                _championDataSettings.Save();
            }
            catch (Exception ex)
            {
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

        private void UpdateChampionDataSourceControlState()
        {
            ChampionDataSourceMode source = ResolveChampionDataSource();
            ChampionDataActiveSourceTextBlock.Text = source == ChampionDataSourceMode.LeagueClient
                ? "League Client (LCU) · local and faster"
                : "Riot Data Dragon · internet confirmation required";
            ChampionDataActiveSourceDescriptionTextBlock.Text = source == ChampionDataSourceMode.LeagueClient
                ? "The watcher is connected, so champion data and pictures use the local League Client."
                : "The League Client is not connected. Start the watcher for faster local downloads; otherwise, each internet operation asks before using Data Dragon.";
            if (_leagueClientConnection.TryGetRegionLocale(out LeagueClientRegionLocaleInfo? regionLocale)
                && regionLocale is not null)
            {
                ChampionDataPlatformIdTextBlock.Text = regionLocale.PlatformId;
                ChampionDataLocaleTextBlock.Text = regionLocale.Locale;
                ChampionDataRegionLocaleDescriptionTextBlock.Text =
                    "Detected from the connected League Client. These values are saved for automatic Data Dragon requests when the client is offline.";
            }
            else
            {
                ChampionDataPlatformIdTextBlock.Text = _championDataSettings.PlatformId;
                ChampionDataLocaleTextBlock.Text = _championDataSettings.Locale;
                ChampionDataRegionLocaleDescriptionTextBlock.Text =
                    "Saved fallback values currently used by Data Dragon. The next League Client connection replaces both if its region or language changed.";
            }
        }

        private ChampionDataSourceMode ResolveChampionDataSource()
        {
            return ChampionDataSourcePolicy.Resolve(_leagueClientConnection.IsConnected);
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

            ChampionDataSourceMode source = ResolveChampionDataSource();

            if (!ConfirmChampionCatalogRefresh(source))
            {
                SetChampionCatalogRefreshStatus("Update canceled.", "TextSoftBrush", Brushes.SlateGray);
                return;
            }

            SetChampionDataOperationInProgress(true);
            SetChampionCatalogRefreshStatus($"Updating champion list from {GetChampionDataSourceName(source)}...", "TextSoftBrush", Brushes.SlateGray);
            SetNewChampionPictureDownloadStatus(string.Empty, "TextSoftBrush", Brushes.SlateGray);
            NewChampionPictureDownloadProgressBar.Visibility = Visibility.Collapsed;

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
                    SetNewChampionPictureDownloadStatus(
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
                    SetNewChampionPictureDownloadStatus(
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

            NewChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            NewChampionPictureDownloadProgressBar.IsIndeterminate = false;
            NewChampionPictureDownloadProgressBar.Minimum = 0;
            NewChampionPictureDownloadProgressBar.Maximum = championKeys.Count;
            NewChampionPictureDownloadProgressBar.Value = 0;

            for (int index = 0; index < championKeys.Count; index++)
            {
                int championKey = championKeys[index];
                if (!ChampionCatalog.TryGetByKey(championKey, out var champion) || champion is null)
                {
                    failedChampionCount++;
                    NewChampionPictureDownloadProgressBar.Value = index + 1;
                    continue;
                }

                SetNewChampionPictureDownloadStatus(
                    $"Downloading pictures for new champion {champion.Name} ({index + 1}/{championKeys.Count})...",
                    "TextSoftBrush",
                    Brushes.SlateGray);

                var progress = new Progress<ChampionTileDownloadProgress>(snapshot =>
                {
                    if (snapshot.Message.StartsWith("Unable to download ", StringComparison.OrdinalIgnoreCase))
                        return;

                    SetNewChampionPictureDownloadStatus(
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
                    NewChampionPictureDownloadProgressBar.Value = index + 1;
                }
            }

            NewChampionPictureDownloadProgressBar.IsIndeterminate = false;
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

            ChampionDataSourceMode source = ResolveChampionDataSource();

            if (!ConfirmDefaultChampionPictureDownload(source))
            {
                SetDefaultChampionPictureDownloadStatus(
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
            DefaultChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            DefaultChampionPictureDownloadProgressBar.IsIndeterminate = false;
            DefaultChampionPictureDownloadProgressBar.Minimum = 0;
            DefaultChampionPictureDownloadProgressBar.Maximum = Math.Max(1, expectedDefaultTileCount);
            DefaultChampionPictureDownloadProgressBar.Value = 0;
            SetDefaultChampionPictureDownloadStatus(
                $"Preparing up to {expectedDefaultTileCount} LoL and League Classic default pictures from {GetChampionDataSourceName(source)}...",
                "TextSoftBrush",
                Brushes.SlateGray);

            try
            {
                var progress = new Progress<ChampionDefaultTileDownloadProgress>(snapshot =>
                {
                    DefaultChampionPictureDownloadProgressBar.Maximum = Math.Max(1, snapshot.TotalTileCount);
                    DefaultChampionPictureDownloadProgressBar.Value = snapshot.CheckedTileCount;
                    SetDefaultChampionPictureDownloadStatus(
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

                DefaultChampionPictureDownloadProgressBar.Maximum = Math.Max(1, result.TileCount);
                DefaultChampionPictureDownloadProgressBar.Value = result.TileCount;
                string message =
                    $"LoL and League Classic default pictures completed. Updated {result.DownloadedTileCount}; "
                    + $"unchanged {result.UnchangedTileCount}; failed {result.FailedTileCount}.";
                SetDefaultChampionPictureDownloadStatus(
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
                SetDefaultChampionPictureDownloadStatus(message, "DangerTextBrush", Brushes.IndianRed);
                LogErrorMessage(message);
            }
            finally
            {
                DefaultChampionPictureDownloadProgressBar.IsIndeterminate = false;
                SetChampionDataOperationInProgress(false);
            }
        }

        private async void DownloadAllChampionPicturesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionDataOperationInProgress || _isChampionPictureDownloadInProgress)
                return;

            if (!_leagueClientConnection.IsConnected)
            {
                SetAllChampionPictureDownloadStatus(
                    "Connect the watcher to download every champion picture from the local League Client.",
                    "DangerTextBrush",
                    Brushes.IndianRed);
                return;
            }

            if (!ConfirmAllChampionPictureDownload())
            {
                SetAllChampionPictureDownloadStatus(
                    "All champion picture download canceled.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            IReadOnlyList<ChampionInfo> champions = ChampionCatalog.All
                .Where(champion => champion.Key > 0)
                .OrderBy(champion => champion.Key)
                .ToList();
            int championModeCount = champions.Count
                + champions.Count(champion => champion.SupportsLeagueClassic == true);
            SetChampionDataOperationInProgress(true);
            AllChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            AllChampionPictureDownloadProgressBar.IsIndeterminate = false;
            AllChampionPictureDownloadProgressBar.Minimum = 0;
            AllChampionPictureDownloadProgressBar.Maximum = Math.Max(1, championModeCount);
            AllChampionPictureDownloadProgressBar.Value = 0;
            SetAllChampionPictureDownloadStatus(
                $"Preparing every LoL and League Classic picture for {championModeCount} champion modes from the local League Client...",
                "TextSoftBrush",
                Brushes.SlateGray);

            try
            {
                LogMessage(
                    $"Starting LCU-only all-picture download for {championModeCount} LoL and League Classic champion modes. Individual asset requests are omitted from the terminal log.");
                var progress = new Progress<ChampionCollectionTileDownloadProgress>(snapshot =>
                {
                    AllChampionPictureDownloadProgressBar.Maximum = Math.Max(1, snapshot.TotalChampionCount);
                    AllChampionPictureDownloadProgressBar.Value = snapshot.CheckedChampionCount;
                    bool failed = snapshot.Message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase);
                    SetAllChampionPictureDownloadStatus(
                        snapshot.Message,
                        failed ? "DangerTextBrush" : "TextSoftBrush",
                        failed ? Brushes.IndianRed : Brushes.SlateGray);
                    if (failed)
                        LogErrorMessage(snapshot.Message);
                });

                using var http = _leagueClientConnection.CreateHttpClient();
                ChampionCollectionTileDownloadResult result =
                    await LeagueClientChampionTileDownloadService.DownloadAllChampionTilesAsync(
                        http,
                        champions,
                        ChampionTileCatalog.TileDirectoryPath,
                        progress,
                        optimizeForLocalCache: !_championDataSettings.DownloadRawChampionPictures);
                ChampionTileCatalog.Reload();

                AllChampionPictureDownloadProgressBar.Maximum = Math.Max(1, result.ChampionCount);
                AllChampionPictureDownloadProgressBar.Value = result.ChampionCount;
                bool completedWithoutFailures = result.FailedTileCount == 0
                    && result.FailedChampionCount == 0;
                string message =
                    $"All champion pictures completed. Checked {result.TileCount}; updated {result.DownloadedTileCount}; "
                    + $"unchanged {result.UnchangedTileCount}; failed tiles {result.FailedTileCount}; "
                    + $"failed champion modes {result.FailedChampionCount}.";
                SetAllChampionPictureDownloadStatus(
                    message,
                    completedWithoutFailures ? "AccentGreenTextBrush" : "DangerTextBrush",
                    completedWithoutFailures ? Brushes.ForestGreen : Brushes.IndianRed);
                LogMessage($"{message} Source: League Client (LCU).");
            }
            catch (Exception ex)
            {
                string message =
                    $"All champion picture download stopped. Existing pictures were kept. {FormatException(ex)}";
                SetAllChampionPictureDownloadStatus(message, "DangerTextBrush", Brushes.IndianRed);
                LogErrorMessage(message);
            }
            finally
            {
                AllChampionPictureDownloadProgressBar.IsIndeterminate = false;
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
            DownloadAllChampionPicturesButton.IsEnabled = enabled && _leagueClientConnection.IsConnected;
        }

        private bool ConfirmAllChampionPictureDownload()
        {
            MessageBoxResult result = MessageBox.Show(
                Window.GetWindow(this),
                "JoinGameAfk will copy every available LoL and League Classic champion tile from the connected League Client. "
                    + "This makes many sequential local LCU requests and can take several minutes. Existing pictures are kept when they are already identical.\n\nContinue?",
                "Download All Champion Pictures",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);
            return result == MessageBoxResult.OK;
        }

        private bool ConfirmDefaultChampionPictureDownload(ChampionDataSourceMode source)
        {
            if (source == ChampionDataSourceMode.LeagueClient)
                return true;

            MessageBoxResult result = MessageBox.Show(
                Window.GetWindow(this),
                $"The League Client is not connected, so JoinGameAfk will use Riot Data Dragon with {_championDataSettings.PlatformId} · {_championDataSettings.Locale}. "
                    + "Start the watcher instead if you want the faster local download.\n\n"
                    + "JoinGameAfk will download one LoL default tile for every champion, "
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
                $"The League Client is not connected, so JoinGameAfk will use Riot Data Dragon with {_championDataSettings.PlatformId} · {_championDataSettings.Locale}. "
                    + "Start the watcher instead if you want the faster local update.\n\n"
                    + $"{pictureDownloadText}\n\nContinue?",
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
            ChampionDataSourceMode source = ResolveChampionDataSource();
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
            if (ResolveChampionDataSource()
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
            bool localCatalogCameFromLeagueClient = string.Equals(
                localVersion,
                ChampionCatalogSourceIds.LeagueClient,
                StringComparison.OrdinalIgnoreCase);
            bool versionMismatch = !localCatalogCameFromLeagueClient
                && !string.IsNullOrWhiteSpace(regionalVersion)
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

        private void SetNewChampionPictureDownloadStatus(
            string message,
            string brushResourceKey,
            Brush fallbackBrush)
        {
            SetChampionPictureDownloadStatus(
                NewChampionPictureDownloadStatusLabel,
                message,
                brushResourceKey,
                fallbackBrush);
        }

        private void SetDefaultChampionPictureDownloadStatus(
            string message,
            string brushResourceKey,
            Brush fallbackBrush)
        {
            SetChampionPictureDownloadStatus(
                DefaultChampionPictureDownloadStatusLabel,
                message,
                brushResourceKey,
                fallbackBrush);
        }

        private void SetAllChampionPictureDownloadStatus(
            string message,
            string brushResourceKey,
            Brush fallbackBrush)
        {
            SetChampionPictureDownloadStatus(
                AllChampionPictureDownloadStatusLabel,
                message,
                brushResourceKey,
                fallbackBrush);
        }

        private void SetChampionPictureDownloadStatus(
            TextBlock statusLabel,
            string message,
            string brushResourceKey,
            Brush fallbackBrush)
        {
            statusLabel.Text = message;
            statusLabel.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            statusLabel.Visibility = string.IsNullOrWhiteSpace(message)
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

        private sealed record NewChampionPictureDownloadResult(
            int ChampionCount,
            int DownloadedTileCount,
            int UnchangedTileCount,
            int FailedTileCount,
            int FailedChampionCount);
    }
}
