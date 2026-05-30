using JoinGameAfk.Constant;
using JoinGameAfk.Services;

namespace JoinGameAfk.Model
{
    public sealed class SoundSettings
    {
        public const int MinSoundAlertVolumePercent = 0;
        public const int MaxSoundAlertVolumePercent = 100;
        public const int DefaultSoundAlertMasterVolumePercent = 50;
        public const int DefaultSoundAlertCueVolumePercent = 100;
        public const int DefaultLoudSoundAlertCueVolumePercent = 50;
        public const int MinSoundAlertThresholdSeconds = 0;
        public const int MaxSoundAlertThresholdSeconds = 30;
        public const int MinSoundAlertPlaybackDurationSeconds = 1;
        public const int MaxSoundAlertPlaybackDurationSeconds = 30;

        public int Version { get; set; } = AppStorage.SoundSettingsFileVersion;

        /// <summary>
        /// Master switch for all sound alerts.
        /// </summary>
        public bool SoundAlertsEnabled { get; set; } = true;

        /// <summary>
        /// Shared volume percentage used for all sound alerts and previews.
        /// </summary>
        public int? SoundAlertVolumePercent { get; set; } = DefaultSoundAlertMasterVolumePercent;

        /// <summary>
        /// Per-alert sound and timing settings.
        /// </summary>
        public Dictionary<string, SoundAlertSetting> SoundAlerts { get; set; } = SoundAlertDefaults.CreateDefaultSettings();

        public event Action? Saved;

        public bool IsSoundAlertActive(string alertId)
        {
            var setting = GetSoundAlertSetting(alertId);
            return SoundAlertsEnabled
                && setting.Enabled
                && !string.IsNullOrWhiteSpace(setting.SoundKey);
        }

        public string? GetSoundAlertSoundKey(string alertId)
        {
            var setting = GetSoundAlertSetting(alertId);
            return string.IsNullOrWhiteSpace(setting.SoundKey)
                ? null
                : setting.SoundKey.Trim();
        }

        public int? GetSoundAlertThresholdSeconds(string alertId)
        {
            var definition = SoundAlertDefaults.GetDefinition(alertId);
            if (definition.DefaultThresholdSeconds is null)
                return null;

            return NormalizeSoundAlertThresholdSeconds(GetSoundAlertSetting(alertId).ThresholdSeconds, definition.DefaultThresholdSeconds.Value);
        }

        public int? GetSoundAlertPlaybackDurationSeconds(string alertId)
        {
            var definition = SoundAlertDefaults.GetDefinition(alertId);
            if (definition.DefaultPlaybackDurationSeconds is null)
                return null;

            var setting = GetSoundAlertSetting(alertId);
            if (setting.PlaybackDurationSeconds is null)
                return definition.DefaultPlaybackDurationSeconds;

            return NormalizeSoundAlertPlaybackDurationSeconds(setting.PlaybackDurationSeconds);
        }

        public bool IsSoundAlertInfinitePlaybackEnabled(string alertId)
        {
            var definition = SoundAlertDefaults.GetDefinition(alertId);
            if (!definition.SupportsInfinitePlayback)
                return false;

            return GetSoundAlertSetting(alertId).InfinitePlaybackEnabled
                ?? definition.DefaultInfinitePlaybackEnabled;
        }

        public int GetSoundAlertEffectiveVolumePercent(string alertId)
        {
            var setting = GetSoundAlertSetting(alertId);
            return GetEffectiveSoundAlertVolumePercent(
                SoundAlertVolumePercent,
                setting.VolumePercent,
                setting.SoundKey);
        }

        public SoundAlertSetting GetSoundAlertSetting(string alertId)
        {
            SoundAlerts ??= SoundAlertDefaults.CreateDefaultSettings();

            if (SoundAlerts.TryGetValue(alertId, out var setting))
                return setting;

            return SoundAlertDefaults.CreateDefaultSetting(alertId);
        }

        public void ResetSoundAlertOptionsToDefaults()
        {
            var defaults = new SoundSettings();

            SoundAlertsEnabled = defaults.SoundAlertsEnabled;
            SoundAlertVolumePercent = defaults.SoundAlertVolumePercent;
            SoundAlerts = SoundAlertDefaults.CreateDefaultSettings();
        }

        public void Save()
        {
            JsonSettingsStore.Save(AppStorage.SoundSettingsFilePath, this, NormalizeSettings);
            Saved?.Invoke();
        }

        public static SoundSettings Load()
        {
            return JsonSettingsStore.Load(AppStorage.SoundSettingsFilePath, () => new SoundSettings(), NormalizeSettings);
        }

