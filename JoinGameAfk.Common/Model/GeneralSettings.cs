using JoinGameAfk.Constant;
using JoinGameAfk.Services;

namespace JoinGameAfk.Model
{
    public sealed class GeneralSettings
    {
        public int Version { get; set; } = AppStorage.GeneralSettingsFileVersion;

        /// <summary>
        /// Whether the watcher should start automatically when the app starts.
        /// </summary>
        public bool StartWatcherOnStartup { get; set; }

        /// <summary>
        /// Whether the app should perform in-queue automation.
        /// </summary>
        public bool InQueueAutomationEnabled { get; set; } = true;

        /// <summary>
        /// Whether the app should automatically accept ready checks.
        /// </summary>
        public bool AutoReadyCheckEnabled { get; set; } = true;

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
        /// Whether the app should check Riot Data Dragon for champion list updates when it starts.
        /// </summary>
        public bool AutoUpdateChampionCatalogOnStartup { get; set; }

        /// <summary>
        /// Whether Data Dragon champion picture downloads should keep Riot's original jpg files instead of resized app-cache copies.
        /// </summary>
        public bool DownloadRawChampionPictures { get; set; }

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

        public void ResetConfigurableOptionsToDefaults()
        {
            var defaults = new GeneralSettings();

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
            DownloadRawChampionPictures = defaults.DownloadRawChampionPictures;
        }

        public void Save()
        {
            JsonSettingsStore.Save(AppStorage.GeneralSettingsFilePath, this, NormalizeSettings);
            Saved?.Invoke();
        }

        public static GeneralSettings Load()
        {
            return JsonSettingsStore.Load(AppStorage.GeneralSettingsFilePath, () => new GeneralSettings(), NormalizeSettings);
        }

        private static void NormalizeSettings(GeneralSettings settings)
        {
            settings.Version = AppStorage.GeneralSettingsFileVersion;
            settings.ThemeKey = string.IsNullOrWhiteSpace(settings.ThemeKey)
                ? new GeneralSettings().ThemeKey
                : settings.ThemeKey.Trim();

            if (settings.ChampSelectPollIntervalMs <= 0)
                settings.ChampSelectPollIntervalMs = new GeneralSettings().ChampSelectPollIntervalMs;

            if (settings.ChampSelectEventFallbackPollIntervalMs <= 0)
                settings.ChampSelectEventFallbackPollIntervalMs = new GeneralSettings().ChampSelectEventFallbackPollIntervalMs;
        }
    }
}
