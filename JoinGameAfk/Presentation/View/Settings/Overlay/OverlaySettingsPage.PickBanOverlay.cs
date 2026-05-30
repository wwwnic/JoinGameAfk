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
            PickBanShowTimersCheckBox.IsChecked = _settings.PickBanOverlayShowTimers;
            PickBanShowPickPlanCheckBox.IsChecked = _settings.PickBanOverlayShowPickPlan;
            PickBanShowBanPlanCheckBox.IsChecked = _settings.PickBanOverlayShowBanPlan;
            PickBanScaleSlider.Value = OverlaySettings.NormalizePickBanOverlayScalePercent(_settings.PickBanOverlayScalePercent);
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
            _settings.PickBanOverlayShowTimers = PickBanShowTimersCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowPickPlan = PickBanShowPickPlanCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowBanPlan = PickBanShowBanPlanCheckBox.IsChecked == true;

            int pickBanScalePercent = GetPickBanScalePercent();
            if (_settings.PickBanOverlayScalePercent != pickBanScalePercent)
            {
                _settings.PickBanOverlayWidth = null;
                _settings.PickBanOverlayHeight = null;
            }

            _settings.PickBanOverlayScalePercent = pickBanScalePercent;
            _settings.PickBanOverlayOpacityPercent = GetPickBanOpacityPercent();
        }

        private void EnsureVisiblePickBanSection()
        {
            if (PickBanShowPhaseSummaryCheckBox.IsChecked == true
                || PickBanShowTimersCheckBox.IsChecked == true
                || PickBanShowPickPlanCheckBox.IsChecked == true
                || PickBanShowBanPlanCheckBox.IsChecked == true)
            {
                return;
            }

            PickBanShowPhaseSummaryCheckBox.IsChecked = true;
        }

        private int GetPickBanScalePercent()
        {
            return OverlaySettings.NormalizePickBanOverlayScalePercent((int)Math.Round(PickBanScaleSlider.Value));
        }

        private int GetPickBanOpacityPercent()
        {
            return OverlaySettings.NormalizePickBanOverlayOpacityPercent((int)Math.Round(PickBanOpacitySlider.Value));
        }
    }
}
