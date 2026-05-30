using JoinGameAfk.Model;

namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage
    {
        private void ApplyQueueOverlaySettingsToControls()
        {
            QueueOverlayEnabledCheckBox.IsChecked = _settings.QueueMicroOverlayEnabled;
            QueueOverlayTopmostCheckBox.IsChecked = _settings.QueueMicroOverlayTopmostEnabled;
            QueueOverlayScaleSlider.Value = OverlaySettings.NormalizeQueueMicroOverlayScalePercent(_settings.QueueMicroOverlayScalePercent);
        }

        private void CaptureQueueOverlayControlsToSettings()
        {
            _settings.QueueMicroOverlayEnabled = QueueOverlayEnabledCheckBox.IsChecked == true;
            _settings.QueueMicroOverlayTopmostEnabled = QueueOverlayTopmostCheckBox.IsChecked == true;
            _settings.QueueMicroOverlayScalePercent = GetQueueOverlayScalePercent();
        }

        private int GetQueueOverlayScalePercent()
        {
            return OverlaySettings.NormalizeQueueMicroOverlayScalePercent((int)Math.Round(QueueOverlayScaleSlider.Value));
        }
    }
}
