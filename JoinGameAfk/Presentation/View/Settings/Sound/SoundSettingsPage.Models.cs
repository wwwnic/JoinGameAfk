using System.ComponentModel;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage
    {
        private sealed record SoundDragData(
            string SoundKey,
            string DisplayName,
            bool IsLoopable,
            SoundChoiceOption? SourceChoice,
            SoundAlertOption? SourceAlert)
        {
            public static SoundDragData FromChoice(SoundChoiceOption choice)
            {
                return new SoundDragData(choice.Key, choice.DisplayName, choice.IsLoopable, choice, null);
            }

            public static SoundDragData FromAlert(SoundAlertOption option)
            {
                string soundKey = option.SoundKey ?? option.DefaultSoundKey;
                return new SoundDragData(
                    soundKey,
                    option.SelectedSoundDisplayName,
                    NotificationSoundPlayer.IsLoopableSoundKey(soundKey),
                    null,
                    option);
            }
        }

        private sealed record SoundAlertGroupOption(string DisplayName, IReadOnlyList<SoundAlertOption> Alerts);

        private sealed class SoundAlertOption : INotifyPropertyChanged
        {
            private bool _isEnabled;
            private bool _isSoundDropTarget;
            private bool _isSoundDragging;
            private readonly bool _supportsPlaybackDuration;
            private readonly bool _defaultInfinitePlaybackEnabled;
            private string? _soundKey;
            private string _description;
            private string _selectedSoundDisplayName = string.Empty;
            private bool _hasPlaybackDuration;
            private bool _isInfinitePlaybackEnabled;
            private string _volumeText;
            private string _thresholdText;
            private string _playbackDurationText;

            public SoundAlertOption(SoundAlertDefinition definition, IReadOnlyList<NotificationSoundOption> soundOptions)
            {
                AlertId = definition.Id;
                DisplayName = definition.DisplayName;
                DefaultIsEnabled = definition.EnabledByDefault;
                DefaultSoundKey = NotificationSoundPlayer.NormalizeSoundKey(definition.DefaultSoundKey);
                DefaultVolumePercent = SoundSettings.GetDefaultSoundAlertCueVolumePercent(DefaultSoundKey);
                _description = definition.Description;
                HasThreshold = definition.DefaultThresholdSeconds is not null;
                DefaultThresholdSeconds = definition.DefaultThresholdSeconds ?? SoundAlertDefaults.DefaultLockSoonThresholdSeconds;
                _supportsPlaybackDuration = definition.DefaultPlaybackDurationSeconds is not null;
                SupportsInfinitePlayback = definition.SupportsInfinitePlayback;
                _defaultInfinitePlaybackEnabled = definition.DefaultInfinitePlaybackEnabled;
                SoundChoices = soundOptions.Select(option => new SoundChoiceOption(option.Key, option.DisplayName, option.IsLoopable)).ToList();
                _isEnabled = DefaultIsEnabled;
                _isInfinitePlaybackEnabled = SupportsInfinitePlayback && _defaultInfinitePlaybackEnabled;
                _soundKey = DefaultSoundKey;
                _volumeText = DefaultVolumePercent.ToString();
                _thresholdText = definition.DefaultThresholdSeconds?.ToString() ?? string.Empty;
                _playbackDurationText = (definition.DefaultPlaybackDurationSeconds ?? SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds).ToString();
                RefreshSelectedSound();
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string AlertId { get; }
            public string DisplayName { get; }
            public bool DefaultIsEnabled { get; }
            public string DefaultSoundKey { get; }
            public int DefaultVolumePercent { get; }
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
            public bool SupportsInfinitePlayback { get; }
            public bool DefaultInfinitePlaybackEnabled => _defaultInfinitePlaybackEnabled;
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

            public bool IsSoundDropTarget
            {
                get => _isSoundDropTarget;
                set
                {
                    if (_isSoundDropTarget == value)
                        return;

                    _isSoundDropTarget = value;
                    OnPropertyChanged(nameof(IsSoundDropTarget));
                }
            }

            public bool IsSoundDragging
            {
                get => _isSoundDragging;
                set
                {
                    if (_isSoundDragging == value)
                        return;

                    _isSoundDragging = value;
                    OnPropertyChanged(nameof(IsSoundDragging));
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
            public bool HasAssignedSound => !string.IsNullOrWhiteSpace(SoundKey);

            public bool IsInfinitePlaybackEnabled
            {
                get => _isInfinitePlaybackEnabled;
                set
                {
                    bool normalizedValue = SupportsInfinitePlayback && value;
                    if (_isInfinitePlaybackEnabled == normalizedValue)
                        return;

                    _isInfinitePlaybackEnabled = normalizedValue;
                    OnPropertyChanged(nameof(IsInfinitePlaybackEnabled));
                }
            }

            public string? SoundKey
            {
                get => _soundKey;
                set
                {
                    string? normalizedSoundKey = NormalizeOptionalSoundKey(value);
                    if (string.Equals(_soundKey, normalizedSoundKey, StringComparison.Ordinal))
                        return;

                    _soundKey = normalizedSoundKey;
                    OnPropertyChanged(nameof(SoundKey));
                    OnPropertyChanged(nameof(HasAssignedSound));
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
                if (!HasAssignedSound)
                {
                    SelectedSoundDisplayName = "No sound";
                    HasPlaybackDuration = false;
                    return;
                }

                SoundChoiceOption? selectedChoice = SoundChoices.FirstOrDefault(
                    choice => string.Equals(choice.Key, SoundKey, StringComparison.Ordinal));

                SelectedSoundDisplayName = selectedChoice?.DisplayName
                    ?? SoundChoices.FirstOrDefault()?.DisplayName
                    ?? "Default";
                HasPlaybackDuration = _supportsPlaybackDuration && selectedChoice?.IsLoopable == true;
                if (HasPlaybackDuration && string.IsNullOrWhiteSpace(PlaybackDurationText))
                    PlaybackDurationText = SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds.ToString();
            }

            public void RestoreDefaults()
            {
                IsEnabled = DefaultIsEnabled;
                SoundKey = DefaultSoundKey;
                VolumeText = DefaultVolumePercent.ToString();
                ThresholdText = HasThreshold ? DefaultThresholdSeconds.ToString() : string.Empty;
                PlaybackDurationText = SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds.ToString();
                IsInfinitePlaybackEnabled = SupportsInfinitePlayback && DefaultInfinitePlaybackEnabled;
            }

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            private static string? NormalizeOptionalSoundKey(string? soundKey)
            {
                return string.IsNullOrWhiteSpace(soundKey)
                    ? null
                    : NotificationSoundPlayer.NormalizeSoundKey(soundKey);
            }
        }

        private sealed class SoundChoiceOption : INotifyPropertyChanged
        {
            private bool _isDragging;
            private bool _isLastPreviewed;

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

            public bool IsDragging
            {
                get => _isDragging;
                set
                {
                    if (_isDragging == value)
                        return;

                    _isDragging = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
                }
            }

            public bool IsLastPreviewed
            {
                get => _isLastPreviewed;
                set
                {
                    if (_isLastPreviewed == value)
                        return;

                    _isLastPreviewed = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLastPreviewed)));
                }
            }
        }

        private readonly record struct SoundSettingsSnapshot(
            bool SoundAlertsEnabled,
            int SoundAlertVolumePercent,
            string SoundAlertConfiguration);
    }
}
