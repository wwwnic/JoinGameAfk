namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private static readonly IReadOnlyList<RegionLocaleSuggestion> PlatformSuggestions =
        [
            new("NA1", "North America"),
            new("BR1", "Brazil"),
            new("EUN1", "Europe Nordic and East"),
            new("EUW1", "Europe West"),
            new("JP1", "Japan"),
            new("LA1", "Latin America North"),
            new("LA2", "Latin America South"),
            new("ME1", "Middle East"),
            new("OC1", "Oceania"),
            new("PH2", "Philippines"),
            new("RU", "Russia"),
            new("SG2", "Singapore"),
            new("TH2", "Thailand"),
            new("TR1", "Turkey"),
            new("TW2", "Taiwan"),
            new("VN2", "Vietnam"),
            new("PBE1", "Public Beta Environment")
        ];

        private static readonly IReadOnlyList<RegionLocaleSuggestion> LocaleSuggestions =
        [
            new("en_US", "English (United States)"),
            new("en_GB", "English (United Kingdom)"),
            new("en_AU", "English (Australia)"),
            new("en_PH", "English (Philippines)"),
            new("en_SG", "English (Singapore)"),
            new("pt_BR", "Portuguese (Brazil)"),
            new("es_MX", "Spanish (Latin America)"),
            new("es_AR", "Spanish (Argentina)"),
            new("es_ES", "Spanish (Spain)"),
            new("fr_FR", "French"),
            new("de_DE", "German"),
            new("it_IT", "Italian"),
            new("pl_PL", "Polish"),
            new("ro_RO", "Romanian"),
            new("cs_CZ", "Czech"),
            new("el_GR", "Greek"),
            new("hu_HU", "Hungarian"),
            new("tr_TR", "Turkish"),
            new("ar_AE", "Arabic"),
            new("ru_RU", "Russian"),
            new("ja_JP", "Japanese"),
            new("ko_KR", "Korean"),
            new("zh_CN", "Chinese (Simplified)"),
            new("zh_TW", "Chinese (Traditional)"),
            new("zh_MY", "Chinese (Malaysia)"),
            new("id_ID", "Indonesian"),
            new("th_TH", "Thai"),
            new("vi_VN", "Vietnamese")
        ];

        private sealed record RegionLocaleSuggestion(string Code, string Name);

        private readonly record struct GeneralSettingsInputValues(
            string PlatformId,
            string Locale,
            int ReadyCheckAcceptDelaySeconds,
            int PickLockDelaySeconds,
            int ChampionHoverDelaySeconds,
            int PlanningHoverDelaySeconds,
            int BanLockDelaySeconds,
            int ChampSelectPollIntervalMs,
            int ChampSelectEventFallbackPollIntervalMs);

        private readonly record struct GeneralSettingsSnapshot(
            bool StartWatcherOnStartup,
            bool AutoDetectRegionLocale,
            string PlatformId,
            string Locale,
            bool InQueueAutomationEnabled,
            bool AutoReadyCheckEnabled,
            string ReadyCheckAcceptDelaySeconds,
            bool ChampionSelectAutomationEnabled,
            bool AutoHoverChampionEnabled,
            bool AutoLockSelectionEnabled,
            string PickLockDelaySeconds,
            string ChampionHoverDelaySeconds,
            string PlanningHoverDelaySeconds,
            string BanLockDelaySeconds,
            string ChampSelectPollIntervalMs,
            bool UseChampSelectEventStream,
            bool ChampSelectEventFallbackPollingEnabled,
            string ChampSelectEventFallbackPollIntervalMs,
            string ThemeKey,
            bool AutoUpdateChampionCatalogOnStartup,
            bool DownloadRawChampionPictures);
    }
}
