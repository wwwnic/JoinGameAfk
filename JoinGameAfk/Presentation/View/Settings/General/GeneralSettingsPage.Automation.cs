using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Validation;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void AttachNumericInputValidation()
        {
            _readyCheckAcceptDelayRule = InputValidator.AttachInteger(ReadyCheckAcceptDelayBox, "Auto accept delay", minimum: 0);
            _pickLockDelayRule = InputValidator.AttachInteger(PickLockDelayBox, "Pick lock timer", minimum: 0);
            _championHoverDelayRule = InputValidator.AttachInteger(ChampionHoverDelayBox, "Champion hover delay", minimum: 0);
            _planningHoverDelayRule = InputValidator.AttachInteger(PlanningHoverDelayBox, "Planning hover delay", minimum: 0);
            _banLockDelayRule = InputValidator.AttachInteger(BanLockDelayBox, "Ban lock timer", minimum: 0);
            _champSelectPollIntervalRule = InputValidator.AttachInteger(ChampSelectPollIntervalBox, "Regular polling interval", minimum: 100, maximum: 5000);
            _champSelectEventFallbackPollIntervalRule = InputValidator.AttachInteger(EventFallbackPollIntervalBox, "Event fallback polling interval", minimum: 1000, maximum: 30000);
        }

        private void ApplySettingsToControls()
        {
            bool inQueueAutomationEnabled = _settings.InQueueAutomationEnabled;
            bool championSelectAutomationEnabled = _settings.IsChampionSelectAutomationActive();

            _isApplyingSettingsToControls = true;
            _isUpdatingAutomationControls = true;
            try
            {
                StartWatcherOnStartupCheckBox.IsChecked = _settings.StartWatcherOnStartup;
                InQueueAutomationCheckBox.IsChecked = inQueueAutomationEnabled;
                AutoReadyCheckCheckBox.IsChecked = inQueueAutomationEnabled && _settings.AutoReadyCheckEnabled;
                ReadyCheckAcceptDelayBox.Text = _settings.ReadyCheckAcceptDelaySeconds.ToString();
                ChampionSelectAutomationCheckBox.IsChecked = championSelectAutomationEnabled;
                AutoHoverChampionCheckBox.IsChecked = championSelectAutomationEnabled && _settings.AutoHoverChampionEnabled;
                AutoLockSelectionCheckBox.IsChecked = championSelectAutomationEnabled && _settings.AutoLockSelectionEnabled;
                PickLockDelayBox.Text = _settings.PickLockDelaySeconds.ToString();
                ChampionHoverDelayBox.Text = _settings.ChampionHoverDelaySeconds.ToString();
                PlanningHoverDelayBox.Text = _settings.PlanningHoverDelaySeconds.ToString();
                BanLockDelayBox.Text = _settings.BanLockDelaySeconds.ToString();
                ChampSelectPollIntervalBox.Text = _settings.ChampSelectPollIntervalMs.ToString();
                UseLiveEventsCheckBox.IsChecked = _settings.UseChampSelectEventStream;
                EventFallbackPollingCheckBox.IsChecked = _settings.ChampSelectEventFallbackPollingEnabled;
                EventFallbackPollIntervalBox.Text = _settings.ChampSelectEventFallbackPollIntervalMs.ToString();
                string themeKeyToSelect = _pendingInitialThemeSelectionKey ?? _settings.ThemeKey;
                _pendingInitialThemeSelectionKey = null;
                SelectTheme(themeKeyToSelect);
                AutoUpdateChampionCatalogOnStartupCheckBox.IsChecked = _settings.AutoUpdateChampionCatalogOnStartup;
                DownloadRawChampionPicturesCheckBox.IsChecked = _settings.DownloadRawChampionPictures;
                UpdateThemePickerExpansionState();
            }
            finally
            {
                _isUpdatingAutomationControls = false;
                _isApplyingSettingsToControls = false;
            }
        }

        private void AutomationCheckBox_StateChanged(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingAutomationControls
                && ReferenceEquals(sender, AutoReadyCheckCheckBox))
            {
                SyncInQueueAutomationCheckBoxFromChildren();
            }

            if (!_isUpdatingAutomationControls
                && (ReferenceEquals(sender, AutoHoverChampionCheckBox) || ReferenceEquals(sender, AutoLockSelectionCheckBox)))
            {
                SyncChampionSelectAutomationCheckBoxFromChildren();
            }

            UpdateAutomationInputStates();
        }

        private void PerformanceCheckBox_StateChanged(object sender, RoutedEventArgs e)
        {
            UpdateAutomationInputStates();
        }

        private void InQueueAutomationCheckBox_StateChanged(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingAutomationControls)
            {
                bool inQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;

                _isUpdatingAutomationControls = true;
                try
                {
                    AutoReadyCheckCheckBox.IsChecked = inQueueAutomationEnabled;
                }
                finally
                {
                    _isUpdatingAutomationControls = false;
                }
            }

            UpdateAutomationInputStates();
        }

        private void ChampionSelectAutomationCheckBox_StateChanged(object sender, RoutedEventArgs e)
        {
            if (!_isUpdatingAutomationControls)
            {
                bool championSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;

                _isUpdatingAutomationControls = true;
                try
                {
                    if (championSelectAutomationEnabled)
                    {
                        if (AutoHoverChampionCheckBox.IsChecked != true
                            && AutoLockSelectionCheckBox.IsChecked != true)
                        {
                            AutoHoverChampionCheckBox.IsChecked = true;
                            AutoLockSelectionCheckBox.IsChecked = true;
                        }
                    }
                    else
                    {
                        AutoHoverChampionCheckBox.IsChecked = false;
                        AutoLockSelectionCheckBox.IsChecked = false;
                    }
                }
                finally
                {
                    _isUpdatingAutomationControls = false;
                }
            }

            UpdateAutomationInputStates();
        }

        private void SyncInQueueAutomationCheckBoxFromChildren()
        {
            bool hasInQueueAutomation = AutoReadyCheckCheckBox.IsChecked == true;

            if (InQueueAutomationCheckBox.IsChecked == hasInQueueAutomation)
                return;

            _isUpdatingAutomationControls = true;
            try
            {
                InQueueAutomationCheckBox.IsChecked = hasInQueueAutomation;
            }
            finally
            {
                _isUpdatingAutomationControls = false;
            }
        }

        private void SyncChampionSelectAutomationCheckBoxFromChildren()
        {
            bool hasChampionSelectAutomation = AutoHoverChampionCheckBox.IsChecked == true
                || AutoLockSelectionCheckBox.IsChecked == true;

            if (ChampionSelectAutomationCheckBox.IsChecked == hasChampionSelectAutomation)
                return;

            _isUpdatingAutomationControls = true;
            try
            {
                ChampionSelectAutomationCheckBox.IsChecked = hasChampionSelectAutomation;
            }
            finally
            {
                _isUpdatingAutomationControls = false;
            }
        }

        private void UpdateAutomationInputStates()
        {
            bool inQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;
            bool championSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && AutoHoverChampionCheckBox.IsChecked == true;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && AutoLockSelectionCheckBox.IsChecked == true;

            UpdateAutomationMasterRowState(InQueueAutomationMasterRow, inQueueAutomationEnabled);
            UpdateAutomationMasterRowState(ChampionSelectAutomationMasterRow, championSelectAutomationEnabled);

            InQueueAutomationOptionsPanel.IsEnabled = inQueueAutomationEnabled;
            AutoReadyCheckCheckBox.IsEnabled = inQueueAutomationEnabled;
            ReadyCheckAcceptDelayBox.IsEnabled = autoReadyCheckEnabled;
            ChampionSelectAutomationOptionsPanel.IsEnabled = championSelectAutomationEnabled;
            ChampionHoverDelayBox.IsEnabled = autoHoverChampionEnabled;
            PlanningHoverDelayBox.IsEnabled = autoHoverChampionEnabled;
            PickLockDelayBox.IsEnabled = autoLockSelectionEnabled;
            BanLockDelayBox.IsEnabled = autoLockSelectionEnabled;
            bool liveEventsEnabled = UseLiveEventsCheckBox.IsChecked == true;
            bool eventFallbackPollingEnabled = liveEventsEnabled && EventFallbackPollingCheckBox.IsChecked == true;
            ChampSelectPollIntervalBox.IsEnabled = !liveEventsEnabled;
            EventFallbackPollingCheckBox.IsEnabled = liveEventsEnabled;
            EventFallbackPollIntervalBox.IsEnabled = eventFallbackPollingEnabled;

            if (!autoReadyCheckEnabled)
                InputValidator.SetValidationState(ReadyCheckAcceptDelayBox, InputValidationState.Valid);
            else if (_readyCheckAcceptDelayRule is not null)
                _readyCheckAcceptDelayRule.Validate();

            if (!autoHoverChampionEnabled)
            {
                InputValidator.SetValidationState(ChampionHoverDelayBox, InputValidationState.Valid);
                InputValidator.SetValidationState(PlanningHoverDelayBox, InputValidationState.Valid);
            }
            else if (_championHoverDelayRule is not null)
            {
                _championHoverDelayRule.Validate();
                _planningHoverDelayRule?.Validate();
            }

            if (!autoLockSelectionEnabled)
            {
                InputValidator.SetValidationState(PickLockDelayBox, InputValidationState.Valid);
                InputValidator.SetValidationState(BanLockDelayBox, InputValidationState.Valid);
            }
            else
            {
                _pickLockDelayRule?.Validate();
                _banLockDelayRule?.Validate();
            }

            if (!eventFallbackPollingEnabled)
                InputValidator.SetValidationState(EventFallbackPollIntervalBox, InputValidationState.Valid);
            else
                _champSelectEventFallbackPollIntervalRule?.Validate();

            if (liveEventsEnabled)
                InputValidator.SetValidationState(ChampSelectPollIntervalBox, InputValidationState.Valid);
            else
                _champSelectPollIntervalRule?.Validate();
        }

        private void UpdateAutomationMasterRowState(Border row, bool enabled)
        {
            row.Background = TryFindResource(enabled ? "AppInputFocusBrush" : "DangerSurfaceDeepBrush") as Brush
                ?? (enabled ? Brushes.Transparent : Brushes.DarkRed);
            row.BorderBrush = TryFindResource(enabled ? "AccentBlueBrush" : "DangerAccentBrush") as Brush
                ?? (enabled ? Brushes.DodgerBlue : Brushes.IndianRed);
        }

        private bool TryReadSettingsInput(out GeneralSettingsInputValues input)
        {
            input = default;

            if (!InputValidator.TryValidateAll(GetActiveNumericInputRules(), out var invalidRule, out string errorMessage))
            {
                ShowValidationMessage(errorMessage);
                invalidRule?.TextBox.Focus();
                invalidRule?.TextBox.SelectAll();
                return false;
            }

            int readyCheckDelay = _settings.ReadyCheckAcceptDelaySeconds;
            int hoverDelay = _settings.ChampionHoverDelaySeconds;
            int planningHoverDelay = _settings.PlanningHoverDelaySeconds;
            int pickDelay = _settings.PickLockDelaySeconds;
            int banDelay = _settings.BanLockDelaySeconds;
            int pollInterval = _settings.ChampSelectPollIntervalMs;
            int eventFallbackPollInterval = _settings.ChampSelectEventFallbackPollIntervalMs;

            bool inQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;
            bool championSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && AutoHoverChampionCheckBox.IsChecked == true;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && AutoLockSelectionCheckBox.IsChecked == true;
            bool useLiveEvents = UseLiveEventsCheckBox.IsChecked == true;
            bool eventFallbackPollingEnabled = useLiveEvents && EventFallbackPollingCheckBox.IsChecked == true;

            if ((autoReadyCheckEnabled && !_readyCheckAcceptDelayRule.TryGetInt32(out readyCheckDelay))
                || (autoLockSelectionEnabled && !_pickLockDelayRule.TryGetInt32(out pickDelay))
                || (autoHoverChampionEnabled && !_championHoverDelayRule.TryGetInt32(out hoverDelay))
                || (autoHoverChampionEnabled && !_planningHoverDelayRule.TryGetInt32(out planningHoverDelay))
                || (autoLockSelectionEnabled && !_banLockDelayRule.TryGetInt32(out banDelay))
                || (!useLiveEvents && !_champSelectPollIntervalRule.TryGetInt32(out pollInterval))
                || (eventFallbackPollingEnabled && !_champSelectEventFallbackPollIntervalRule.TryGetInt32(out eventFallbackPollInterval)))
            {
                ShowValidationMessage("Fix invalid settings before saving.");
                return false;
            }

            input = new GeneralSettingsInputValues(
                readyCheckDelay,
                pickDelay,
                hoverDelay,
                planningHoverDelay,
                banDelay,
                pollInterval,
                eventFallbackPollInterval);

            return true;
        }

        private IEnumerable<NumericInputRule> GetActiveNumericInputRules()
        {
            bool inQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;

            if (autoReadyCheckEnabled)
                yield return _readyCheckAcceptDelayRule;

            bool championSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && AutoHoverChampionCheckBox.IsChecked == true;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && AutoLockSelectionCheckBox.IsChecked == true;

            if (autoLockSelectionEnabled)
                yield return _pickLockDelayRule;

            if (autoHoverChampionEnabled)
            {
                yield return _championHoverDelayRule;
                yield return _planningHoverDelayRule;
            }

            if (autoLockSelectionEnabled)
                yield return _banLockDelayRule;

            if (UseLiveEventsCheckBox.IsChecked != true)
                yield return _champSelectPollIntervalRule;

            if (UseLiveEventsCheckBox.IsChecked == true
                && EventFallbackPollingCheckBox.IsChecked == true)
                yield return _champSelectEventFallbackPollIntervalRule;
        }
    }
}