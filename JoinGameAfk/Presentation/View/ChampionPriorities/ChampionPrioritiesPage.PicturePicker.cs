using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private void ToggleChampionPictureEditModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            SetChampionPictureEditMode(!IsChampionPictureEditMode);
            e.Handled = true;
        }

        private void SetChampionPictureEditMode(bool isEnabled)
        {
            if (IsChampionPictureEditMode == isEnabled)
                return;

            if (!isEnabled)
                CloseChampionPicturePicker();

            IsChampionPictureEditMode = isEnabled;
            _draggedReferenceChampion = null;
            _suppressReferenceChampionClick = false;
            _filteredChampionReferences = CreateChampionReferenceItems(_filteredChampions);
            ChampionReferenceList.ItemsSource = _filteredChampionReferences;
        }

        private void OpenChampionPicturePicker(ChampionInfo champion)
        {
            _selectedChampionPictureChampion = champion;
            _originalChampionPictureFileName = ChampionImageSelectionStore.GetSelection(champion.Id);
            _pendingChampionPictureFileName = _originalChampionPictureFileName;

            var chipLabel = ChampionChipLabelFormatter.Format(champion.Name);
            ChampionPicturePickerPreviewNameTextBlock.Text = chipLabel.Text;
            ChampionPicturePickerPreviewNameTextBlock.FontSize = chipLabel.FontSize;
            ChampionPicturePickerTitleTextBlock.Text = champion.Name;
            ChampionPicturePickerOverlay.Visibility = Visibility.Visible;
            ClearChampionPicturePickerDownloadStatus();
            RefreshChampionPicturePicker();
            ChampionPicturePickerTileListBox.Focus();
        }

        private void OpenChampionPicturePicker(ChampionSelectionItem champion)
        {
            ChampionInfo championInfo = ChampionCatalog.TryGetById(champion.ChampionId, out var catalogChampion)
                ? catalogChampion!
                : new ChampionInfo(champion.ChampionId, champion.DisplayText);

            OpenChampionPicturePicker(championInfo);
        }

        private void CloseChampionPicturePicker()
        {
            ChampionInfo? champion = _selectedChampionPictureChampion;
            string? pendingFileName = _pendingChampionPictureFileName;
            string? originalFileName = _originalChampionPictureFileName;

            _selectedChampionPictureChampion = null;
            _pendingChampionPictureFileName = null;
            _originalChampionPictureFileName = null;
            ChampionPicturePickerOverlay.Visibility = Visibility.Collapsed;
            ChampionPicturePickerTileListBox.ItemsSource = null;
            ChampionPicturePickerStatusTextBlock.Text = string.Empty;
            ClearChampionPicturePickerDownloadStatus();
            ChampionPicturePickerPreviewImage.Source = null;
            ChampionPicturePickerPreviewNameTextBlock.Text = string.Empty;

            if (champion is not null && !IsSameChampionPictureFileName(pendingFileName, originalFileName))
                QueueChampionPictureSelectionSave(champion, pendingFileName);
        }

        private void ChampionPicturePickerCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseChampionPicturePicker();
            e.Handled = true;
        }

        private void ChampionPicturePickerUseDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionPictureDownloadInProgress)
                return;

            if (_selectedChampionPictureChampion is not ChampionInfo champion)
                return;

            _pendingChampionPictureFileName = null;
            ClearChampionPicturePickerDownloadStatus();
            var options = ChampionTileCatalog.GetOptions(champion).ToList();
            ChampionTileOption? defaultOption = ChampionTileCatalog.GetDefaultOption(champion);
            SelectChampionPicturePickerOption(defaultOption, scrollIntoView: true);
            UpdateChampionPicturePickerPreview(champion, defaultOption, options.Count);
            e.Handled = true;
        }

        private async void ChampionPicturePickerDownloadAllButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (_isChampionPictureDownloadInProgress
                || _selectedChampionPictureChampion is not ChampionInfo champion)
            {
                return;
            }

            if (!ConfirmChampionPictureDownload(champion))
            {
                SetChampionPicturePickerDownloadStatus(
                    $"Picture download canceled for {champion.Name}.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            int requestedChampionId = champion.Id;
            _isChampionPictureDownloadInProgress = true;
            SetChampionPicturePickerDownloadControlsEnabled(false);
            SetChampionPicturePickerDownloadStatus(
                $"Preparing {champion.Name} picture download...",
                "TextSoftBrush",
                Brushes.SlateGray);

            try
            {
                var progress = new Progress<ChampionTileDownloadProgress>(snapshot =>
                {
                    if (_selectedChampionPictureChampion?.Id != requestedChampionId
                        || ChampionPicturePickerOverlay.Visibility != Visibility.Visible)
                    {
                        return;
                    }

                    if (snapshot.Message.StartsWith("Unable to download ", StringComparison.OrdinalIgnoreCase))
                        return;

                    SetChampionPicturePickerDownloadStatus(
                        snapshot.Message,
                        snapshot.Message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase)
                            ? "DangerTextBrush"
                            : "TextSoftBrush",
                        snapshot.Message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase)
                            ? Brushes.IndianRed
                            : Brushes.SlateGray);
                });

                var result = await ChampionTileCatalog.DownloadAllImagesForChampionAsync(
                    champion,
                    progress,
                    optimizeForLocalCache: !_settings.DownloadRawChampionPictures);
                if (_selectedChampionPictureChampion?.Id != requestedChampionId
                    || ChampionPicturePickerOverlay.Visibility != Visibility.Visible)
                {
                    return;
                }

                RefreshChampionImages();
                string statusMessage = CreateChampionPictureDownloadStatusMessage(result);
                bool isCompleteSuccess = result.FailedTileCount == 0;
                SetChampionPicturePickerDownloadStatus(
                    statusMessage,
                    isCompleteSuccess ? "AccentGreenTextBrush" : "TextSoftBrush",
                    isCompleteSuccess ? Brushes.ForestGreen : Brushes.SlateGray);
            }
            catch (Exception ex)
            {
                if (_selectedChampionPictureChampion?.Id == requestedChampionId
                    && ChampionPicturePickerOverlay.Visibility == Visibility.Visible)
                {
                    SetChampionPicturePickerDownloadStatus(
                        $"Unable to download {champion.Name} pictures. Existing local pictures were kept. {ex.Message}",
                        "DangerTextBrush",
                        Brushes.IndianRed);
                }
            }
            finally
            {
                _isChampionPictureDownloadInProgress = false;
                if (_selectedChampionPictureChampion?.Id == requestedChampionId
                    && ChampionPicturePickerOverlay.Visibility == Visibility.Visible)
                {
                    SetChampionPicturePickerDownloadControlsEnabled(true);
                    RefreshChampionPicturePickerActionStates();
                }
            }
        }

        private static string CreateChampionPictureDownloadStatusMessage(ChampionTileDownloadResult result)
        {
            if (result.FailedTileCount == 0 && result.DownloadedTileCount == 0)
                return $"{result.ChampionName} pictures are already up to date. {result.UnchangedTileCount} local pictures checked.";

            if (result.FailedTileCount == 0)
                return $"Downloaded {result.DownloadedTileCount} {result.ChampionName} pictures; {result.UnchangedTileCount} already up to date.";

            if (result.DownloadedTileCount == 0)
                return $"{result.ChampionName} pictures were already present where available. Checked {result.UnchangedTileCount} existing pictures; {result.FailedTileCount} Riot tile request(s) did not return an image.";

            return $"Downloaded {result.DownloadedTileCount} {result.ChampionName} pictures; {result.UnchangedTileCount} already up to date; {result.FailedTileCount} Riot tile request(s) did not return an image.";
        }

        private void ChampionPicturePickerTileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingChampionPicturePicker)
                return;

            if (_selectedChampionPictureChampion is not ChampionInfo champion
                || ChampionPicturePickerTileListBox.SelectedItem is not ChampionTileOption selectedOption)
            {
                return;
            }

            _pendingChampionPictureFileName = IsDefaultChampionPictureOption(champion, selectedOption)
                ? null
                : selectedOption.FileName;
            ClearChampionPicturePickerDownloadStatus();

            UpdateChampionPicturePickerPreview(
                champion,
                selectedOption,
                ChampionPicturePickerTileListBox.Items.Count);
        }

        private void ChampionPicturePickerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, ChampionPicturePickerOverlay))
            {
                CloseChampionPicturePicker();
                e.Handled = true;
            }
        }

        private void RefreshChampionPicturePicker()
        {
            if (_selectedChampionPictureChampion is not ChampionInfo champion
                || ChampionPicturePickerOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            var options = ChampionTileCatalog.GetOptions(champion).ToList();
            ChampionTileOption? selectedOption = GetPendingChampionPictureOption(champion, options);

            _isUpdatingChampionPicturePicker = true;
            try
            {
                ChampionPicturePickerTileListBox.ItemsSource = options;
                ChampionPicturePickerTileListBox.IsEnabled = !_isChampionPictureDownloadInProgress && options.Count > 0;
                SelectChampionPicturePickerOption(selectedOption, scrollIntoView: true);
            }
            finally
            {
                _isUpdatingChampionPicturePicker = false;
            }

            UpdateChampionPicturePickerPreview(champion, selectedOption, options.Count);
        }

        private ChampionTileOption? GetPendingChampionPictureOption(
            ChampionInfo champion,
            IReadOnlyList<ChampionTileOption> options)
        {
            if (options.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(_pendingChampionPictureFileName))
            {
                var pendingOption = options.FirstOrDefault(option =>
                    string.Equals(option.FileName, _pendingChampionPictureFileName, StringComparison.OrdinalIgnoreCase));

                if (pendingOption is not null)
                    return pendingOption;
            }

            return ChampionTileCatalog.GetDefaultOption(champion);
        }

        private void SelectChampionPicturePickerOption(ChampionTileOption? selectedOption, bool scrollIntoView)
        {
            _isUpdatingChampionPicturePicker = true;
            try
            {
                ChampionPicturePickerTileListBox.SelectedItem = selectedOption;
                if (scrollIntoView && selectedOption is not null)
                    ChampionPicturePickerTileListBox.ScrollIntoView(selectedOption);
            }
            finally
            {
                _isUpdatingChampionPicturePicker = false;
            }
        }

        private void UpdateChampionPicturePickerPreview(
            ChampionInfo champion,
            ChampionTileOption? selectedOption,
            int optionCount)
        {
            bool hasPendingCustomSelection = !string.IsNullOrWhiteSpace(_pendingChampionPictureFileName);
            ChampionPicturePickerPreviewImage.Source = selectedOption?.ImageSource;
            ChampionPicturePickerUseDefaultButton.IsEnabled = !_isChampionPictureDownloadInProgress && hasPendingCustomSelection;
            ChampionPicturePickerDownloadAllButton.IsEnabled = !_isChampionPictureDownloadInProgress;

            if (optionCount == 0)
            {
                SetChampionPicturePickerStatus(
                    $"No local pictures found for {champion.Name}. Use Settings to download or reload champion pictures.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            SetChampionPicturePickerStatus(
                selectedOption is null
                    ? $"No picture selected for {champion.Name}."
                    : hasPendingCustomSelection
                        ? $"Selected {selectedOption.FileName} for {champion.Name}."
                        : $"Using default {selectedOption.FileName} for {champion.Name}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private void RefreshChampionPicturePickerActionStates()
        {
            bool hasPendingCustomSelection = !string.IsNullOrWhiteSpace(_pendingChampionPictureFileName);
            ChampionPicturePickerDownloadAllButton.IsEnabled = !_isChampionPictureDownloadInProgress
                && _selectedChampionPictureChampion is not null;
            ChampionPicturePickerUseDefaultButton.IsEnabled = !_isChampionPictureDownloadInProgress
                && hasPendingCustomSelection;
            ChampionPicturePickerTileListBox.IsEnabled = !_isChampionPictureDownloadInProgress
                && ChampionPicturePickerTileListBox.Items.Count > 0;
        }

        private void SetChampionPicturePickerDownloadControlsEnabled(bool enabled)
        {
            ChampionPicturePickerDownloadAllButton.IsEnabled = enabled;
            ChampionPicturePickerUseDefaultButton.IsEnabled = enabled;
            ChampionPicturePickerTileListBox.IsEnabled = enabled && ChampionPicturePickerTileListBox.Items.Count > 0;
        }

        private void SetChampionPicturePickerStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionPicturePickerStatusTextBlock.Text = message;
            ChampionPicturePickerStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
        }

        private void SetChampionPicturePickerDownloadStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionPicturePickerDownloadStatusTextBlock.Text = message;
            ChampionPicturePickerDownloadStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            ChampionPicturePickerDownloadStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ClearChampionPicturePickerDownloadStatus()
        {
            SetChampionPicturePickerDownloadStatus(string.Empty, "TextSoftBrush", Brushes.SlateGray);
        }

        private bool ConfirmChampionPictureDownload(ChampionInfo champion)
        {
            if (!_settings.ShowChampionPictureDownloadWarning)
                return true;

            var dialog = new ChampionPictureDownloadWarningWindow(champion.Name, _settings.DownloadRawChampionPictures)
            {
                Owner = Window.GetWindow(this)
            };

            bool confirmed = dialog.ShowDialog() == true;
            if (confirmed && dialog.DontShowAgain)
            {
                _settings.ShowChampionPictureDownloadWarning = false;
                _settings.Save();
            }

            return confirmed;
        }

        private static bool IsDefaultChampionPictureOption(ChampionInfo champion, ChampionTileOption selectedOption)
        {
            ChampionTileOption? defaultOption = ChampionTileCatalog.GetDefaultOption(champion);
            return defaultOption is not null
                && string.Equals(defaultOption.FileName, selectedOption.FileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameChampionPictureFileName(string? first, string? second)
        {
            return string.Equals(
                string.IsNullOrWhiteSpace(first) ? null : first,
                string.IsNullOrWhiteSpace(second) ? null : second,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void SaveChampionPictureSelection(ChampionInfo champion, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                ChampionImageSelectionStore.ClearSelection(champion.Id);
            else
                ChampionImageSelectionStore.SetSelection(champion.Id, fileName);
        }

        private void QueueChampionPictureSelectionSave(ChampionInfo champion, string? fileName)
        {
            Dispatcher.InvokeAsync(
                () => SaveChampionPictureSelection(champion, fileName),
                DispatcherPriority.ContextIdle);
        }
    }
}