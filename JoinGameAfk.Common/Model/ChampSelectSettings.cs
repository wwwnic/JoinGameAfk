using System.Text.Json;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;

namespace JoinGameAfk.Model
{
    public class PositionPreference
    {
        public List<int> PickChampionIds { get; set; } = [];
        public List<int> BanChampionIds { get; set; } = [];
    }

    public class ChampSelectSettings
    {
        public const int MinSoundAlertVolumePercent = 0;
        public const int MaxSoundAlertVolumePercent = 100;
        public const int DefaultSoundAlertVolumePercent = 50;
        public const int MinSoundAlertThresholdSeconds = 0;
        public const int MaxSoundAlertThresholdSeconds = 30;
        public const int MinSoundAlertPlaybackDurationSeconds = 1;
        public const int MaxSoundAlertPlaybackDurationSeconds = 30;

        public int Version { get; set; } = AppStorage.SettingsFileVersion;

        /// <summary>
        /// Whether the watcher should start automatically when the app starts.
        /// </summary>
        public bool StartWatcherOnStartup { get; set; } = false;

        /// <summary>
        /// Whether the app should perform in-queue automation.
        /// </summary>
        public bool InQueueAutomationEnabled { get; set; } = true;

        /// <summary>
        /// Whether the app should automatically accept ready checks.
        /// </summary>
        public bool AutoReadyCheckEnabled { get; set; } = true;

        /// <summary>
        /// Preset that controls which sound alerts are active.
        /// </summary>
        public SoundAlertProfile SoundAlertProfile { get; set; } = SoundAlertProfile.Minimal;

        /// <summary>
        /// Shared volume percentage used for all sound alerts and previews.
        /// </summary>
        public int? SoundAlertVolumePercent { get; set; } = DefaultSoundAlertVolumePercent;

        /// <summary>
        /// Per-alert sound and timing settings used when the Custom profile is active.
        /// </summary>
        public Dictionary<string, SoundAlertSetting> SoundAlerts { get; set; } = SoundAlertDefaults.CreateDefaultSettings();

        /// <summary>
        /// Number of seconds to wait before automatically accepting a ready check.
        /// This gives the player time to manually accept or decline first.
        /// </summary>
        public int ReadyCheckAcceptDelaySeconds { get; set; } = 5;

        /// <summary>
        /// Lock the pick when this many seconds (or fewer) remain on the timer. 0 = lock immediately.
        /// </summary>
        public int PickLockDelaySeconds { get; set; } = 11;

        /// <summary>
        /// Whether the app should perform champion select automation.
        /// </summary>
        public bool ChampionSelectAutomationEnabled { get; set; } = true;

        /// <summary>
        /// Whether the app should automatically hover configured champions during pick or ban.
        /// </summary>
        public bool AutoHoverChampionEnabled { get; set; } = true;

        /// <summary>
        /// Number of seconds to wait before automatically hovering a configured champion during pick or ban.
        /// </summary>
        public int ChampionHoverDelaySeconds { get; set; } = 2;

        /// <summary>
        /// Minimum number of seconds to wait before automatically hovering a configured champion during planning.
        /// </summary>
        public int PlanningHoverDelaySeconds { get; set; } = 10;

        /// <summary>
        /// Lock the ban when this many seconds (or fewer) remain on the timer. 0 = lock immediately.
        /// </summary>
        public int BanLockDelaySeconds { get; set; } = 11;

        /// <summary>
        /// Whether the app should automatically lock the currently selected pick or ban before the timer reaches 0.
        /// </summary>
        public bool AutoLockSelectionEnabled { get; set; } = true;

        /// <summary>
        /// Regular League Client polling interval in milliseconds.
        /// </summary>
        public int ChampSelectPollIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Whether live LCU websocket events should be used as the primary League Client refresh source.
        /// </summary>
        public bool UseChampSelectEventStream { get; set; } = true;

        /// <summary>
        /// Whether regular safety polling should run while live LCU events are connected.
        /// </summary>
        public bool ChampSelectEventFallbackPollingEnabled { get; set; }

        /// <summary>
        /// Safety polling interval in milliseconds while live LCU events are enabled.
        /// </summary>
        public int ChampSelectEventFallbackPollIntervalMs { get; set; } = 5000;

        /// <summary>
        /// Visual theme selected for the WPF application.
        /// </summary>
        public string ThemeKey { get; set; } = "draft-desk";

