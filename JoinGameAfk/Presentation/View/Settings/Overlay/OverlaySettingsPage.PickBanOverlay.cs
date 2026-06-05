using JoinGameAfk.Model;

namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage
    {
        private void ApplyPickBanOverlaySettingsToControls()
        {
            PickBanAutoShowCheckBox.IsChecked = _settings.AutoShowPickBanOverlayEnabled;
            PickBanAutoCloseCheckBox.IsChecked = _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled;
            PickBanOpenOnStartupCheckBox.IsChecked = _settings.PickBanOverlayOpenOnStartup;
            PickBanTopmostCheckBox.IsChecked = _settings.PickBanOverlayTopmostEnabled;
            PickBanShowPhaseSummaryCheckBox.IsChecked = _settings.PickBanOverlayShowPhaseSummary;
            PickBanShowPhaseTimerCheckBox.IsChecked = _settings.PickBanOverlayShowPhaseTimer;
            PickBanShowLockTimerCheckBox.IsChecked = _settings.PickBanOverlayShowLockTimer;
            PickBanShowPickPlanCheckBox.IsChecked = _settings.PickBanOverlayShowPickPlan;
            PickBanShowBanPlanCheckBox.IsChecked = _settings.PickBanOverlayShowBanPlan;
            PickBanOpacitySlider.Value = OverlaySettings.NormalizePickBanOverlayOpacityPercent(_settings.PickBanOverlayOpacityPercent);
        }

        private void CapturePickBanOverlayControlsToSettings()
        {
            _settings.AutoShowPickBanOverlayEnabled = PickBanAutoShowCheckBox.IsChecked == true;
            _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled = PickBanAutoCloseCheckBox.IsChecked == true;
            _settings.PickBanOverlayOpenOnStartup = PickBanOpenOnStartupCheckBox.IsChecked == true;
            _settings.PickBanOverlayTopmostEnabled = PickBanTopmostCheckBox.IsChecked == true;
            EnsureVisiblePickBanSection();
            _settings.PickBanOverlayShowPhaseSummary = PickBanShowPhaseSummaryCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowPhaseTimer = PickBanShowPhaseTimerCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowLockTimer = PickBanShowLockTimerCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowTimers = _settings.PickBanOverlayShowPhaseTimer || _settings.PickBanOverlayShowLockTimer;
            _settings.PickBanOverlayShowPickPlan = PickBanShowPickPlanCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowBanPlan = PickBanShowBanPlanCheckBox.IsChecked == true;
            _settings.PickBanOverlayOpacityPercent = GetPickBanOpacityPercent();
        }

        private void EnsureVisiblePickBanSection()
        {
            if (PickBanShowPhaseSummaryCheckBox.IsChecked == true
                || PickBanShowPhaseTimerCheckBox.IsChecked == true
                || PickBanShowLockTimerCheckBox.IsChecked == true
                || PickBanShowPickPlanCheckBox.IsChecked == true
                || PickBanShowBanPlanCheckBox.IsChecked == true)
            {
                return;
            }

            PickBanShowPhaseSummaryCheckBox.IsChecked = true;
        }

        private int GetPickBanOpacityPercent()
        {
            return OverlaySettings.NormalizePickBanOverlayOpacityPercent((int)Math.Round(PickBanOpacitySlider.Value));
        }
    }
}
