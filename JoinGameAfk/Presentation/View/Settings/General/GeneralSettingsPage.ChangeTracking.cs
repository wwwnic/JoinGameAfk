using System.Windows;
using System.Windows.Controls;
using JoinGameAfk.Theme;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void AttachDirtyStateTracking()
        {
            CheckBox[] checkBoxes =
            [
                StartWatcherOnStartupCheckBox,
                InQueueAutomationCheckBox,
                AutoReadyCheckCheckBox,
                ChampionSelectAutomationCheckBox,
                AutoHoverChampionCheckBox,
                AutoLockSelectionCheckBox,
                UseLiveEventsCheckBox,
                EventFallbackPollingCheckBox,
                AutoUpdateChampionCatalogOnStartupCheckBox,
                DownloadRawChampionPicturesCheckBox
            ];

            foreach (var checkBox in checkBoxes)
            {
                checkBox.Checked += DirtyTrackedControl_Changed;
                checkBox.Unchecked += DirtyTrackedControl_Changed;
            }

            ReadyCheckAcceptDelayBox.TextChanged += DirtyTrackedControl_TextChanged;
            PickLockDelayBox.TextChanged += DirtyTrackedControl_TextChanged;
            ChampionHoverDelayBox.TextChanged += DirtyTrackedControl_TextChanged;
            PlanningHoverDelayBox.TextChanged += DirtyTrackedControl_TextChanged;
            BanLockDelayBox.TextChanged += DirtyTrackedControl_TextChanged;
            ChampSelectPollIntervalBox.TextChanged += DirtyTrackedControl_TextChanged;
            EventFallbackPollIntervalBox.TextChanged += DirtyTrackedControl_TextChanged;
        }

        private void DirtyTrackedControl_Changed(object sender, RoutedEventArgs e)
        {
            RefreshDirtyState();
        }

        private void DirtyTrackedControl_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshDirtyState();
        }

        private void RefreshDirtyState()
        {
            if (_isApplyingSettingsToControls)
                return;

            bool hasDirtySettings = CaptureCurrentSettingsSnapshot() != CaptureSavedSettingsSnapshot();
            DirtySettingsBar.Visibility = hasDirtySettings ? Visibility.Visible : Visibility.Collapsed;
            CancelSettingsChangesButton.IsEnabled = hasDirtySettings;
            if (hasDirtySettings)
            {
                _savedMessageTimer.Stop();
                FloatingSettingsStatusBar.Visibility = Visibility.Collapsed;
            }
        }

        private GeneralSettingsSnapshot CaptureCurrentSettingsSnapshot()
        {
            bool inQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;
            bool championSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && AutoHoverChampionCheckBox.IsChecked == true;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && AutoLockSelectionCheckBox.IsChecked == true;
            bool useLiveEvents = UseLiveEventsCheckBox.IsChecked == true;
            bool eventFallbackPollingEnabled = EventFallbackPollingCheckBox.IsChecked == true;

            return new GeneralSettingsSnapshot(
                StartWatcherOnStartupCheckBox.IsChecked == true,
                inQueueAutomationEnabled,
                autoReadyCheckEnabled,
                autoReadyCheckEnabled ? CreateNumericSnapshot(ReadyCheckAcceptDelayBox) : string.Empty,
                championSelectAutomationEnabled,
                autoHoverChampionEnabled,
                autoLockSelectionEnabled,
                autoLockSelectionEnabled ? CreateNumericSnapshot(PickLockDelayBox) : string.Empty,
                autoHoverChampionEnabled ? CreateNumericSnapshot(ChampionHoverDelayBox) : string.Empty,
                autoHoverChampionEnabled ? CreateNumericSnapshot(PlanningHoverDelayBox) : string.Empty,
                autoLockSelectionEnabled ? CreateNumericSnapshot(BanLockDelayBox) : string.Empty,
                useLiveEvents ? string.Empty : CreateNumericSnapshot(ChampSelectPollIntervalBox),
                useLiveEvents,
                eventFallbackPollingEnabled,
                useLiveEvents && eventFallbackPollingEnabled ? CreateNumericSnapshot(EventFallbackPollIntervalBox) : string.Empty,
                GetSelectedThemeKey(),
                AutoUpdateChampionCatalogOnStartupCheckBox.IsChecked == true,
                DownloadRawChampionPicturesCheckBox.IsChecked == true);
        }

        private GeneralSettingsSnapshot CaptureSavedSettingsSnapshot()
        {
            bool inQueueAutomationEnabled = _settings.InQueueAutomationEnabled;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && _settings.AutoReadyCheckEnabled;
            bool championSelectAutomationEnabled = _settings.IsChampionSelectAutomationActive();
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && _settings.AutoHoverChampionEnabled;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && _settings.AutoLockSelectionEnabled;
            bool useLiveEvents = _settings.UseChampSelectEventStream;
            bool eventFallbackPollingEnabled = _settings.ChampSelectEventFallbackPollingEnabled;

            return new GeneralSettingsSnapshot(
                _settings.StartWatcherOnStartup,
                inQueueAutomationEnabled,
                autoReadyCheckEnabled,
                autoReadyCheckEnabled ? _settings.ReadyCheckAcceptDelaySeconds.ToString() : string.Empty,
                championSelectAutomationEnabled,
                autoHoverChampionEnabled,
                autoLockSelectionEnabled,
                autoLockSelectionEnabled ? _settings.PickLockDelaySeconds.ToString() : string.Empty,
                autoHoverChampionEnabled ? _settings.ChampionHoverDelaySeconds.ToString() : string.Empty,
                autoHoverChampionEnabled ? _settings.PlanningHoverDelaySeconds.ToString() : string.Empty,
                autoLockSelectionEnabled ? _settings.BanLockDelaySeconds.ToString() : string.Empty,
                useLiveEvents ? string.Empty : _settings.ChampSelectPollIntervalMs.ToString(),
                useLiveEvents,
                eventFallbackPollingEnabled,
                useLiveEvents && eventFallbackPollingEnabled ? _settings.ChampSelectEventFallbackPollIntervalMs.ToString() : string.Empty,
                AppThemeManager.NormalizeThemeKey(_settings.ThemeKey),
                _settings.AutoUpdateChampionCatalogOnStartup,
                _settings.DownloadRawChampionPictures);
        }

        private static string CreateNumericSnapshot(TextBox textBox)
        {
            return int.TryParse(textBox.Text, out int value)
                ? value.ToString()
                : $"invalid:{textBox.Text}";
        }
    }
}