        /// <summary>
        /// Whether the app should check Riot Data Dragon for champion list and picture updates when it starts.
        /// </summary>
        public bool AutoUpdateChampionCatalogOnStartup { get; set; }

        /// <summary>
        /// Whether the champion picture picker should warn before downloading all images for one champion.
        /// </summary>
        public bool ShowChampionPictureDownloadWarning { get; set; } = true;

        /// <summary>
        /// Whether Data Dragon champion picture downloads should keep Riot's original jpg files instead of resized app-cache copies.
        /// </summary>
        public bool DownloadRawChampionPictures { get; set; }

        public Dictionary<Position, PositionPreference> Preferences { get; set; } = new()
        {
            { Position.Default, new PositionPreference() },
            { Position.Top, new PositionPreference() },
            { Position.Jungle, new PositionPreference() },
            { Position.Mid, new PositionPreference() },
            { Position.Adc, new PositionPreference() },
            { Position.Support, new PositionPreference() },
        };

        public PositionPreference GetPreference(Position position)
        {
            position = NormalizePreferencePosition(position);

            if (Preferences.TryGetValue(position, out var pref) && (pref.PickChampionIds.Count > 0 || pref.BanChampionIds.Count > 0))
                return pref;

            return Preferences.GetValueOrDefault(Position.Default) ?? new PositionPreference();
        }

        public List<int> GetMergedPickChampionIds(Position position)
        {
            return GetMergedChampionIds(position, pref => pref.PickChampionIds);
        }

        public List<int> GetMergedBanChampionIds(Position position)
        {
            return GetMergedChampionIds(position, pref => pref.BanChampionIds);
        }

        private List<int> GetMergedChampionIds(Position position, Func<PositionPreference, List<int>> selector)
        {
            position = NormalizePreferencePosition(position);

            var rolePref = position != Position.Default
                && Preferences.TryGetValue(position, out var rp)
                ? selector(rp)
                : [];

            var defaultPref = Preferences.TryGetValue(Position.Default, out var dp)
                ? selector(dp)
                : [];

            if (rolePref.Count == 0)
                return [.. defaultPref];

            var seen = new HashSet<int>(rolePref);
            var merged = new List<int>(rolePref);
            foreach (var id in defaultPref)
            {
                if (seen.Add(id))
                    merged.Add(id);
            }

            return merged;
        }

        private static Position NormalizePreferencePosition(Position position)
        {
            return position == Position.None
                ? Position.Default
                : position;
        }

        public event Action? Saved;

        public bool IsInQueueAutomationActive()
        {
            return InQueueAutomationEnabled && AutoReadyCheckEnabled;
        }

        public bool IsChampionSelectAutomationActive()
        {
            return ChampionSelectAutomationEnabled
                && (AutoHoverChampionEnabled || AutoLockSelectionEnabled);
        }

        public bool IsSoundAlertActive(string alertId)
        {
            return SoundAlertProfile switch
            {
                SoundAlertProfile.Off => false,
                SoundAlertProfile.Minimal => SoundAlertDefaults.IsEnabledInMinimal(alertId),
                SoundAlertProfile.Custom => GetSoundAlertSetting(alertId).Enabled,
                _ => false
            };
        }

        public string GetSoundAlertSoundKey(string alertId)
        {
            var setting = GetSoundAlertSetting(alertId);
            return string.IsNullOrWhiteSpace(setting.SoundKey)
                ? SoundAlertDefaults.GetDefinition(alertId).DefaultSoundKey
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
            return GetEffectiveSoundAlertVolumePercent(
                SoundAlertVolumePercent,
                GetSoundAlertSetting(alertId).VolumePercent);
        }

        public SoundAlertSetting GetSoundAlertSetting(string alertId)
        {
            SoundAlerts ??= SoundAlertDefaults.CreateDefaultSettings();

            if (SoundAlerts.TryGetValue(alertId, out var setting))
                return setting;

            return SoundAlertDefaults.CreateDefaultSetting(alertId);
        }

