using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void ShowSavedMessage()
        {
            ShowStatusMessage("Settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowDefaultsRestoredMessage()
        {
            ShowStatusMessage("Default settings restored.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowChangesCanceledMessage()
        {
            ShowStatusMessage("Settings changes canceled.", "TextSoftBrush", Brushes.SlateGray);
        }

        private void ShowValidationMessage(string message)
        {
            ShowStatusMessage(message, "DangerTextBrush", Brushes.IndianRed);
        }

        private void ShowStatusMessage(string message, string brushResourceKey, Brush fallbackBrush)
        {
            _savedMessageTimer.Stop();
            var messageBrush = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            FloatingSettingsStatusText.Text = message;
            FloatingSettingsStatusText.Foreground = messageBrush;
            FloatingSettingsStatusBar.BorderBrush = messageBrush;
            FloatingSettingsStatusBar.Visibility = Visibility.Visible;
            _savedMessageTimer.Start();
        }
    }
}