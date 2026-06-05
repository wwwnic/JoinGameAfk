namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage
    {
        private readonly record struct OverlaySettingsSnapshot(
            bool QueueMicroOverlayEnabled,
            bool QueueMicroOverlayTopmostEnabled,
            int QueueMicroOverlayScalePercent,
            bool AutoShowPickBanOverlayEnabled,
            bool PickBanOverlayAutoCloseAfterChampSelectEnabled,
            bool PickBanOverlayOpenOnStartup,
            bool PickBanOverlayTopmostEnabled,
            bool PickBanOverlayShowPhaseSummary,
            bool PickBanOverlayShowPhaseTimer,
            bool PickBanOverlayShowLockTimer,
            bool PickBanOverlayShowPickPlan,
            bool PickBanOverlayShowBanPlan,
            int PickBanOverlayOpacityPercent);
    }
}