        public void ResetConfigurableOptionsToDefaults()
        {
            var defaults = new ChampSelectSettings();

            StartWatcherOnStartup = defaults.StartWatcherOnStartup;
            InQueueAutomationEnabled = defaults.InQueueAutomationEnabled;
            AutoReadyCheckEnabled = defaults.AutoReadyCheckEnabled;
            ReadyCheckAcceptDelaySeconds = defaults.ReadyCheckAcceptDelaySeconds;
            PickLockDelaySeconds = defaults.PickLockDelaySeconds;
            ChampionSelectAutomationEnabled = defaults.ChampionSelectAutomationEnabled;
            AutoHoverChampionEnabled = defaults.AutoHoverChampionEnabled;
            ChampionHoverDelaySeconds = defaults.ChampionHoverDelaySeconds;
            PlanningHoverDelaySeconds = defaults.PlanningHoverDelaySeconds;
            BanLockDelaySeconds = defaults.BanLockDelaySeconds;
            AutoLockSelectionEnabled = defaults.AutoLockSelectionEnabled;
            ChampSelectPollIntervalMs = defaults.ChampSelectPollIntervalMs;
            UseChampSelectEventStream = defaults.UseChampSelectEventStream;
            ChampSelectEventFallbackPollingEnabled = defaults.ChampSelectEventFallbackPollingEnabled;
            ChampSelectEventFallbackPollIntervalMs = defaults.ChampSelectEventFallbackPollIntervalMs;
            ThemeKey = defaults.ThemeKey;
            AutoUpdateChampionCatalogOnStartup = defaults.AutoUpdateChampionCatalogOnStartup;
            ShowChampionPictureDownloadWarning = defaults.ShowChampionPictureDownloadWarning;
            DownloadRawChampionPictures = defaults.DownloadRawChampionPictures;
        }

        public void ResetSoundAlertOptionsToDefaults()
        {
            var defaults = new ChampSelectSettings();

            SoundAlertProfile = defaults.SoundAlertProfile;
            SoundAlertVolumePercent = defaults.SoundAlertVolumePercent;
            SoundAlerts = SoundAlertDefaults.CreateDefaultSettings();
        }

        public void Save()
        {
            AppStorage.EnsureDirectoryExists();
            Version = AppStorage.SettingsFileVersion;
            NormalizeSoundAlertOptions();

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppStorage.SettingsFilePath, json);
            Saved?.Invoke();
        }

        public static ChampSelectSettings Load()
        {
            try
            {
                if (File.Exists(AppStorage.SettingsFilePath))
                {
                    var json = File.ReadAllText(AppStorage.SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<ChampSelectSettings>(json);
                    return settings is null
                        ? ResetSettingsFile()
                        : NormalizeSettings(settings);
                }
            }
            catch
            {
                return ResetSettingsFile();
            }

            return new ChampSelectSettings();
        }

        private static ChampSelectSettings ResetSettingsFile()
        {
            var defaults = new ChampSelectSettings();
            try
            {
                defaults.Save();
            }
            catch
            {
            }

            return defaults;
        }

        private static ChampSelectSettings NormalizeSettings(ChampSelectSettings settings)
        {
            settings.Preferences.Remove(Position.None);
            settings.NormalizeSoundAlertOptions();
            settings.Version = AppStorage.SettingsFileVersion;

            if (settings.ChampSelectEventFallbackPollIntervalMs <= 0)
                settings.ChampSelectEventFallbackPollIntervalMs = new ChampSelectSettings().ChampSelectEventFallbackPollIntervalMs;

            return settings;
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

                var normalizedAlert = normalizedAlerts[definition.Id];
                normalizedAlert.Enabled = currentAlert.Enabled;
                if (!string.IsNullOrWhiteSpace(currentAlert.SoundKey))
                    normalizedAlert.SoundKey = currentAlert.SoundKey.Trim();

                normalizedAlert.VolumePercent = NormalizeSoundAlertVolumePercent(currentAlert.VolumePercent);
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
            if (volumePercent is null)
                return DefaultSoundAlertVolumePercent;

            return Math.Clamp(
                volumePercent.Value < MinSoundAlertVolumePercent ? DefaultSoundAlertVolumePercent : volumePercent.Value,
                MinSoundAlertVolumePercent,
                MaxSoundAlertVolumePercent);
        }

        public static int GetEffectiveSoundAlertVolumePercent(int? masterVolumePercent, int? alertVolumePercent)
        {
            int normalizedMasterVolume = NormalizeSoundAlertVolumePercent(masterVolumePercent);
            int normalizedAlertVolume = NormalizeSoundAlertVolumePercent(alertVolumePercent);
            return Math.Clamp(
                (int)Math.Round(normalizedMasterVolume * (normalizedAlertVolume / 100d)),
                MinSoundAlertVolumePercent,
                MaxSoundAlertVolumePercent);
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

        private static int NormalizeSoundAlertThresholdFallbackSeconds(int thresholdSeconds)
        {
            return Math.Clamp(
                thresholdSeconds,
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
    }
}
