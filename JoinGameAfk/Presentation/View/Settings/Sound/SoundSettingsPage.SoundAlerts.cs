using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

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
                SoundAlertsEnabledCheckBox.IsChecked = _settings.SoundAlertsEnabled;
                SoundAlertVolumeSlider.Value = SoundSettings.NormalizeSoundAlertVolumePercent(_settings.SoundAlertVolumePercent);
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
                _settings.SoundAlertsEnabled,
                SoundSettings.NormalizeSoundAlertVolumePercent(_settings.SoundAlertVolumePercent),
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
            return SoundSettings.NormalizeSoundAlertVolumePercent((int)Math.Round(SoundAlertVolumeSlider.Value));
        }

        private int GetEffectiveSoundAlertVolumePercent(SoundAlertOption option)
        {
            return SoundSettings.GetEffectiveSoundAlertVolumePercent(
                GetSoundAlertVolumePercent(),
                ParseSoundAlertVolumeOrDefault(option),
                option.SoundKey ?? option.DefaultSoundKey);
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
                option.SoundKey = setting.SoundKey;
                option.VolumeText = SoundSettings.NormalizeSoundAlertCueVolumePercent(
                    setting.VolumePercent,
                    setting.SoundKey ?? option.DefaultSoundKey).ToString();
                option.ThresholdText = option.HasThreshold
                    ? SoundSettings.NormalizeSoundAlertThresholdSeconds(setting.ThresholdSeconds, option.DefaultThresholdSeconds).ToString()
                    : string.Empty;
                option.IsInfinitePlaybackEnabled = option.SupportsInfinitePlayback
                    && (setting.InfinitePlaybackEnabled ?? option.DefaultInfinitePlaybackEnabled);
                option.PlaybackDurationText = option.HasPlaybackDuration
                    ? SoundSettings.NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds).ToString()
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
                ? $"Starts {countdownOption.SelectedSoundDisplayName} {FormatLeadSeconds(countdownSeconds)} before auto-lock."
                : countdownSeconds > closeSeconds
                ? $"Starts {countdownOption.SelectedSoundDisplayName} {FormatLeadSeconds(countdownSeconds)} before auto-lock and plays until {FormatLeadSeconds(closeSeconds)} before auto-lock, then the next countdown cue replaces it if enabled."
                : $"Starts {countdownOption.SelectedSoundDisplayName} {FormatLeadSeconds(countdownSeconds)} before auto-lock and keeps playing until auto-lock when the next countdown cue is off.";
            closeOption.Description = closeOption.IsInfinitePlaybackEnabled
                ? $"Starts {closeOption.SelectedSoundDisplayName} {FormatLeadSeconds(closeSeconds)} before auto-lock and keeps playing until auto-lock."
                : $"Starts {closeOption.SelectedSoundDisplayName} {FormatLeadSeconds(closeSeconds)} before auto-lock.";
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
                    SoundKey = option.HasAssignedSound
                        ? NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey)
                        : null,
                    VolumePercent = SoundSettings.NormalizeSoundAlertCueVolumePercent(
                        ParseSoundAlertVolumeOrDefault(option),
                        option.SoundKey ?? option.DefaultSoundKey),
                    ThresholdSeconds = option.HasThreshold
                        ? SoundSettings.NormalizeSoundAlertThresholdSeconds(ParseSoundAlertThresholdOrDefault(option), option.DefaultThresholdSeconds)
                        : null,
                    PlaybackDurationSeconds = option.HasPlaybackDuration
                        ? SoundSettings.NormalizeSoundAlertPlaybackDurationSeconds(ParseSoundAlertPlaybackDurationOrDefault(option))
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
                string? soundKey = string.IsNullOrWhiteSpace(setting.SoundKey)
                    ? null
                    : NotificationSoundPlayer.NormalizeSoundKey(setting.SoundKey);
                string volume = SoundSettings.NormalizeSoundAlertCueVolumePercent(
                    setting.VolumePercent,
                    soundKey ?? definition.DefaultSoundKey).ToString();
                string threshold = definition.DefaultThresholdSeconds is null
                    ? string.Empty
                    : SoundSettings.NormalizeSoundAlertThresholdSeconds(setting.ThresholdSeconds, definition.DefaultThresholdSeconds.Value).ToString();
                string playbackDuration = definition.DefaultPlaybackDurationSeconds is not null
                    && !string.IsNullOrWhiteSpace(soundKey)
                    && NotificationSoundPlayer.IsLoopableSoundKey(soundKey)
                    ? SoundSettings.NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds).ToString()
                    : string.Empty;
                string infinitePlayback = definition.SupportsInfinitePlayback
                    ? (setting.InfinitePlaybackEnabled ?? definition.DefaultInfinitePlaybackEnabled).ToString()
                    : string.Empty;
                return $"{definition.Id}:{setting.Enabled}:{soundKey ?? "none"}:{volume}:{threshold}:{playbackDuration}:{infinitePlayback}";
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
            string soundKey = option.HasAssignedSound
                ? NotificationSoundPlayer.NormalizeSoundKey(option.SoundKey)
                : "none";
            return $"{option.AlertId}:{option.IsEnabled}:{soundKey}:{volume}:{threshold}:{playbackDuration}:{infinitePlayback}";
        }

        private bool TryValidateSoundAlertSettings()
        {
            if (!AreSoundAlertsEnabled())
                return true;

            foreach (var option in GetSoundAlertOptions())
            {
                if (TryParseSoundAlertVolume(option.VolumeText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} volume must be a whole number between {SoundSettings.MinSoundAlertVolumePercent} and {SoundSettings.MaxSoundAlertVolumePercent}.");
                return false;
            }

            foreach (var option in GetSoundAlertOptions().Where(option => option.HasThreshold))
            {
                if (TryParseSoundAlertThreshold(option.ThresholdText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} cue start must be a whole number between {SoundSettings.MinSoundAlertThresholdSeconds} and {SoundSettings.MaxSoundAlertThresholdSeconds} seconds before auto-lock.");
                return false;
            }

            foreach (var option in GetSoundAlertOptions().Where(option => option.HasPlaybackDuration))
            {
                if (TryParseSoundAlertPlaybackDuration(option.PlaybackDurationText, out _))
                    continue;

                ShowValidationMessage($"{option.DisplayName} play time must be a whole number between {SoundSettings.MinSoundAlertPlaybackDurationSeconds} and {SoundSettings.MaxSoundAlertPlaybackDurationSeconds}.");
                return false;
            }

            return true;
        }

        private static int ParseSoundAlertVolumeOrDefault(SoundAlertOption option)
        {
            return TryParseSoundAlertVolume(option.VolumeText, out int volume)
                ? volume
                : SoundSettings.GetDefaultSoundAlertCueVolumePercent(option.SoundKey ?? option.DefaultSoundKey);
        }

        private static bool TryParseSoundAlertVolume(string? text, out int volume)
        {
            volume = 0;
            if (!int.TryParse(text, out int value))
                return false;

            if (value < SoundSettings.MinSoundAlertVolumePercent
                || value > SoundSettings.MaxSoundAlertVolumePercent)
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

            if (value < SoundSettings.MinSoundAlertThresholdSeconds
                || value > SoundSettings.MaxSoundAlertThresholdSeconds)
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

            if (value < SoundSettings.MinSoundAlertPlaybackDurationSeconds
                || value > SoundSettings.MaxSoundAlertPlaybackDurationSeconds)
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
