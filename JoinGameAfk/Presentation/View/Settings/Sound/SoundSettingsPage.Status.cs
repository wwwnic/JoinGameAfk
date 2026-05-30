using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage
    {
        private void ShowSavedMessage()
        {
            ShowStatusMessage("Sound settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowDefaultsRestoredMessage()
        {
            ShowStatusMessage("Default sounds restored.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowChangesCanceledMessage()
        {
            ShowStatusMessage("Sound changes canceled.", "TextSoftBrush", Brushes.SlateGray);
        }

        private void ShowValidationMessage(string message)
        {
            ShowStatusMessage(message, "DangerTextBrush", Brushes.IndianRed);
        }

        private void ShowStatusMessage(string message, string brushResourceKey, Brush fallbackBrush)
        {
            _savedMessageTimer.Stop();
            var messageBrush = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            FloatingSoundStatusText.Text = message;
            FloatingSoundStatusText.Foreground = messageBrush;
            FloatingSoundStatusBar.BorderBrush = messageBrush;
            FloatingSoundStatusBar.Visibility = Visibility.Visible;
            _savedMessageTimer.Start();
        }
    }
}
