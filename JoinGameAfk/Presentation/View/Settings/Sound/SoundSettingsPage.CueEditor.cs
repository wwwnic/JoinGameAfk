using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoinGameAfk.Model;
using JoinGameAfk.Validation;

namespace JoinGameAfk.Presentation.View.Settings.Sound
{
    public partial class SoundSettingsPage
    {
        private void SoundCueEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SoundAlertOption option })
                return;

            OpenSoundCueEditor(option);
            e.Handled = true;
        }

        private void OpenSoundCueEditor(SoundAlertOption option)
        {
            if (_isSoundDragActive)
                FinishSoundDrag(drop: false);

            _editingSoundCueOption = option;
            _isApplyingSoundCueEditor = true;
            try
            {
                SoundCueEditorTitleTextBlock.Text = option.DisplayName;
                SoundCueEditorSoundTextBlock.Text = option.SelectedSoundDisplayName;
                SoundCueEditorDescriptionTextBlock.Text = option.Description;
                SoundCueEditorEnabledCheckBox.IsChecked = option.IsEnabled;
                SoundCueEditorVolumeSlider.Value = SoundSettings.NormalizeSoundAlertVolumePercent(ParseSoundAlertVolumeOrDefault(option));
                SoundCueEditorVolumeValueText.Text = $"{GetSoundCueEditorVolumePercent()}%";
                SoundCueEditorLeadTextBox.Text = option.ThresholdText;
                SoundCueEditorLeadPanel.Visibility = option.HasThreshold ? Visibility.Visible : Visibility.Collapsed;
                SoundCueEditorInfiniteCheckBox.IsChecked = option.IsInfinitePlaybackEnabled;
                SoundCueEditorInfinitePanel.Visibility = option.SupportsInfinitePlayback ? Visibility.Visible : Visibility.Collapsed;
                ValidateSoundCueEditorLeadBox();
                UpdateSoundCueEditorInputStates();
            }
            finally
            {
                _isApplyingSoundCueEditor = false;
            }

            SoundCueEditorOverlay.Visibility = Visibility.Visible;
            SoundCueEditorVolumeSlider.Focus();
        }

        private void CloseSoundCueEditor()
        {
            if (SoundCueEditorOverlay is null)
                return;

            _notificationSoundPlayer.StopChannel(SoundStudioPreviewChannelKey);
            SoundCueEditorOverlay.Visibility = Visibility.Collapsed;
            _editingSoundCueOption = null;
            _isApplyingSoundCueEditor = false;
        }

        private void SoundCueEditorCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSoundCueEditor();
            e.Handled = true;
        }

        private void SoundCueEditorOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, SoundCueEditorOverlay))
            {
                CloseSoundCueEditor();
                e.Handled = true;
            }
        }

        private void SoundCueEditorEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSoundCueEditor || _editingSoundCueOption is null)
                return;

            bool cueEnabled = SoundCueEditorEnabledCheckBox.IsChecked == true;
            if (cueEnabled && !_editingSoundCueOption.HasAssignedSound)
                _editingSoundCueOption.SoundKey = _editingSoundCueOption.DefaultSoundKey;

            _editingSoundCueOption.IsEnabled = cueEnabled;
            if (_editingSoundCueOption.IsEnabled && !AreSoundAlertsEnabled())
                SoundAlertsEnabledCheckBox.IsChecked = true;
            else
                SyncSoundAlertsEnabledFromRows();
            RefreshSoundCueEditorSummary();
            UpdateSoundCueEditorInputStates();
            UpdateSoundAlertInputStates();
            RefreshDirtyState();
            e.Handled = true;
        }

        private void SoundCueEditorVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SoundCueEditorVolumeValueText.Text = $"{GetSoundCueEditorVolumePercent()}%";

            if (_isApplyingSoundCueEditor || _editingSoundCueOption is null)
                return;

            _editingSoundCueOption.VolumeText = GetSoundCueEditorVolumePercent().ToString();
            RefreshDirtyState();
        }

        private void SoundCueEditorLeadTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingSoundCueEditor || _editingSoundCueOption is null)
                return;

            _editingSoundCueOption.ThresholdText = SoundCueEditorLeadTextBox.Text;
            ValidateSoundCueEditorLeadBox();
            RefreshLockCountdownDescriptions();
            RefreshSoundCueEditorSummary();
            RefreshDirtyState();
        }

        private void SoundCueEditorInfiniteCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSoundCueEditor || _editingSoundCueOption is null)
                return;

            _editingSoundCueOption.IsInfinitePlaybackEnabled = SoundCueEditorInfiniteCheckBox.IsChecked == true;
            RefreshLockCountdownDescriptions();
            RefreshSoundCueEditorSummary();
            RefreshDirtyState();
            e.Handled = true;
        }

        private void SoundCueEditorSoundCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            PreviewEditingSoundCue();
            e.Handled = true;
        }

        private void SoundCueEditorRestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingSoundCueOption is null)
                return;

            _editingSoundCueOption.RestoreDefaults();
            if (_editingSoundCueOption.IsEnabled && !AreSoundAlertsEnabled())
                SoundAlertsEnabledCheckBox.IsChecked = true;
            else
                SyncSoundAlertsEnabledFromRows();

            RefreshLockCountdownDescriptions();
            ApplySoundCueEditorValues(_editingSoundCueOption);
            UpdateSoundAlertInputStates();
            RefreshDirtyState();
            e.Handled = true;
        }

        private void RefreshSoundCueEditorSummary()
        {
            if (_editingSoundCueOption is null)
                return;

            SoundCueEditorSoundTextBlock.Text = _editingSoundCueOption.SelectedSoundDisplayName;
            SoundCueEditorDescriptionTextBlock.Text = _editingSoundCueOption.Description;
        }

        private void UpdateSoundCueEditorInputStates()
        {
            if (_editingSoundCueOption is null)
                return;

            bool canEditAssignedSound = _editingSoundCueOption.IsEnabled && _editingSoundCueOption.HasAssignedSound;
            SoundCueEditorVolumeSlider.IsEnabled = canEditAssignedSound;
            SoundCueEditorLeadTextBox.IsEnabled = canEditAssignedSound;
            SoundCueEditorInfiniteCheckBox.IsEnabled = canEditAssignedSound && _editingSoundCueOption.SupportsInfinitePlayback;
        }

        private bool TryValidateSoundCueEditorInputs()
        {
            if (_editingSoundCueOption?.HasThreshold == true
                && !TryParseSoundAlertThreshold(SoundCueEditorLeadTextBox.Text, out _))
            {
                ValidateSoundCueEditorLeadBox();
                ShowValidationMessage($"{_editingSoundCueOption.DisplayName} cue start must be a whole number between {SoundSettings.MinSoundAlertThresholdSeconds} and {SoundSettings.MaxSoundAlertThresholdSeconds} seconds before auto-lock.");
                SoundCueEditorLeadTextBox.Focus();
                return false;
            }

            return true;
        }

        private void ValidateSoundCueEditorLeadBox()
        {
            if (SoundCueEditorLeadTextBox is null)
                return;

            bool isValid = _editingSoundCueOption is null
                || !_editingSoundCueOption.HasThreshold
                || TryParseSoundAlertThreshold(SoundCueEditorLeadTextBox.Text, out _);
            InputValidator.SetValidationState(
                SoundCueEditorLeadTextBox,
                isValid ? InputValidationState.Valid : InputValidationState.Invalid);
        }

        private int GetSoundCueEditorVolumePercent()
        {
            return SoundSettings.NormalizeSoundAlertVolumePercent((int)Math.Round(SoundCueEditorVolumeSlider.Value));
        }

        private void PreviewEditingSoundCue()
        {
            if (_editingSoundCueOption is null)
                return;

            PreviewSoundAlertOption(_editingSoundCueOption);
        }

        private void ApplySoundCueEditorValues(SoundAlertOption option)
        {
            _isApplyingSoundCueEditor = true;
            try
            {
                SoundCueEditorTitleTextBlock.Text = option.DisplayName;
                SoundCueEditorEnabledCheckBox.IsChecked = option.IsEnabled;
                SoundCueEditorVolumeSlider.Value = SoundSettings.NormalizeSoundAlertVolumePercent(ParseSoundAlertVolumeOrDefault(option));
                SoundCueEditorVolumeValueText.Text = $"{GetSoundCueEditorVolumePercent()}%";
                SoundCueEditorLeadTextBox.Text = option.ThresholdText;
                SoundCueEditorLeadPanel.Visibility = option.HasThreshold ? Visibility.Visible : Visibility.Collapsed;
                SoundCueEditorInfiniteCheckBox.IsChecked = option.IsInfinitePlaybackEnabled;
                SoundCueEditorInfinitePanel.Visibility = option.SupportsInfinitePlayback ? Visibility.Visible : Visibility.Collapsed;
                RefreshSoundCueEditorSummary();
                ValidateSoundCueEditorLeadBox();
                UpdateSoundCueEditorInputStates();
            }
            finally
            {
                _isApplyingSoundCueEditor = false;
            }
        }
    }
}
