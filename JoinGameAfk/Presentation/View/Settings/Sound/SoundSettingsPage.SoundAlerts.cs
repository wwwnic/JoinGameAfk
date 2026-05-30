using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.Validation;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage
    {
        private void LoadSoundAlertOptions()
        {
            _soundOptions.Clear();
            _soundOptions.AddRange(NotificationSoundPlayer.SoundOptions);

            _soundLibraryChoices.Clear();
            _soundLibraryChoices.AddRange(_soundOptions.Select(option => new SoundChoiceOption(option.Key, option.DisplayName, option.IsLoopable)));

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
                RefreshSoundPickerChoices();
                UpdateSoundAlertInputStates();
            }
            finally
            {
                _isApplyingSettingsToControls = false;
            }
        }

        private void RefreshSoundPickerChoices()
        {
            string filterText = SoundPickerSearchBox.Text?.Trim() ?? string.Empty;
            IReadOnlyList<SoundChoiceOption> choices = string.IsNullOrWhiteSpace(filterText)
                ? _soundLibraryChoices
                : _soundLibraryChoices
                    .Where(choice => choice.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            SoundPickerItemsControl.ItemsSource = choices;
            SoundPickerEmptyState.Visibility = choices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void RefreshDirtyState()
        {
            if (_isApplyingSettingsToControls)
                return;

            bool hasDirtySettings = CaptureCurrentSoundSettingsSnapshot() != CaptureSavedSoundSettingsSnapshot();
            DirtySoundBar.Visibility = hasDirtySettings ? Visibility.Visible : Visibility.Collapsed;
            CancelSoundChangesButton.IsEnabled = hasDirtySettings;
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
            SoundPickerChoicesPanel.IsEnabled = alertsEnabled;
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
                option.IsInfinitePlaybackEnabled = option.SupportsInfinitePlayback
                    && (setting.InfinitePlaybackEnabled ?? option.DefaultInfinitePlaybackEnabled);
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
            countdownOption.Description = !countdownOption.IsInfinitePlaybackEnabled
                ? $"Plays {countdownOption.SelectedSoundDisplayName} once at {FormatLeadSeconds(countdownSeconds)} before auto-lock."
                : countdownSeconds > closeSeconds
                ? $"Plays {countdownOption.SelectedSoundDisplayName} from {FormatLeadSeconds(countdownSeconds)} to {FormatLeadSeconds(closeSeconds)} before auto-lock. Replaced by the next countdown cue if enabled."
                : $"Plays {countdownOption.SelectedSoundDisplayName} from {FormatLeadSeconds(countdownSeconds)} until auto-lock when the next countdown cue is off.";
            closeOption.Description = closeOption.IsInfinitePlaybackEnabled
                ? $"Plays {closeOption.SelectedSoundDisplayName} from {FormatLeadSeconds(closeSeconds)} until auto-lock."
                : $"Plays {closeOption.SelectedSoundDisplayName} once at {FormatLeadSeconds(closeSeconds)} before auto-lock.";
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
                        : null,
                    InfinitePlaybackEnabled = option.SupportsInfinitePlayback
                        ? option.IsInfinitePlaybackEnabled
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
                string infinitePlayback = definition.SupportsInfinitePlayback
                    ? (setting.InfinitePlaybackEnabled ?? definition.DefaultInfinitePlaybackEnabled).ToString()
                    : string.Empty;
                return $"{definition.Id}:{setting.Enabled}:{soundKey}:{volume}:{threshold}:{playbackDuration}:{infinitePlayback}";
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
            string infinitePlayback = option.SupportsInfinitePlayback
                ? option.IsInfinitePlaybackEnabled.ToString()
                : string.Empty;
            return $"{option.AlertId}:{option.IsEnabled}:{NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey)}:{volume}:{threshold}:{playbackDuration}:{infinitePlayback}";
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
    }
}
