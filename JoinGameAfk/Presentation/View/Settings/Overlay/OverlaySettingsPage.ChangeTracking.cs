using System.Windows;
using System.Windows.Controls;
using JoinGameAfk.Model;

namespace JoinGameAfk.Presentation.View.Settings.Overlay
{
    public partial class OverlaySettingsPage
    {
        private void AttachDirtyStateTracking()
        {
            CheckBox[] checkBoxes =
            [
                QueueOverlayEnabledCheckBox,
                QueueOverlayTopmostCheckBox,
                PickBanAutoShowCheckBox,
                PickBanAutoCloseCheckBox,
                PickBanOpenOnStartupCheckBox,
                PickBanTopmostCheckBox,
                PickBanShowPhaseSummaryCheckBox,
                PickBanShowPhaseTimerCheckBox,
                PickBanShowLockTimerCheckBox,
                PickBanShowPickPlanCheckBox,
                PickBanShowBanPlanCheckBox
            ];

            foreach (var checkBox in checkBoxes)
            {
                checkBox.Checked += TrackedControl_Changed;
                checkBox.Unchecked += TrackedControl_Changed;
            }

            QueueOverlayScaleSlider.ValueChanged += OverlaySlider_ValueChanged;
            PickBanOpacitySlider.ValueChanged += OverlaySlider_ValueChanged;
        }

        private void TrackedControl_Changed(object sender, RoutedEventArgs e)
        {
            RefreshDirtyState();
        }

        private void OverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshSliderValueText();
            RefreshDirtyState();
        }

        private void RefreshDirtyState()
        {
            if (_isApplyingSettingsToControls)
                return;

            bool hasDirtySettings = CaptureCurrentSnapshot() != CaptureSavedSnapshot();
            DirtyOverlayBar.Visibility = hasDirtySettings ? Visibility.Visible : Visibility.Collapsed;
            CancelOverlayChangesButton.IsEnabled = hasDirtySettings;
            if (hasDirtySettings)
            {
                _savedMessageTimer.Stop();
                FloatingOverlayStatusBar.Visibility = Visibility.Collapsed;
            }
        }

        private OverlaySettingsSnapshot CaptureCurrentSnapshot()
        {
            return new OverlaySettingsSnapshot(
                QueueOverlayEnabledCheckBox.IsChecked == true,
                QueueOverlayTopmostCheckBox.IsChecked == true,
                GetQueueOverlayScalePercent(),
                PickBanAutoShowCheckBox.IsChecked == true,
                PickBanAutoCloseCheckBox.IsChecked == true,
                PickBanOpenOnStartupCheckBox.IsChecked == true,
                PickBanTopmostCheckBox.IsChecked == true,
                PickBanShowPhaseSummaryCheckBox.IsChecked == true,
                PickBanShowPhaseTimerCheckBox.IsChecked == true,
                PickBanShowLockTimerCheckBox.IsChecked == true,
                PickBanShowPickPlanCheckBox.IsChecked == true,
                PickBanShowBanPlanCheckBox.IsChecked == true,
                GetPickBanOpacityPercent());
        }

        private OverlaySettingsSnapshot CaptureSavedSnapshot()
        {
            return new OverlaySettingsSnapshot(
                _settings.QueueMicroOverlayEnabled,
                _settings.QueueMicroOverlayTopmostEnabled,
                OverlaySettings.NormalizeQueueMicroOverlayScalePercent(_settings.QueueMicroOverlayScalePercent),
                _settings.AutoShowPickBanOverlayEnabled,
                _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled,
                _settings.PickBanOverlayOpenOnStartup,
                _settings.PickBanOverlayTopmostEnabled,
                _settings.PickBanOverlayShowPhaseSummary,
                _settings.PickBanOverlayShowPhaseTimer,
                _settings.PickBanOverlayShowLockTimer,
                _settings.PickBanOverlayShowPickPlan,
                _settings.PickBanOverlayShowBanPlan,
                OverlaySettings.NormalizePickBanOverlayOpacityPercent(_settings.PickBanOverlayOpacityPercent));
        }
    }
}
