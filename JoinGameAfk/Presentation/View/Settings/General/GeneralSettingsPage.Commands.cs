using System.Windows;
using JoinGameAfk.Theme;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSettingsInput(out var input))
                return;

            _settings.StartWatcherOnStartup = StartWatcherOnStartupCheckBox.IsChecked == true;
            _settings.InQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            _settings.AutoReadyCheckEnabled = _settings.InQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;
            _settings.ReadyCheckAcceptDelaySeconds = input.ReadyCheckAcceptDelaySeconds;
            _settings.ChampionSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            _settings.AutoHoverChampionEnabled = _settings.ChampionSelectAutomationEnabled && AutoHoverChampionCheckBox.IsChecked == true;
            _settings.AutoLockSelectionEnabled = _settings.ChampionSelectAutomationEnabled && AutoLockSelectionCheckBox.IsChecked == true;
            _settings.PickLockDelaySeconds = input.PickLockDelaySeconds;
            _settings.ChampionHoverDelaySeconds = input.ChampionHoverDelaySeconds;
            _settings.PlanningHoverDelaySeconds = input.PlanningHoverDelaySeconds;
            _settings.BanLockDelaySeconds = input.BanLockDelaySeconds;
            _settings.ChampSelectPollIntervalMs = input.ChampSelectPollIntervalMs;
            _settings.UseChampSelectEventStream = UseLiveEventsCheckBox.IsChecked == true;
            _settings.ChampSelectEventFallbackPollingEnabled = EventFallbackPollingCheckBox.IsChecked == true;
            _settings.ChampSelectEventFallbackPollIntervalMs = input.ChampSelectEventFallbackPollIntervalMs;
            _settings.ThemeKey = GetSelectedThemeKey();
            _settings.AutoUpdateChampionCatalogOnStartup = AutoUpdateChampionCatalogOnStartupCheckBox.IsChecked == true;
            _settings.DownloadRawChampionPictures = DownloadRawChampionPicturesCheckBox.IsChecked == true;
            bool shouldReloadTheme = SelectedThemeRequiresReload();

            _settings.Save();
            RefreshDirtyState();
            if (shouldReloadTheme && _reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings, _overlaySettings, GetSelectedThemeKey(), _isThemePickerExpanded);
                return;
            }

            ShowSavedMessage();
        }

        private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Restore default startup, automation, timing, performance, theme, and download settings?\n\nRole plans, sound alerts, and overlay settings are kept.",
                "Reset Defaults",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK)
                return;

            _settings.ResetConfigurableOptionsToDefaults();
            _isThemePickerExpanded = false;
            ApplySettingsToControls();
            UpdateAutomationInputStates();

            bool shouldReloadTheme = SelectedThemeRequiresReload();

            _settings.Save();
            RefreshDirtyState();
            if (shouldReloadTheme && _reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings, _overlaySettings, GetSelectedThemeKey(), _isThemePickerExpanded);
                return;
            }

            ShowDefaultsRestoredMessage();
        }

        private void CancelSettingsChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmCancelSettingsChanges())
                return;

            bool shouldReloadTheme = SavedThemeRequiresReload();

            ApplySettingsToControls();
            UpdateAutomationInputStates();
            RefreshDirtyState();

            if (shouldReloadTheme && _reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings, _overlaySettings, _settings.ThemeKey, _isThemePickerExpanded);
                return;
            }

            if (shouldReloadTheme)
            {
                AppThemeManager.ApplyTheme(_settings.ThemeKey);
                RefreshThemeDrivenControls();
            }

            ShowChangesCanceledMessage();
        }

        private bool ConfirmCancelSettingsChanges()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Discard unsaved general settings changes and return to the last saved settings?",
                "Cancel Settings Changes",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }
    }
}
