namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private readonly record struct GeneralSettingsInputValues(
            int ReadyCheckAcceptDelaySeconds,
            int PickLockDelaySeconds,
            int ChampionHoverDelaySeconds,
            int PlanningHoverDelaySeconds,
            int BanLockDelaySeconds,
            int ChampSelectPollIntervalMs,
            int ChampSelectEventFallbackPollIntervalMs);

        private readonly record struct GeneralSettingsSnapshot(
            bool StartWatcherOnStartup,
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