        public void NormalizeSoundAlertOptions()
        {
            SoundAlertVolumePercent = NormalizeSoundAlertVolumePercent(SoundAlertVolumePercent);

            var currentAlerts = SoundAlerts ?? new Dictionary<string, SoundAlertSetting>(StringComparer.Ordinal);
            var normalizedAlerts = SoundAlertDefaults.CreateDefaultSettings();
            foreach (var definition in SoundAlertDefaults.Definitions)
            {
                if (!currentAlerts.TryGetValue(definition.Id, out var currentAlert) || currentAlert is null)
                    continue;

                string? configuredSoundKey = currentAlert.SoundKey?.Trim();
                bool hasConfiguredSoundKey = !string.IsNullOrWhiteSpace(configuredSoundKey);
                var normalizedAlert = normalizedAlerts[definition.Id];
                normalizedAlert.Enabled = currentAlert.Enabled && hasConfiguredSoundKey;
                normalizedAlert.SoundKey = !hasConfiguredSoundKey
                    ? null
                    : configuredSoundKey;

                normalizedAlert.VolumePercent = NormalizeSoundAlertCueVolumePercent(
                    currentAlert.VolumePercent,
                    normalizedAlert.SoundKey ?? definition.DefaultSoundKey);
                normalizedAlert.ThresholdSeconds = definition.DefaultThresholdSeconds is null
                    ? null
                    : NormalizeSoundAlertThresholdSeconds(currentAlert.ThresholdSeconds, definition.DefaultThresholdSeconds.Value);
                normalizedAlert.PlaybackDurationSeconds = definition.DefaultPlaybackDurationSeconds is null
                    ? null
                    : NormalizeSoundAlertPlaybackDurationSeconds(currentAlert.PlaybackDurationSeconds ?? definition.DefaultPlaybackDurationSeconds);
                normalizedAlert.InfinitePlaybackEnabled = definition.SupportsInfinitePlayback
                    ? currentAlert.InfinitePlaybackEnabled ?? definition.DefaultInfinitePlaybackEnabled
                    : null;
            }

            SoundAlerts = normalizedAlerts;
        }

        public static int NormalizeSoundAlertVolumePercent(int? volumePercent)
        {
            return NormalizeSoundAlertMasterVolumePercent(volumePercent);
        }

        public static int NormalizeSoundAlertMasterVolumePercent(int? volumePercent)
        {
            if (volumePercent is null)
                return DefaultSoundAlertMasterVolumePercent;

            return Math.Clamp(
                volumePercent.Value < MinSoundAlertVolumePercent ? DefaultSoundAlertMasterVolumePercent : volumePercent.Value,
                MinSoundAlertVolumePercent,
                MaxSoundAlertVolumePercent);
        }

        public static int NormalizeSoundAlertCueVolumePercent(int? volumePercent, string? soundKey)
        {
            int fallbackVolumePercent = GetDefaultSoundAlertCueVolumePercent(soundKey);
            if (volumePercent is null)
                return fallbackVolumePercent;

            return Math.Clamp(
                volumePercent.Value < MinSoundAlertVolumePercent ? fallbackVolumePercent : volumePercent.Value,
                MinSoundAlertVolumePercent,
                MaxSoundAlertVolumePercent);
        }

        public static int GetEffectiveSoundAlertVolumePercent(int? masterVolumePercent, int? alertVolumePercent)
        {
            return GetEffectiveSoundAlertVolumePercent(masterVolumePercent, alertVolumePercent, soundKey: null);
        }

        public static int GetEffectiveSoundAlertVolumePercent(int? masterVolumePercent, int? alertVolumePercent, string? soundKey)
        {
            int normalizedMasterVolume = NormalizeSoundAlertVolumePercent(masterVolumePercent);
            int normalizedAlertVolume = NormalizeSoundAlertCueVolumePercent(alertVolumePercent, soundKey);
            return Math.Clamp(
                (int)Math.Round(normalizedMasterVolume * (normalizedAlertVolume / 100d)),
                MinSoundAlertVolumePercent,
                MaxSoundAlertVolumePercent);
        }

        public static int GetDefaultSoundAlertCueVolumePercent(string? soundKey)
        {
            string normalizedSoundKey = string.IsNullOrWhiteSpace(soundKey)
                ? string.Empty
                : soundKey.Trim();
            return string.Equals(normalizedSoundKey, SoundAlertDefaults.DefaultSoundKey, StringComparison.Ordinal)
                ? DefaultLoudSoundAlertCueVolumePercent
                : DefaultSoundAlertCueVolumePercent;
        }

        public static int NormalizeSoundAlertThresholdSeconds(int? thresholdSeconds)
        {
            return NormalizeSoundAlertThresholdSeconds(thresholdSeconds, SoundAlertDefaults.DefaultLockSoonThresholdSeconds);
        }

        public static int NormalizeSoundAlertThresholdSeconds(int? thresholdSeconds, int defaultThresholdSeconds)
        {
            if (thresholdSeconds is null)
                return NormalizeSoundAlertThresholdFallbackSeconds(defaultThresholdSeconds);

            int fallbackThresholdSeconds = NormalizeSoundAlertThresholdFallbackSeconds(defaultThresholdSeconds);
            return Math.Clamp(
                thresholdSeconds.Value < MinSoundAlertThresholdSeconds ? fallbackThresholdSeconds : thresholdSeconds.Value,
                MinSoundAlertThresholdSeconds,
                MaxSoundAlertThresholdSeconds);
        }

        public static int NormalizeSoundAlertPlaybackDurationSeconds(int? playbackDurationSeconds)
        {
            if (playbackDurationSeconds is null)
                return SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds;

            return Math.Clamp(
                playbackDurationSeconds.Value < MinSoundAlertPlaybackDurationSeconds
                    ? SoundAlertDefaults.DefaultLoopPlaybackDurationSeconds
                    : playbackDurationSeconds.Value,
                MinSoundAlertPlaybackDurationSeconds,
                MaxSoundAlertPlaybackDurationSeconds);
        }

        private static void NormalizeSettings(SoundSettings settings)
        {
            settings.Version = AppStorage.SoundSettingsFileVersion;
            settings.NormalizeSoundAlertOptions();
        }

        private static int NormalizeSoundAlertThresholdFallbackSeconds(int thresholdSeconds)
        {
            return Math.Clamp(
                thresholdSeconds,
                MinSoundAlertThresholdSeconds,
                MaxSoundAlertThresholdSeconds);
        }
    }
}
