using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage
    {
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            CaptureControlsToSettings();
            _settings.Save();
            RefreshDirtyState();
            ShowStatusMessage("Overlay settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Restore default overlay settings?",
                "Reset Overlays",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
                return;

            _settings.ResetToDefaults();
            ApplySettingsToControls();
            _settings.Save();
            RefreshDirtyState();
            ShowStatusMessage("Default overlay settings restored.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void CancelChangesButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Discard unsaved overlay changes and return to the last saved overlay settings?",
                "Cancel Overlay Changes",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
                return;

            ApplySettingsToControls();
            RefreshDirtyState();
            ShowStatusMessage("Overlay changes canceled.", "TextMutedBrush", Brushes.Gray);
        }

        private void CaptureControlsToSettings()
        {
            CaptureQueueOverlayControlsToSettings();
            CapturePickBanOverlayControlsToSettings();
            _settings.NormalizeOptions();
        }
    }
}
