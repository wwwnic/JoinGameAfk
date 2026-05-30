using System.Windows;
using System.Windows.Controls;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage
    {
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryValidateSoundAlertSettings())
                return;

            _settings.SoundAlertProfile = AreSoundAlertsEnabled()
                ? SoundAlertProfile.Custom
                : SoundAlertProfile.Off;
            _settings.SoundAlertVolumePercent = GetSoundAlertVolumePercent();
            _settings.SoundAlerts = CaptureSoundAlertSettings();
            _settings.Save();

            RefreshDirtyState();
            ShowSavedMessage();
        }

        private void ResetSoundDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Restore default sound alert mode, volume, sounds, and warning timing?",
                "Reset Sounds",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
                return;

            _settings.ResetSoundAlertOptionsToDefaults();
            ApplySettingsToControls();
            _settings.Save();

            RefreshDirtyState();
            ShowDefaultsRestoredMessage();
        }

        private void CancelSoundChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmCancelSoundChanges())
                return;

            if (_isSoundDragActive)
                FinishSoundDrag(drop: false);

            ClearPendingSoundDrag();
            ClearSoundDropTarget();
            IsSoundClearDropTarget = false;
            _notificationSoundPlayer.StopChannel(SoundStudioPreviewChannelKey);
            ClearLastPreviewedSoundChoice();

            ApplySettingsToControls();
            NotificationSoundPlayer.SetActivePlayerVolume(GetSoundAlertVolumePercent());
            RefreshDirtyState();
            ShowChangesCanceledMessage();
        }

        private bool ConfirmCancelSoundChanges()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Discard unsaved sound changes and return to the last saved sound settings?",
                "Cancel Sound Changes",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private void SoundAlertVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshSoundAlertVolumeValueText();
            NotificationSoundPlayer.SetActivePlayerVolume(GetSoundAlertVolumePercent());
            RefreshDirtyState();
        }

        private void SoundAlertsEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSoundAlertInputStates();
            RefreshDirtyState();
        }


        private void ClearSoundAlertButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SoundAlertOption option })
                return;

            ClearSoundAlertOption(option);
            e.Handled = true;
        }

        private void SoundAlertInfiniteToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSettingsToControls)
                return;

            RefreshLockCountdownDescriptions();
            RefreshDirtyState();
            e.Handled = true;
        }

        private void SoundPickerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshSoundPickerChoices();
        }

        private void SoundPickerSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (SoundPickerSearchBox.Text.Length > 0)
                SoundPickerSearchBox.Clear();

            e.Handled = true;
        }

        private void SoundAlertThresholdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateSoundAlertThresholdBox(sender as TextBox);
            RefreshLockCountdownDescriptions();
            RefreshDirtyState();
        }
    }
}
