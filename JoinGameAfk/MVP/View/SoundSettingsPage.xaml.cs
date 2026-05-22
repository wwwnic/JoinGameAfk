using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.Validation;

namespace JoinGameAfk.View
{
    public partial class SoundSettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly ChampSelectSettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly NotificationSoundPlayer _notificationSoundPlayer;
        private readonly List<NotificationSoundOption> _soundOptions = [];
        private readonly List<SoundAlertGroupOption> _soundAlertGroups = [];
        private SoundAlertOption? _activeSoundPickerOption;
        private bool _isApplyingSettingsToControls;

        public SoundSettingsPage(ChampSelectSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            _notificationSoundPlayer = new NotificationSoundPlayer(ShowValidationMessage);
            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                FloatingSoundStatusBar.Visibility = Visibility.Collapsed;
            };

            _settings.Saved += Settings_Saved;
            Unloaded += SoundSettingsPage_Unloaded;
            LoadSoundAlertOptions();
            ApplySettingsToControls();
            SelectSoundAlertOption(GetSoundAlertOptions().FirstOrDefault());
            AttachDirtyStateTracking();
            RefreshDirtyState();
        }

        private void AttachDirtyStateTracking()
        {
            SoundAlertVolumeSlider.ValueChanged += SoundAlertVolumeSlider_ValueChanged;
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                if (DirtySoundBar.Visibility == Visibility.Visible)
                    return;

                ApplySettingsToControls();
                RefreshDirtyState();
            });
        }

        private void SoundSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _settings.Saved -= Settings_Saved;
            Unloaded -= SoundSettingsPage_Unloaded;
        }

        private void LoadSoundAlertOptions()
        {
            _soundOptions.Clear();
            _soundOptions.AddRange(NotificationSoundPlayer.SoundOptions);

            _soundAlertGroups.Clear();
            _soundAlertGroups.AddRange(SoundAlertDefaults.Definitions
                .GroupBy(definition => definition.GroupName)
                .Select(group => new SoundAlertGroupOption(
                    group.Key,
                    group.Select(definition => new SoundAlertOption(definition, _soundOptions)).ToList())));

            SoundAlertGroupItemsControl.ItemsSource = _soundAlertGroups;
        }

        private void ApplySettingsToControls()
        {
            _isApplyingSettingsToControls = true;
            try
            {
                SoundAlertsEnabledCheckBox.IsChecked = _settings.SoundAlertProfile != SoundAlertProfile.Off;
                SoundAlertVolumeSlider.Value = ChampSelectSettings.NormalizeSoundAlertVolumePercent(_settings.SoundAlertVolumePercent);
                ApplySoundAlertSettingsToRows();
                RefreshSoundAlertVolumeValueText();
                RefreshActiveSoundAlertEditor();
                UpdateSoundAlertInputStates();
            }
            finally
            {
                _isApplyingSettingsToControls = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryValidateSoundAlertSettings())
                return;

            _settings.SoundAlertProfile = AreSoundAlertsEnabled()
                ? SoundAlertProfile.Custom
                : SoundAlertProfile.Off;
            _settings.SoundAlertVolumePercent = GetSoundAlertVolumePercent();
            _settings.SoundAlerts = CaptureSoundAlertSettings();
            SyncLegacyReadyCheckSoundSettings();
            _settings.Save();

            RefreshDirtyState();
            ShowSavedMessage();
        }

        private void ResetSoundDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Restore default sound alert mode, volume, sounds, and warning timing?",
                "Reset Sounds",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
                return;

            _settings.ResetSoundAlertOptionsToDefaults();
            ApplySettingsToControls();
            _settings.Save();

            RefreshDirtyState();
            ShowDefaultsRestoredMessage();
        }

        private void SoundAlertVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshSoundAlertVolumeValueText();
            NotificationSoundPlayer.SetActivePlayerVolume(_activeSoundPickerOption is null
                ? GetSoundAlertVolumePercent()
                : GetEffectiveSoundAlertVolumePercent(_activeSoundPickerOption));
            RefreshDirtyState();
        }

        private void SoundAlertsEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSoundAlertInputStates();
            RefreshDirtyState();
        }

        private void SoundAlertRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SoundAlertOption selectedOption })
                SelectSoundAlertOption(selectedOption);
        }

        private void SoundPickerChoiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSoundPickerOption is null
                || sender is not FrameworkElement { DataContext: SoundChoiceOption soundChoice })
                return;

            _activeSoundPickerOption.IsEnabled = true;
            _activeSoundPickerOption.SoundKey = soundChoice.Key;
            RefreshLockCountdownDescriptions();
            _notificationSoundPlayer.PreviewAlert(
                soundChoice.Key,
                GetEffectiveSoundAlertVolumePercent(_activeSoundPickerOption),
                $"{_activeSoundPickerOption.DisplayName} sound preview",
                GetSoundAlertPlaybackDurationSecondsOrNull(_activeSoundPickerOption));
            RefreshSoundPickerChoices();
            RefreshDirtyState();
        }

        private void ClearSoundAlertButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SoundAlertOption option })
                return;

            option.IsEnabled = false;
            SelectSoundAlertOption(option);
            SyncSoundAlertsEnabledFromRows();
            RefreshDirtyState();
            e.Handled = true;
        }

        private void SoundPickerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshSoundPickerChoices();
        }

        private void SoundPickerSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (SoundPickerSearchBox.Text.Length > 0)
                SoundPickerSearchBox.Clear();

            e.Handled = true;
        }

        private void SelectSoundAlertOption(SoundAlertOption? option)
        {
            _activeSoundPickerOption = null;
            foreach (var alertOption in GetSoundAlertOptions())
            {
                bool isSelected = ReferenceEquals(alertOption, option);
                alertOption.IsSelected = isSelected;
                if (isSelected)
                    _activeSoundPickerOption = alertOption;
            }

            RefreshActiveSoundAlertEditor();
            UpdateSoundAlertInputStates();
        }

        private void RefreshActiveSoundAlertEditor()
        {
            if (_activeSoundPickerOption is null)
            {
                SoundPickerItemsControl.ItemsSource = null;
                SoundPickerEmptyState.Visibility = Visibility.Collapsed;
                return;
            }

            _activeSoundPickerOption.RefreshSelectedSound();
            RefreshSoundPickerChoices();
        }

        private void RefreshSoundPickerChoices()
        {
            if (_activeSoundPickerOption is null)
            {
                SoundPickerItemsControl.ItemsSource = null;
                SoundPickerEmptyState.Visibility = Visibility.Collapsed;
                return;
            }

            string filterText = SoundPickerSearchBox.Text?.Trim() ?? string.Empty;
            IReadOnlyList<SoundChoiceOption> choices = string.IsNullOrWhiteSpace(filterText)
                ? _activeSoundPickerOption.SoundChoices
                : _activeSoundPickerOption.SoundChoices
                    .Where(choice => choice.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            SoundPickerItemsControl.ItemsSource = choices;
            SoundPickerEmptyState.Visibility = choices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SoundAlertThresholdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertThresholdBox(sender as TextBox);
            RefreshLockCountdownDescriptions();
            RefreshDirtyState();
        }

        private void SoundAlertVolumeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertVolumeBox(sender as TextBox);
            RefreshDirtyState();
        }

        private void SoundAlertPlaybackDurationBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertPlaybackDurationBox(sender as TextBox);
            RefreshDirtyState();
        }

        private void RefreshDirtyState()
        {
            if (_isApplyingSettingsToControls)
                return;

            bool hasDirtySettings = CaptureCurrentSoundSettingsSnapshot() != CaptureSavedSoundSettingsSnapshot();
            DirtySoundBar.Visibility = hasDirtySettings ? Visibility.Visible : Visibility.Collapsed;
            if (hasDirtySettings)
            {
                _savedMessageTimer.Stop();
                FloatingSoundStatusBar.Visibility = Visibility.Collapsed;
            }
        }

        private SoundSettingsSnapshot CaptureCurrentSoundSettingsSnapshot()
        {
            return new SoundSettingsSnapshot(
                AreSoundAlertsEnabled(),
                GetSoundAlertVolumePercent(),
                CaptureCurrentSoundAlertConfigurationSnapshot());
        }

        private SoundSettingsSnapshot CaptureSavedSoundSettingsSnapshot()
        {
            return new SoundSettingsSnapshot(
                _settings.SoundAlertProfile != SoundAlertProfile.Off,
                ChampSelectSettings.NormalizeSoundAlertVolumePercent(_settings.SoundAlertVolumePercent),
                CaptureSavedSoundAlertConfigurationSnapshot());
        }

        private bool AreSoundAlertsEnabled()
        {
            return SoundAlertsEnabledCheckBox.IsChecked == true;
        }

        private void SyncSoundAlertsEnabledFromRows()
        {
            if (_isApplyingSettingsToControls || !AreSoundAlertsEnabled())
                return;

            if (GetSoundAlertOptions().Any(option => option.IsEnabled))
                return;

            SoundAlertsEnabledCheckBox.IsChecked = false;
        }

        private int GetSoundAlertVolumePercent()
        {
            return ChampSelectSettings.NormalizeSoundAlertVolumePercent((int)Math.Round(SoundAlertVolumeSlider.Value));
        }

        private int GetEffectiveSoundAlertVolumePercent(SoundAlertOption option)
        {
            return ChampSelectSettings.GetEffectiveSoundAlertVolumePercent(
                GetSoundAlertVolumePercent(),
                ParseSoundAlertVolumeOrDefault(option));
        }

        private void RefreshSoundAlertVolumeValueText()
        {
            if (SoundAlertVolumeValueText is null)
                return;

            SoundAlertVolumeValueText.Text = $"{GetSoundAlertVolumePercent()}%";
        }

        private void UpdateSoundAlertInputStates()
        {
            if (CustomAlertsSection is null
                || SoundAlertCustomPanel is null
                || SoundAlertsMasterRow is null
                || SoundPickerChoicesPanel is null)
            {
                return;
            }

            bool alertsEnabled = AreSoundAlertsEnabled();
            UpdateSoundAlertMasterRowState(alertsEnabled);
            SoundAlertVolumeSlider.IsEnabled = alertsEnabled;
            CustomAlertsSection.Visibility = Visibility.Visible;
            SoundAlertCustomPanel.Visibility = Visibility.Visible;
            SoundAlertCustomPanel.IsEnabled = alertsEnabled;
            SoundAlertCustomPanel.Opacity = alertsEnabled ? 1 : 0.58;
            SoundPickerChoicesPanel.IsEnabled = alertsEnabled && _activeSoundPickerOption is not null;
            SoundPickerChoicesPanel.Opacity = alertsEnabled ? 1 : 0.58;
        }

        private void UpdateSoundAlertMasterRowState(bool enabled)
        {
            SoundAlertsMasterRow.Background = TryFindResource(enabled ? "AppInputFocusBrush" : "DangerSurfaceDeepBrush") as Brush
                ?? (enabled ? Brushes.Transparent : Brushes.DarkRed);
            SoundAlertsMasterRow.BorderBrush = TryFindResource(enabled ? "AccentBlueBrush" : "DangerAccentBrush") as Brush
                ?? (enabled ? Brushes.DodgerBlue : Brushes.IndianRed);
        }

        private void ApplySoundAlertSettingsToRows()
        {
            _settings.NormalizeSoundAlertOptions();
            foreach (var option in GetSoundAlertOptions())
            {
                var setting = _settings.GetSoundAlertSetting(option.AlertId);
                option.IsEnabled = setting.Enabled;
                option.SoundKey = NotificationSoundPlayer.NormalizeSoundKey(setting.SoundKey);
                option.VolumeText = ChampSelectSettings.NormalizeSoundAlertVolumePercent(setting.VolumePercent).ToString();
                option.ThresholdText = option.HasThreshold
                    ? ChampSelectSettings.NormalizeSoundAlertThresholdSeconds(setting.ThresholdSeconds, option.DefaultThresholdSeconds).ToString()
                    : string.Empty;
                option.PlaybackDurationText = option.HasPlaybackDuration
                    ? ChampSelectSettings.NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds).ToString()
                    : SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds.ToString();
                option.RefreshSelectedSound();
            }

            RefreshLockCountdownDescriptions();
        }

        private void RefreshLockCountdownDescriptions()
        {
            RefreshLockCountdownDescriptions(
                SoundAlertIds.PickLockCountdown,
                SoundAlertIds.PickLockSoon);
            RefreshLockCountdownDescriptions(
                SoundAlertIds.BanLockCountdown,
                SoundAlertIds.BanLockSoon);
        }

        private void RefreshLockCountdownDescriptions(string countdownAlertId, string closeAlertId)
        {
            var countdownOption = FindSoundAlertOption(countdownAlertId);
            var closeOption = FindSoundAlertOption(closeAlertId);
            if (countdownOption is null || closeOption is null)
                return;

            int countdownSeconds = ParseSoundAlertThresholdOrDefault(countdownOption);
            int closeSeconds = ParseSoundAlertThresholdOrDefault(closeOption);
            countdownOption.Description = countdownSeconds > closeSeconds
                ? $"Plays {countdownOption.SelectedSoundDisplayName} from {FormatLeadSeconds(countdownSeconds)} to {FormatLeadSeconds(closeSeconds)} before auto-lock. Replaced by the next countdown cue if enabled."
                : $"Plays {countdownOption.SelectedSoundDisplayName} from {FormatLeadSeconds(countdownSeconds)} until auto-lock when the next countdown cue is off.";
            closeOption.Description = $"Plays {closeOption.SelectedSoundDisplayName} from {FormatLeadSeconds(closeSeconds)} until auto-lock.";
        }

        private SoundAlertOption? FindSoundAlertOption(string alertId)
        {
            return GetSoundAlertOptions().FirstOrDefault(option => string.Equals(option.AlertId, alertId, StringComparison.Ordinal));
        }

        private static string FormatLeadSeconds(int seconds)
        {
            return $"{Math.Max(0, seconds)}s";
        }

        private Dictionary<string, SoundAlertSetting> CaptureSoundAlertSettings()
        {
            return GetSoundAlertOptions().ToDictionary(
                option => option.AlertId,
                option => new SoundAlertSetting
                {
                    Enabled = option.IsEnabled,
                    SoundKey = NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey),
                    VolumePercent = ChampSelectSettings.NormalizeSoundAlertVolumePercent(ParseSoundAlertVolumeOrDefault(option)),
                    ThresholdSeconds = option.HasThreshold
                        ? ChampSelectSettings.NormalizeSoundAlertThresholdSeconds(ParseSoundAlertThresholdOrDefault(option), option.DefaultThresholdSeconds)
                        : null,
                    PlaybackDurationSeconds = option.HasPlaybackDuration
                        ? ChampSelectSettings.NormalizeSoundAlertPlaybackDurationSeconds(ParseSoundAlertPlaybackDurationOrDefault(option))
                        : null
                },
                StringComparer.Ordinal);
        }

        private string CaptureCurrentSoundAlertConfigurationSnapshot()
        {
            return string.Join("|", GetSoundAlertOptions().Select(CreateSoundAlertSnapshot));
        }

        private string CaptureSavedSoundAlertConfigurationSnapshot()
        {
            _settings.NormalizeSoundAlertOptions();
            return string.Join("|", SoundAlertDefaults.Definitions.Select(definition =>
            {
                var setting = _settings.GetSoundAlertSetting(definition.Id);
                string soundKey = NotificationSoundPlayer.NormalizeSoundKey(setting.SoundKey);
                string volume = ChampSelectSettings.NormalizeSoundAlertVolumePercent(setting.VolumePercent).ToString();
                string threshold = definition.DefaultThresholdSeconds is null
                    ? string.Empty
                    : ChampSelectSettings.NormalizeSoundAlertThresholdSeconds(setting.ThresholdSeconds, definition.DefaultThresholdSeconds.Value).ToString();
                string playbackDuration = definition.DefaultPlaybackDurationSeconds is not null
                    && NotificationSoundPlayer.IsLoopableSoundKey(soundKey)
                    ? ChampSelectSettings.NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds).ToString()
                    : string.Empty;
                return $"{definition.Id}:{setting.Enabled}:{soundKey}:{volume}:{threshold}:{playbackDuration}";
            }));
        }

        private static string CreateSoundAlertSnapshot(SoundAlertOption option)
        {
            string volume = CreateNumericSnapshotText(option.VolumeText);
            string threshold = option.HasThreshold
                ? CreateNumericSnapshotText(option.ThresholdText)
                : string.Empty;
            string playbackDuration = option.HasPlaybackDuration
                ? CreateNumericSnapshotText(option.PlaybackDurationText)
                : string.Empty;
            return $"{option.AlertId}:{option.IsEnabled}:{NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey)}:{volume}:{threshold}:{playbackDuration}";
        }

        private bool TryValidateSoundAlertSettings()
        {
            if (!AreSoundAlertsEnabled())
                return true;

            foreach (var option in GetSoundAlertOptions())
            {
                if (TryParseSoundAlertVolume(option.VolumeText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} volume must be a whole number between {ChampSelectSettings.MinSoundAlertVolumePercent} and {ChampSelectSettings.MaxSoundAlertVolumePercent}.");
                return false;
            }

            foreach (var option in GetSoundAlertOptions().Where(option => option.HasThreshold))
            {
                if (TryParseSoundAlertThreshold(option.ThresholdText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} lead time must be a whole number between {ChampSelectSettings.MinSoundAlertThresholdSeconds} and {ChampSelectSettings.MaxSoundAlertThresholdSeconds}.");
                return false;
            }

            foreach (var option in GetSoundAlertOptions().Where(option => option.HasPlaybackDuration))
            {
                if (TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} play time must be a whole number between {ChampSelectSettings.MinSoundAlertPlaybackDurationSeconds} and {ChampSelectSettings.MaxSoundAlertPlaybackDurationSeconds}.");
                return false;
            }

            return true;
        }

        private void ValidateSoundAlertThresholdBox(TextBox? textBox)
        {
            if (textBox is null)
                return;

            bool isValid = textBox.DataContext is not SoundAlertOption option
                || !option.HasThreshold
                || TryParseSoundAlertThreshold(option.ThresholdText, out _);
            InputValidator.SetValidationState(textBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private void ValidateSoundAlertVolumeBox(TextBox? textBox)
        {
            if (textBox is null)
                return;

            bool isValid = textBox.DataContext is not SoundAlertOption option
                || TryParseSoundAlertVolume(option.VolumeText, out _);
            InputValidator.SetValidationState(textBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private void ValidateSoundAlertPlaybackDurationBox(TextBox? textBox)
        {
            if (textBox is null)
                return;

            bool isValid = textBox.DataContext is not SoundAlertOption option
                || !option.HasPlaybackDuration
                || TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out _);
            InputValidator.SetValidationState(textBox, isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private static int ParseSoundAlertVolumeOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertVolume(option.VolumeText, out int volume)
                ? volume
                : ChampSelectSettings.DefaultSoundAlertVolumePercent;
        }

        private static bool TryParseSoundAlertVolume(string? text, out int volume)
        {
            volume = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < ChampSelectSettings.MinSoundAlertVolumePercent
                || value > ChampSelectSettings.MaxSoundAlertVolumePercent)
            {
                return false;
            }

            volume = value;
            return true;
        }

        private static int ParseSoundAlertThresholdOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertThreshold(option.ThresholdText, out int threshold)
                ? threshold
                : option.DefaultThresholdSeconds;
        }

        private static bool TryParseSoundAlertThreshold(string? text, out int threshold)
        {
            threshold = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < ChampSelectSettings.MinSoundAlertThresholdSeconds
                || value > ChampSelectSettings.MaxSoundAlertThresholdSeconds)
            {
                return false;
            }

            threshold = value;
            return true;
        }

        private static int? GetSoundAlertPlaybackDurationSecondsOrNull(SoundAlertOption option)
        {
            return option.HasPlaybackDuration
                ? ParseSoundAlertPlaybackDurationOrDefault(option)
                : null;
        }

        private static int ParseSoundAlertPlaybackDurationOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out int duration)
                ? duration
                : SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds;
        }

        private static bool TryParseSoundAlertPlaybackDuration(string? text, out int playbackDuration)
        {
            playbackDuration = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < ChampSelectSettings.MinSoundAlertPlaybackDurationSeconds
                || value > ChampSelectSettings.MaxSoundAlertPlaybackDurationSeconds)
            {
                return false;
            }

            playbackDuration = value;
            return true;
        }

        private static string CreateNumericSnapshotText(string? text)
        {
            return int.TryParse(text, out int value)
                ? value.ToString()
                : $"invalid:{text}";
        }

        private IEnumerable<SoundAlertOption> GetSoundAlertOptions()
        {
            return _soundAlertGroups.SelectMany(group => group.Alerts);
        }

        private void SyncLegacyReadyCheckSoundSettings()
        {
            var readyCheckSetting = _settings.GetSoundAlertSetting(SoundAlertIds.ReadyCheck);
            _settings.ReadyCheckSoundNotificationEnabled = _settings.IsSoundAlertActive(SoundAlertIds.ReadyCheck);
            _settings.ReadyCheckSoundNotificationKey = NotificationSoundPlayer.NormalizeSoundKey(readyCheckSetting.SoundKey);
            _settings.ReadyCheckSoundNotificationVolumePercent = _settings.GetSoundAlertEffectiveVolumePercent(SoundAlertIds.ReadyCheck);
        }

        private void ShowSavedMessage()
        {
            ShowStatusMessage("Sound settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowDefaultsRestoredMessage()
        {
            ShowStatusMessage("Default sounds restored.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowValidationMessage(string message)
        {
            ShowStatusMessage(message, "DangerTextBrush", Brushes.IndianRed);
        }

        private void ShowStatusMessage(string message, string brushResourceKey, Brush fallbackBrush)
        {
            _savedMessageTimer.Stop();
            var messageBrush = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            FloatingSoundStatusText.Text = message;
            FloatingSoundStatusText.Foreground = messageBrush;
            FloatingSoundStatusBar.BorderBrush = messageBrush;
            FloatingSoundStatusBar.Visibility = Visibility.Visible;
            _savedMessageTimer.Start();
        }

        private sealed record SoundAlertGroupOption(string DisplayName, IReadOnlyList<SoundAlertOption> Alerts);

        private sealed class SoundAlertOption : INotifyPropertyChanged
        {
            private bool _isEnabled;
            private bool _isSelected;
            private readonly bool _supportsPlaybackDuration;
            private string _soundKey;
            private string _description;
            private string _selectedSoundDisplayName = string.Empty;
            private bool _hasPlaybackDuration;
            private string _volumeText;
            private string _thresholdText;
            private string _playbackDurationText;

            public SoundAlertOption(SoundAlertDefinition definition, IReadOnlyList<NotificationSoundOption> soundOptions)
            {
                AlertId = definition.Id;
                DisplayName = definition.DisplayName;
                _description = definition.Description;
                HasThreshold = definition.DefaultThresholdSeconds is not null;
                DefaultThresholdSeconds = definition.DefaultThresholdSeconds ?? SoundAlertDefaults.DefaultLockSoonThresholdSeconds;
                _supportsPlaybackDuration = definition.DefaultPlaybackDurationSeconds is not null;
                SoundChoices = soundOptions.Select(option => new SoundChoiceOption(option.Key, option.DisplayName, option.IsLoopable)).ToList();
                _isEnabled = definition.EnabledInMinimal;
                _soundKey = NotificationSoundPlayer.NormalizeSoundKey(definition.DefaultSoundKey);
                _volumeText = ChampSelectSettings.DefaultSoundAlertVolumePercent.ToString();
                _thresholdText = definition.DefaultThresholdSeconds?.ToString() ?? string.Empty;
                _playbackDurationText = (definition.DefaultPlaybackDurationSeconds ?? SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds).ToString();
                RefreshSelectedSound();
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string AlertId { get; }
            public string DisplayName { get; }
            public string Description
            {
                get => _description;
                set
                {
                    if (string.Equals(_description, value, StringComparison.Ordinal))
                        return;

                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
            public bool HasThreshold { get; }
            public int DefaultThresholdSeconds { get; }
            public bool HasPlaybackDuration
            {
                get => _hasPlaybackDuration;
                private set
                {
                    if (_hasPlaybackDuration == value)
                        return;

                    _hasPlaybackDuration = value;
                    OnPropertyChanged(nameof(HasPlaybackDuration));
                }
            }

            public IReadOnlyList<SoundChoiceOption> SoundChoices { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }

            public string SelectedSoundDisplayName
            {
                get => _selectedSoundDisplayName;
                private set
                {
                    if (string.Equals(_selectedSoundDisplayName, value, StringComparison.Ordinal))
                        return;

                    _selectedSoundDisplayName = value;
                    OnPropertyChanged(nameof(SelectedSoundDisplayName));
                }
            }

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (_isEnabled == value)
                        return;

                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }

            public string SoundKey
            {
                get => _soundKey;
                set
                {
                    string normalizedSoundKey = NotificationSoundPlayer.NormalizeSoundKey(value);
                    if (string.Equals(_soundKey, normalizedSoundKey, StringComparison.Ordinal))
                        return;

                    _soundKey = normalizedSoundKey;
                    OnPropertyChanged(nameof(SoundKey));
                    RefreshSelectedSound();
                }
            }

            public string VolumeText
            {
                get => _volumeText;
                set
                {
                    if (string.Equals(_volumeText, value, StringComparison.Ordinal))
                        return;

                    _volumeText = value;
                    OnPropertyChanged(nameof(VolumeText));
                }
            }

            public string ThresholdText
            {
                get => _thresholdText;
                set
                {
                    if (string.Equals(_thresholdText, value, StringComparison.Ordinal))
                        return;

                    _thresholdText = value;
                    OnPropertyChanged(nameof(ThresholdText));
                }
            }

            public string PlaybackDurationText
            {
                get => _playbackDurationText;
                set
                {
                    if (string.Equals(_playbackDurationText, value, StringComparison.Ordinal))
                        return;

                    _playbackDurationText = value;
                    OnPropertyChanged(nameof(PlaybackDurationText));
                }
            }

            public void RefreshSelectedSound()
            {
                SoundChoiceOption? selectedChoice = null;
                foreach (var choice in SoundChoices)
                {
                    bool isSelected = string.Equals(choice.Key, SoundKey, StringComparison.Ordinal);
                    choice.IsSelected = isSelected;
                    if (isSelected)
                        selectedChoice = choice;
                }

                SelectedSoundDisplayName = selectedChoice?.DisplayName
                    ?? SoundChoices.FirstOrDefault()?.DisplayName
                    ?? "Default";
                HasPlaybackDuration = _supportsPlaybackDuration && selectedChoice?.IsLoopable == true;
                if (HasPlaybackDuration && string.IsNullOrWhiteSpace(PlaybackDurationText))
                    PlaybackDurationText = SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds.ToString();
            }

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private sealed class SoundChoiceOption : INotifyPropertyChanged
        {
            private bool _isSelected;

            public SoundChoiceOption(string key, string displayName, bool isLoopable)
            {
                Key = key;
                DisplayName = displayName;
                IsLoopable = isLoopable;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string Key { get; }
            public string DisplayName { get; }
            public bool IsLoopable { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        private readonly record struct SoundSettingsSnapshot(
            bool SoundAlertsEnabled,
            int SoundAlertVolumePercent,
            string SoundAlertConfiguration);
    }
}
