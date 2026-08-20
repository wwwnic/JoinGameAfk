using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private void ProfilesOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ProfilesDialogBorder is null || ProfilesBackdropBrush is null)
                return;

            double opacityPercent = Math.Clamp(e.NewValue, 40, 100);
            double dialogOpacity = opacityPercent / 100d;
            ProfilesDialogBorder.Opacity = dialogOpacity;
            ProfilesBackdropBrush.Opacity = 0.5 * ((dialogOpacity - 0.4) / 0.6);
            if (ProfilesOpacityValueTextBlock is not null)
                ProfilesOpacityValueTextBlock.Text = $"{opacityPercent:0}%";
        }

        private void OpenProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            SetChampionPictureEditMode(false);
            if (ChampionDataOverlay.Visibility == Visibility.Visible)
                CloseChampionDataManager();
            if (ChampionPicturePickerOverlay.Visibility == Visibility.Visible)
                CloseChampionPicturePicker();

            HideProfileStatus();
            ReloadProfiles();
            ProfilesOverlay.Visibility = Visibility.Visible;
            ProfilesCloseButton.Focus();
            e.Handled = true;
        }

        private void CloseProfiles()
        {
            ProfilesOverlay.Visibility = Visibility.Collapsed;
            AddProfileValidationTextBlock.Text = string.Empty;
            HideProfileStatus();
            FocusPriorityPage();
        }

        private void ProfilesCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseProfiles();
            e.Handled = true;
        }

        private void ProfilesOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, ProfilesOverlay))
                return;

            CloseProfiles();
            e.Handled = true;
        }

        private void ReloadProfiles(Guid? selectedProfileId = null)
        {
            selectedProfileId ??= (ProfilesListBox.SelectedItem as RolePlanProfileListItem)?.Profile.Id;
            RolePlanProfileListItem? selection;
            _isReloadingProfiles = true;
            try
            {
                _profiles.Clear();
                foreach (var profile in _rolePlanProfileStore.LoadProfiles())
                    _profiles.Add(new RolePlanProfileListItem(profile, LoadProfileImage(_rolePlanProfileStore.GetIconPath(profile))));

                ProfilesListBox.ItemsSource = _profiles;
                ProfilesEmptyPanel.Visibility = _profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                selection = selectedProfileId.HasValue
                    ? _profiles.FirstOrDefault(item => item.Profile.Id == selectedProfileId.Value)
                    : _profiles.FirstOrDefault();
                selection ??= _profiles.FirstOrDefault();
                ProfilesListBox.SelectedItem = selection;
            }
            finally
            {
                _isReloadingProfiles = false;
            }

            RefreshProfileSelectionActionStates();
            if (selection is null)
                ShowCreateProfileEditor(clearStatus: false);
            else
                ShowUpdateProfileEditor(selection.Profile, clearStatus: false);
        }

        private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshProfileSelectionActionStates();
            if (_isReloadingProfiles)
                return;

            if (ProfilesListBox.SelectedItem is RolePlanProfileListItem item)
                ShowUpdateProfileEditor(item.Profile, clearStatus: true);
            else
                ShowCreateProfileEditor(clearStatus: true);
        }

        private void ProfilesListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source
                || ItemsControl.ContainerFromElement(ProfilesListBox, source) is not ListBoxItem
                || ProfilesListBox.SelectedItem is not RolePlanProfileListItem item)
            {
                return;
            }

            LoadProfile(item.Profile);
        }

        private void ProfilesListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || ProfilesListBox.SelectedItem is not RolePlanProfileListItem item)
                return;

            LoadProfile(item.Profile);
            e.Handled = true;
        }

        private void RefreshProfileSelectionActionStates()
        {
            int selectedIndex = ProfilesListBox.SelectedIndex;
            bool hasSelection = selectedIndex >= 0;
            DeleteProfileButton.IsEnabled = hasSelection;
            MoveProfileUpButton.IsEnabled = selectedIndex > 0;
            MoveProfileDownButton.IsEnabled = selectedIndex >= 0 && selectedIndex < _profiles.Count - 1;
        }

        private void MoveProfileUpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedProfile(-1);
            e.Handled = true;
        }

        private void MoveProfileDownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedProfile(1);
            e.Handled = true;
        }

        private void MoveSelectedProfile(int offset)
        {
            if (ProfilesListBox.SelectedItem is not RolePlanProfileListItem item)
                return;

            try
            {
                _rolePlanProfileStore.MoveProfile(item.Profile.Id, offset);
                ReloadProfiles(item.Profile.Id);
            }
            catch (Exception ex)
            {
                ShowProfileStatus($"Unable to move this profile: {ex.Message}", isError: true);
                _logErrorMessage?.Invoke($"Unable to move role plan profile: {ex}");
            }
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedItem is null)
                ShowCreateProfileEditor(clearStatus: true);
            else
                ProfilesListBox.SelectedItem = null;

            ProfileNameTextBox.Focus();
            e.Handled = true;
        }

        private void ShowCreateProfileEditor(bool clearStatus)
        {
            _editingProfileId = null;
            ProfileNameTextBox.Clear();
            ProfileIncludePlansCheckBox.IsChecked = true;
            ProfileIncludePicturesCheckBox.IsChecked = true;
            ClearProfileIconChampionSelection(clearSearch: true);
            UpdateProfileIconChampionSearchResults();
            AddProfileValidationTextBlock.Text = string.Empty;
            ProfileEditorTitleTextBlock.Text = "Create profile";
            ProfileEditorDescriptionTextBlock.Text =
                $"Create a named copy of the current {GetGameModeDisplayName(_activeRolePlanMode)} Role Plan page.";
            SaveProfileButton.Content = "Create profile";
            SaveProfileButton.ToolTip = "Save the current Role Plan page as a new profile.";

            if (clearStatus)
                HideProfileStatus();
        }

        private void ShowUpdateProfileEditor(RolePlanProfile profile, bool clearStatus)
        {
            _editingProfileId = profile.Id;
            ProfileNameTextBox.Text = profile.Name;
            ProfileIncludePlansCheckBox.IsChecked =
                profile.IncludedSections.HasFlag(RolePlanProfileSections.RolePlans);
            ProfileIncludePicturesCheckBox.IsChecked =
                profile.IncludedSections.HasFlag(RolePlanProfileSections.ChampionPictures);
            _selectedProfileIconChampion = profile.IconChampionId.HasValue
                ? _allChampions.FirstOrDefault(champion => champion.Key == profile.IconChampionId.Value)
                : null;
            ProfileIconChampionSearchTextBox.Clear();
            UpdateProfileIconChampionSearchResults();
            AddProfileValidationTextBlock.Text = string.Empty;
            ProfileEditorTitleTextBlock.Text = "Update profile";
            ProfileEditorDescriptionTextBlock.Text =
                $"Edit this {GetGameModeDisplayName(profile.GameMode)} profile, or save the current page over its stored content.";
            SaveProfileButton.Content = "Save changes";
            SaveProfileButton.ToolTip =
                "Update this profile with these fields and the current Role Plan page.";

            if (clearStatus)
                HideProfileStatus();
        }

        private void ProfileIconChampionSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateProfileIconChampionSearchResults();
            RefreshProfileIconClearButtonState();
        }

        private void UpdateProfileIconChampionSearchResults()
        {
            if (_allChampions is null || ProfileIconChampionResultsListBox is null)
                return;

            string search = ProfileIconChampionSearchTextBox?.Text.Trim() ?? string.Empty;
            IEnumerable<ChampionInfo> availableChampions = _activeRolePlanMode == LeagueGameMode.Classic
                ? _allChampions.Where(champion => champion.SupportsLeagueClassic == true)
                : _allChampions;
            IEnumerable<ChampionInfo> matches = availableChampions;
            if (string.IsNullOrWhiteSpace(search))
            {
                IEnumerable<ChampionInfo> randomized = availableChampions
                    .Where(champion => champion.Key != _selectedProfileIconChampion?.Key)
                    .OrderBy(_ => Random.Shared.Next());
                matches = _selectedProfileIconChampion is null
                    ? randomized
                    : new[] { _selectedProfileIconChampion }.Concat(randomized);
            }
            else
            {
                matches = matches
                    .Select(champion => new
                    {
                        Champion = champion,
                        Score = GetChampionSearchScore(champion, search)
                    })
                    .Where(result => result.Score >= 0)
                    .OrderBy(result => result.Score)
                    .ThenBy(result => result.Champion.Name)
                    .Select(result => result.Champion);
            }

            var items = matches
                .Take(3)
                .Select(champion => new ProfileChampionSearchItem(champion, _activeRolePlanMode))
                .ToList();
            ProfileIconChampionResultsListBox.ItemsSource = items;
            ProfileChampionSearchItem? selectedItem = _selectedProfileIconChampion is null
                ? null
                : items.FirstOrDefault(item => item.Champion.Key == _selectedProfileIconChampion.Key);
            ProfileIconChampionResultsListBox.SelectedItem = selectedItem ?? items.FirstOrDefault();
        }

        private void ProfileIconChampionResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileIconChampionResultsListBox.SelectedItem is not ProfileChampionSearchItem item)
                return;

            _selectedProfileIconChampion = item.Champion;
            RefreshProfileIconClearButtonState();
        }

        private void ClearProfileIconChampionButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileIconChampionSearchTextBox.Clear();
            ProfileIconChampionSearchTextBox.Focus();
            e.Handled = true;
        }

        private void ClearProfileIconChampionSelection(bool clearSearch)
        {
            _selectedProfileIconChampion = null;
            if (ProfileIconChampionResultsListBox is not null)
                ProfileIconChampionResultsListBox.SelectedItem = null;
            if (clearSearch && ProfileIconChampionSearchTextBox is not null)
                ProfileIconChampionSearchTextBox.Clear();
            RefreshProfileIconClearButtonState();
        }

        private void RefreshProfileIconClearButtonState()
        {
            if (ClearProfileIconChampionButton is null)
                return;

            ClearProfileIconChampionButton.IsEnabled =
                !string.IsNullOrWhiteSpace(ProfileIconChampionSearchTextBox?.Text);
        }

        private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            AddProfileValidationTextBlock.Text = string.Empty;
            HideProfileStatus();
            string name = ProfileNameTextBox.Text.Trim();
            if (name.Length == 0)
            {
                ShowAddProfileValidation("Enter a profile name.");
                return;
            }

            RolePlanProfileSections sections = RolePlanProfileSections.None;
            if (ProfileIncludePlansCheckBox.IsChecked == true)
                sections |= RolePlanProfileSections.RolePlans;
            if (ProfileIncludePicturesCheckBox.IsChecked == true)
                sections |= RolePlanProfileSections.ChampionPictures;
            if (sections == RolePlanProfileSections.None)
            {
                ShowAddProfileValidation("Choose Role plans, Champion pictures, or both.");
                return;
            }

            ChampionInfo? iconChampion = _selectedProfileIconChampion;
            if (iconChampion is null)
            {
                ShowAddProfileValidation("Choose an identifying tile.");
                return;
            }

            string? iconSourcePath = null;
            ChampionTileOption? selectedTile = ChampionTileCatalog.GetSelectedOption(
                iconChampion,
                _activeRolePlanMode);
            if (selectedTile is null)
            {
                ShowAddProfileValidation("The selected champion does not have a local picture yet.");
                return;
            }

            iconSourcePath = Path.Combine(ChampionTileCatalog.TileDirectoryPath, selectedTile.FileName);
            if (_editingProfileId is Guid profileId && !ConfirmProfileUpdate(profileId))
            {
                e.Handled = true;
                return;
            }

            try
            {
                FlushPendingPreferenceSave();
                bool isUpdate;
                RolePlanProfile profile;
                if (_editingProfileId is Guid editingProfileId)
                {
                    isUpdate = true;
                    profile = _rolePlanProfileStore.UpdateProfile(
                        editingProfileId,
                        name,
                        sections,
                        CaptureCurrentRolePlans(),
                        ChampionImageSelectionStore.GetSelections(_activeRolePlanMode),
                        iconChampion.Key,
                        iconSourcePath,
                        _activeRolePlanMode);
                }
                else
                {
                    isUpdate = false;
                    profile = _rolePlanProfileStore.AddProfile(
                        name,
                        sections,
                        CaptureCurrentRolePlans(),
                        ChampionImageSelectionStore.GetSelections(_activeRolePlanMode),
                        iconChampion.Key,
                        iconSourcePath,
                        _activeRolePlanMode);
                }

                ReloadProfiles(profile.Id);
                ShowProfileStatus(isUpdate
                    ? $"Saved changes to “{profile.Name}”."
                    : $"Created “{profile.Name}”.");
            }
            catch (Exception ex)
            {
                ShowAddProfileValidation($"Unable to save this profile: {ex.Message}");
                _logErrorMessage?.Invoke($"Unable to save role plan profile: {ex}");
            }

            e.Handled = true;
        }

        private bool ConfirmProfileUpdate(Guid profileId)
        {
            RolePlanProfile? existingProfile = _profiles
                .FirstOrDefault(item => item.Profile.Id == profileId)
                ?.Profile;
            string profileName = existingProfile?.Name ?? ProfileNameTextBox.Text.Trim();
            string message =
                $"Save changes to “{profileName}”?\n\n"
                + "This replaces its saved name, included sections, identifying tile, role plans, and champion pictures with the current setup. Unchecked sections are removed. This cannot be undone.";

            Window? owner = Window.GetWindow(this);
            MessageBoxResult result = owner is null
                ? MessageBox.Show(
                    message,
                    "Save Profile Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No)
                : MessageBox.Show(
                    owner,
                    message,
                    "Save Profile Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }

        private void ShowAddProfileValidation(string message)
        {
            HideProfileStatus();
            AddProfileValidationTextBlock.Text = message;
            ProfileNameTextBox.Focus();
        }

        private void HideProfileStatus()
        {
            _profileStatusTimer.Stop();
            ProfileActionStatusBorder.Visibility = Visibility.Collapsed;
            ProfileActionStatusTextBlock.Text = string.Empty;
        }

        private void ShowProfileStatus(string message, bool isError = false)
        {
            ProfileActionStatusTextBlock.Text = message;
            ProfileActionStatusBorder.Height = isError ? 54 : 20;
            ProfileActionStatusBorder.Margin = new Thickness(0, 0, 0, isError ? 52 : 46);
            double verticalPadding = isError ? 4 : 0;
            ProfileActionStatusBorder.Padding = new Thickness(10, verticalPadding, 10, verticalPadding);
            ProfileActionStatusTextBlock.Foreground = TryFindResource(
                isError ? "DangerTextBrush" : "AccentGreenTextBrush") as Brush
                ?? (isError ? Brushes.IndianRed : Brushes.SeaGreen);
            ProfileActionStatusBorder.BorderBrush = TryFindResource(
                isError ? "DangerAccentBrush" : "AccentGreenBrush") as Brush
                ?? (isError ? Brushes.IndianRed : Brushes.SeaGreen);
            ProfileActionStatusBorder.Visibility = Visibility.Visible;
            _profileStatusTimer.Stop();
            _profileStatusTimer.Start();
        }

        private Dictionary<Position, PositionPreference> CaptureCurrentRolePlans()
        {
            return _rows.ToDictionary(
                row => row.Position,
                row => new PositionPreference
                {
                    PickChampionIds = row.PickChampions.Select(champion => champion.ChampionId).ToList(),
                    BanChampionIds = row.BanChampions.Select(champion => champion.ChampionId).ToList()
                });
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedItem is not RolePlanProfileListItem item)
                return;

            Window? owner = Window.GetWindow(this);
            MessageBoxResult result = owner is null
                ? MessageBox.Show(
                    $"Delete the profile “{item.Name}” ({item.SavedText})?\n\nThis cannot be undone.",
                    "Delete Profile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No)
                : MessageBox.Show(
                    owner,
                    $"Delete the profile “{item.Name}” ({item.SavedText})?\n\nThis cannot be undone.",
                    "Delete Profile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                _rolePlanProfileStore.DeleteProfile(item.Profile.Id);
                ReloadProfiles();
                ShowProfileStatus($"Deleted “{item.Name}”.");
            }
            catch (Exception ex)
            {
                ShowProfileStatus($"Unable to delete this profile: {ex.Message}", isError: true);
                _logErrorMessage?.Invoke($"Unable to delete role plan profile: {ex}");
            }

            e.Handled = true;
        }

        private void LoadProfile(RolePlanProfile profile)
        {
            if (!IsPriorityEditingEnabled)
                return;

            SetRolePlanMode(profile.GameMode);
            ShowUpdateProfileEditor(profile, clearStatus: false);
            Dictionary<Position, PositionPreference> previousPlans = CaptureCurrentRolePlans();
            IReadOnlyDictionary<int, string> previousPictures =
                ChampionImageSelectionStore.GetSelections(_activeRolePlanMode);
            bool plansWereApplied = false;
            bool picturesWereApplied = false;
            try
            {
                FlushPendingPreferenceSave();
                if (profile.IncludedSections.HasFlag(RolePlanProfileSections.RolePlans))
                {
                    plansWereApplied = true;
                    _rolePlanSettings.ReplacePreferences(profile.RolePlans ?? [], _activeRolePlanMode);
                    _rolePlanSettings.Save();
                    ReloadRolePlanRows();
                }

                if (profile.IncludedSections.HasFlag(RolePlanProfileSections.ChampionPictures))
                {
                    picturesWereApplied = true;
                    ChampionImageSelectionStore.ReplaceSelections(
                        profile.ChampionPictures ?? [],
                        _activeRolePlanMode);
                }

                RefreshChampionImages();
                ShowProfileStatus($"Loaded “{profile.Name}” into the Role Plan page.");
            }
            catch (Exception ex)
            {
                bool rolledBack = TryRollbackProfileApply(
                    previousPlans,
                    previousPictures,
                    plansWereApplied,
                    picturesWereApplied);
                string message = rolledBack
                    ? $"Unable to load this profile. Your previous setup was restored: {ex.Message}"
                    : $"Unable to load this profile, and the previous setup could not be fully restored: {ex.Message}";
                ShowProfileStatus(message, isError: true);
                _logErrorMessage?.Invoke($"Unable to load role plan profile: {ex}");
            }
        }

        private bool TryRollbackProfileApply(
            IReadOnlyDictionary<Position, PositionPreference> previousPlans,
            IReadOnlyDictionary<int, string> previousPictures,
            bool plansWereApplied,
            bool picturesWereApplied)
        {
            bool succeeded = true;
            if (plansWereApplied)
            {
                try
                {
                    _rolePlanSettings.ReplacePreferences(previousPlans, _activeRolePlanMode);
                    _rolePlanSettings.Save();
                    ReloadRolePlanRows();
                }
                catch (Exception rollbackException)
                {
                    succeeded = false;
                    _logErrorMessage?.Invoke($"Unable to restore role plans after a profile apply failure: {rollbackException}");
                }
            }

            if (picturesWereApplied)
            {
                try
                {
                    ChampionImageSelectionStore.ReplaceSelections(previousPictures, _activeRolePlanMode);
                }
                catch (Exception rollbackException)
                {
                    succeeded = false;
                    _logErrorMessage?.Invoke($"Unable to restore champion pictures after a profile apply failure: {rollbackException}");
                }
            }

            RefreshChampionImages();
            return succeeded;
        }

        private static string GetGameModeDisplayName(LeagueGameMode gameMode)
        {
            return gameMode == LeagueGameMode.Classic ? "LoL Classic" : "LoL";
        }

        private void ReloadRolePlanRows()
        {
            ClearChampionSelection();
            ClearInsertionIndicator();
            foreach (var row in _rows)
            {
                PositionPreference preference = _rolePlanSettings
                    .GetPreferences(_activeRolePlanMode)
                    .GetValueOrDefault(row.Position)
                    ?? new PositionPreference();
                row.PickChampions.Clear();
                row.BanChampions.Clear();
                foreach (int championId in preference.PickChampionIds)
                    row.PickChampions.Add(CreateSelectionItem(row, championId, isPick: true));
                foreach (int championId in preference.BanChampionIds)
                    row.BanChampions.Add(CreateSelectionItem(row, championId, isPick: false));
                UpdateRowTextFromCollection(row, isPick: true);
                UpdateRowTextFromCollection(row, isPick: false);
            }

            if (_rows.Count > 0)
                SetActiveTarget(_rows[0], isPick: true, focusSearch: false);
        }

        private static ImageSource? LoadProfileImage(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(filePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class RolePlanProfileListItem
    {
        public RolePlanProfileListItem(RolePlanProfile profile, ImageSource? thumbnailImageSource)
        {
            Profile = profile;
            ThumbnailImageSource = thumbnailImageSource;
        }

        public RolePlanProfile Profile { get; }
        public string Name => Profile.Name;
        public ImageSource? ThumbnailImageSource { get; }
        public string ListsActionText => Profile.IncludedSections.HasFlag(RolePlanProfileSections.RolePlans)
            ? $"{(Profile.GameMode == LeagueGameMode.Classic ? "Classic" : "LoL")} lists: change"
            : "Lists: unchanged";
        public string TilesActionText => Profile.IncludedSections.HasFlag(RolePlanProfileSections.ChampionPictures)
            ? "Tiles: change"
            : "Tiles: unchanged";
        public string SavedText =>
            $"{(Profile.GameMode == LeagueGameMode.Classic ? "LoL Classic" : "LoL")} · Saved {Profile.UpdatedAtUtc.ToLocalTime():g}";
    }

    internal sealed class ProfileChampionSearchItem
    {
        public ProfileChampionSearchItem(ChampionInfo champion, LeagueGameMode gameMode)
        {
            Champion = champion;
            PortraitImageSource = ChampionTileCatalog.GetSelectedOption(champion, gameMode)?.ImageSource;
        }

        public ChampionInfo Champion { get; }
        public string Name => Champion.Name;
        public string ChampionIdText => $"ID {Champion.Key}";
        public ImageSource? PortraitImageSource { get; }
    }
}
