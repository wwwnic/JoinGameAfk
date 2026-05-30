using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage
    {
        private void RefreshSliderValueText()
        {
            if (QueueOverlayScaleValueText is null)
                return;

            QueueOverlayScaleValueText.Text = $"{GetQueueOverlayScalePercent()}%";
            PickBanScaleValueText.Text = $"{GetPickBanScalePercent()}%";
            PickBanOpacityValueText.Text = $"{GetPickBanOpacityPercent()}%";
        }

        private void ShowStatusMessage(string message, string brushKey, Brush fallbackBrush)
        {
            FloatingOverlayStatusText.Text = message;
            FloatingOverlayStatusText.Foreground = TryFindResource(brushKey) as Brush ?? fallbackBrush;
            FloatingOverlayStatusBar.Visibility = Visibility.Visible;
            _savedMessageTimer.Stop();
            _savedMessageTimer.Start();
        }
    }
}
