using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
using JoinGameAfk.Services;
using JoinGameAfk.Theme;
using JoinGameAfk.Validation;

namespace JoinGameAfk.View
{
    public partial class SettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);
        private const int CollapsedPickerRows = 2;
        private const double ThemePickerTileOuterWidth = 192;
        private const double ThemePickerTileOuterHeight = 84;

        private readonly ChampSelectSettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly Action<ChampSelectSettings, string?, bool>? _reloadUiForTheme;
        private readonly Action<string>? _logMessage;
        private readonly Action<string>? _logErrorMessage;
        private readonly DataDragonChampionCatalogService _championCatalogRemoteService = new();
        private readonly SoundSettingsPage _soundSettingsPage;
        private readonly List<ThemePickerOption> _themeOptions = [];
        private Button[] _settingsSectionButtons = [];
        private FrameworkElement[] _settingsSectionViews = [];
        private NumericInputRule _readyCheckAcceptDelayRule = null!;
        private NumericInputRule _pickLockDelayRule = null!;
        private NumericInputRule _championHoverDelayRule = null!;
        private NumericInputRule _planningHoverDelayRule = null!;
        private NumericInputRule _banLockDelayRule = null!;
        private NumericInputRule _champSelectPollIntervalRule = null!;
        private NumericInputRule _champSelectEventFallbackPollIntervalRule = null!;
        private bool _isUpdatingAutomationControls;
        private bool _isApplyingSettingsToControls;
        private bool _isThemePickerExpanded;
        private string? _pendingInitialThemeSelectionKey;
        private string _selectedThemeKey = AppThemeManager.DefaultThemeKey;

        public SettingsPage(
            ChampSelectSettings settings,
            Action<ChampSelectSettings, string?, bool>? reloadUiForTheme = null,
            Action<string>? logMessage = null,
            Action<string>? logErrorMessage = null,
            string? selectedThemeKey = null,
            bool themePickerExpanded = false)
        {
            _settings = settings;
            InitializeComponent();
            _soundSettingsPage = new SoundSettingsPage(settings);
            SoundSettingsFrame.Content = _soundSettingsPage;
            _settingsSectionButtons = [GeneralSettingsSectionButton, SoundSettingsSectionButton];
            _settingsSectionViews = [GeneralSettingsPanel, SoundSettingsFrame];
            _reloadUiForTheme = reloadUiForTheme;
            _logMessage = logMessage;
            _logErrorMessage = logErrorMessage;
            _isThemePickerExpanded = themePickerExpanded;
            _pendingInitialThemeSelectionKey = string.IsNullOrWhiteSpace(selectedThemeKey)
                ? null
                : AppThemeManager.NormalizeThemeKey(selectedThemeKey);
            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                FloatingSettingsStatusBar.Visibility = Visibility.Collapsed;
            };

            StoragePathTextBlock.Text = AppStorage.DirectoryPath;
            ChampionPictureFolderPathTextBlock.Text = ChampionTileCatalog.TileDirectoryPath;
            RefreshChampionCatalogSyncStatus();
            RefreshChampionPictureCacheStatus();
            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
            _settings.Saved += Settings_Saved;
            Unloaded += SettingsPage_Unloaded;
            LoadThemeOptions();
            ApplySettingsToControls();
            ActivateSettingsSection(0);
            AttachNumericInputValidation();
            UpdateAutomationInputStates();
            AttachDirtyStateTracking();
            RefreshDirtyState();
        }

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

        private void AttachDirtyStateTracking()
        {
            CheckBox[] checkBoxes =
            [
                StartWatcherOnStartupCheckBox,
                InQueueAutomationCheckBox,
                AutoReadyCheckCheckBox,
                ChampionSelectAutomationCheckBox,
                AutoShowPickBanOverlayCheckBox,
                AutoClosePickBanOverlayCheckBox,
                OpenPickBanOverlayOnStartupCheckBox,
                OverlayTopmostCheckBox,
                OverlayShowPhaseSummaryCheckBox,
                OverlayShowTimersCheckBox,
                OverlayShowPickPlanCheckBox,
                OverlayShowBanPlanCheckBox,
                AutoHoverChampionCheckBox,
                AutoLockSelectionCheckBox,
                UseLiveEventsCheckBox,
                EventFallbackPollingCheckBox,
                AutoUpdateChampionCatalogOnStartupCheckBox
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

            OverlayScaleSlider.ValueChanged += OverlaySlider_ValueChanged;
            OverlayOpacitySlider.ValueChanged += OverlaySlider_ValueChanged;
        }

        private void DirtyTrackedControl_Changed(object sender, RoutedEventArgs e)
        {
            RefreshDirtyState();
        }

        private void DirtyTrackedControl_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshDirtyState();
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                if (DirtySettingsBar.Visibility == Visibility.Visible)
                    return;

                ApplySettingsToControls();
                UpdateAutomationInputStates();
                RefreshDirtyState();
            });
        }

        private void GeneralSettingsSectionButton_Click(object sender, RoutedEventArgs e) => ActivateSettingsSection(0);

        private void SoundSettingsSectionButton_Click(object sender, RoutedEventArgs e) => ActivateSettingsSection(1);

        private void ActivateSettingsSection(int index)
        {
            if (index < 0 || index >= _settingsSectionButtons.Length || index >= _settingsSectionViews.Length)
                index = 0;

            Brush activeTabForeground = TryFindResource("TabActiveForegroundBrush") as Brush ?? Brushes.White;
            Brush inactiveTabForeground = TryFindResource("TabInactiveForegroundBrush") as Brush ?? Brushes.Gray;
            Brush activeTabBorder = TryFindResource("TabActiveBorderBrush") as Brush ?? Brushes.DodgerBlue;
            Brush inactiveTabBorder = TryFindResource("TabInactiveBorderBrush") as Brush ?? Brushes.Transparent;

            for (int i = 0; i < _settingsSectionButtons.Length; i++)
            {
                bool isActive = i == index;
                _settingsSectionButtons[i].Tag = isActive ? "Active" : null;
                _settingsSectionButtons[i].Background = Brushes.Transparent;
                _settingsSectionButtons[i].Foreground = isActive ? activeTabForeground : inactiveTabForeground;
                _settingsSectionButtons[i].BorderBrush = isActive ? activeTabBorder : inactiveTabBorder;
                _settingsSectionViews[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshThemeDrivenControls()
        {
            UpdateAutomationInputStates();
            RefreshChampionCatalogSyncStatus();
            RefreshChampionPictureCacheStatus();
            InvalidateVisual();
        }

        private void OverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RefreshOverlaySliderValueText();
            RefreshDirtyState();
        }

        private void RefreshDirtyState()
        {
            if (_isApplyingSettingsToControls)
                return;

            bool hasDirtySettings = CaptureCurrentSettingsSnapshot() != CaptureSavedSettingsSnapshot();
            DirtySettingsBar.Visibility = hasDirtySettings ? Visibility.Visible : Visibility.Collapsed;
            if (hasDirtySettings)
            {
                _savedMessageTimer.Stop();
                FloatingSettingsStatusBar.Visibility = Visibility.Collapsed;
            }
        }

        private SettingsPageSnapshot CaptureCurrentSettingsSnapshot()
        {
            bool inQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;
            bool championSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && AutoHoverChampionCheckBox.IsChecked == true;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && AutoLockSelectionCheckBox.IsChecked == true;
            bool useLiveEvents = UseLiveEventsCheckBox.IsChecked == true;
            bool eventFallbackPollingEnabled = EventFallbackPollingCheckBox.IsChecked == true;
            var overlaySections = CaptureCurrentOverlaySectionSnapshot();

            return new SettingsPageSnapshot(
                StartWatcherOnStartupCheckBox.IsChecked == true,
                inQueueAutomationEnabled,
                autoReadyCheckEnabled,
                autoReadyCheckEnabled ? CreateNumericSnapshot(ReadyCheckAcceptDelayBox) : string.Empty,
                championSelectAutomationEnabled,
                AutoShowPickBanOverlayCheckBox.IsChecked == true,
                AutoClosePickBanOverlayCheckBox.IsChecked == true,
                OpenPickBanOverlayOnStartupCheckBox.IsChecked == true,
                GetOverlayScalePercent(),
                GetOverlayOpacityPercent(),
                OverlayTopmostCheckBox.IsChecked == true,
                overlaySections.ShowPhaseSummary,
                overlaySections.ShowTimers,
                overlaySections.ShowPickPlan,
                overlaySections.ShowBanPlan,
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
                AutoUpdateChampionCatalogOnStartupCheckBox.IsChecked == true);
        }

        private SettingsPageSnapshot CaptureSavedSettingsSnapshot()
        {
            bool inQueueAutomationEnabled = _settings.InQueueAutomationEnabled;
            bool autoReadyCheckEnabled = inQueueAutomationEnabled && _settings.AutoReadyCheckEnabled;
            bool championSelectAutomationEnabled = _settings.IsChampionSelectAutomationActive();
            bool autoHoverChampionEnabled = championSelectAutomationEnabled && _settings.AutoHoverChampionEnabled;
            bool autoLockSelectionEnabled = championSelectAutomationEnabled && _settings.AutoLockSelectionEnabled;
            bool useLiveEvents = _settings.UseChampSelectEventStream;
            bool eventFallbackPollingEnabled = _settings.ChampSelectEventFallbackPollingEnabled;

            return new SettingsPageSnapshot(
                _settings.StartWatcherOnStartup,
                inQueueAutomationEnabled,
                autoReadyCheckEnabled,
                autoReadyCheckEnabled ? _settings.ReadyCheckAcceptDelaySeconds.ToString() : string.Empty,
                championSelectAutomationEnabled,
                _settings.AutoShowPickBanOverlayEnabled,
                _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled,
                _settings.PickBanOverlayOpenOnStartup,
                ChampSelectSettings.NormalizePickBanOverlayScalePercent(_settings.PickBanOverlayScalePercent),
                ChampSelectSettings.NormalizePickBanOverlayOpacityPercent(_settings.PickBanOverlayOpacityPercent),
                _settings.PickBanOverlayTopmostEnabled,
                _settings.PickBanOverlayShowPhaseSummary,
                _settings.PickBanOverlayShowTimers,
                _settings.PickBanOverlayShowPickPlan,
                _settings.PickBanOverlayShowBanPlan,
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
                _settings.AutoUpdateChampionCatalogOnStartup);
        }

        private static string CreateNumericSnapshot(TextBox textBox)
        {
            return int.TryParse(textBox.Text, out int value)
                ? value.ToString()
                : $"invalid:{textBox.Text}";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSettingsInput(out var input))
                return;

            _settings.StartWatcherOnStartup = StartWatcherOnStartupCheckBox.IsChecked == true;
            _settings.InQueueAutomationEnabled = InQueueAutomationCheckBox.IsChecked == true;
            _settings.AutoReadyCheckEnabled = _settings.InQueueAutomationEnabled && AutoReadyCheckCheckBox.IsChecked == true;
            _settings.ReadyCheckAcceptDelaySeconds = input.ReadyCheckAcceptDelaySeconds;
            _settings.ChampionSelectAutomationEnabled = ChampionSelectAutomationCheckBox.IsChecked == true;
            _settings.AutoShowPickBanOverlayEnabled = AutoShowPickBanOverlayCheckBox.IsChecked == true;
            _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled = AutoClosePickBanOverlayCheckBox.IsChecked == true;
            _settings.PickBanOverlayOpenOnStartup = OpenPickBanOverlayOnStartupCheckBox.IsChecked == true;
            EnsureOverlayControlsHaveVisibleSection();
            var overlaySections = CaptureCurrentOverlaySectionSnapshot();
            _settings.PickBanOverlayScalePercent = GetOverlayScalePercent();
            _settings.PickBanOverlayOpacityPercent = GetOverlayOpacityPercent();
            _settings.PickBanOverlayTopmostEnabled = OverlayTopmostCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowPhaseSummary = overlaySections.ShowPhaseSummary;
            _settings.PickBanOverlayShowTimers = overlaySections.ShowTimers;
            _settings.PickBanOverlayShowPickPlan = overlaySections.ShowPickPlan;
            _settings.PickBanOverlayShowBanPlan = overlaySections.ShowBanPlan;
            _settings.NormalizePickBanOverlayOptions();
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
            bool shouldReloadTheme = SelectedThemeRequiresReload();

            _settings.Save();
            RefreshDirtyState();
            if (shouldReloadTheme && _reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings, GetSelectedThemeKey(), _isThemePickerExpanded);
                return;
            }

            ShowSavedMessage();
        }

        private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Restore default startup, automation, timing, overlay, performance, theme, and download-warning settings?\n\nChampion priorities and sound alerts are kept.",
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
                _reloadUiForTheme(_settings, GetSelectedThemeKey(), _isThemePickerExpanded);
                return;
            }

            ShowDefaultsRestoredMessage();
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
                AutoShowPickBanOverlayCheckBox.IsChecked = _settings.AutoShowPickBanOverlayEnabled;
                AutoClosePickBanOverlayCheckBox.IsChecked = _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled;
                OpenPickBanOverlayOnStartupCheckBox.IsChecked = _settings.PickBanOverlayOpenOnStartup;
                OverlayScaleSlider.Value = ChampSelectSettings.NormalizePickBanOverlayScalePercent(_settings.PickBanOverlayScalePercent);
                OverlayOpacitySlider.Value = ChampSelectSettings.NormalizePickBanOverlayOpacityPercent(_settings.PickBanOverlayOpacityPercent);
                OverlayTopmostCheckBox.IsChecked = _settings.PickBanOverlayTopmostEnabled;
                OverlayShowPhaseSummaryCheckBox.IsChecked = _settings.PickBanOverlayShowPhaseSummary;
                OverlayShowTimersCheckBox.IsChecked = _settings.PickBanOverlayShowTimers;
                OverlayShowPickPlanCheckBox.IsChecked = _settings.PickBanOverlayShowPickPlan;
                OverlayShowBanPlanCheckBox.IsChecked = _settings.PickBanOverlayShowBanPlan;
                RefreshOverlaySliderValueText();
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

        private void AutoUpdateChampionCatalogOnStartupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingAutomationControls)
                return;

            if (ConfirmChampionCatalogAutoUpdate())
                return;

            _isUpdatingAutomationControls = true;
            try
            {
                AutoUpdateChampionCatalogOnStartupCheckBox.IsChecked = false;
            }
            finally
            {
                _isUpdatingAutomationControls = false;
            }
        }

        private bool TryReadSettingsInput(out SettingsInputValues input)
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

            input = new SettingsInputValues(
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

        private void ShowSavedMessage()
        {
            ShowStatusMessage("Settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowDefaultsRestoredMessage()
        {
            ShowStatusMessage("Default settings restored.", "AccentGreenTextBrush", Brushes.ForestGreen);
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

        private void OpenStorageFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppStorage.EnsureDirectoryExists();
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppStorage.DirectoryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open storage folder: {ex.Message}", "Open Folder Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshChampionCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmChampionCatalogRefresh())
            {
                SetChampionCatalogRefreshStatus("Update canceled.", "TextSoftBrush", Brushes.SlateGray);
                return;
            }

            RefreshChampionCatalogButton.IsEnabled = false;
            SetChampionCatalogRefreshStatus("Updating champion list from Riot Data Dragon...", "TextSoftBrush", Brushes.SlateGray);

            try
            {
                var result = await ChampionCatalog.RefreshFromDataDragonAsync(_championCatalogRemoteService);
                RefreshChampionCatalogSyncStatus(result);
                SetChampionCatalogRefreshStatus(
                    "Champion list updated.",
                    "AccentGreenTextBrush",
                    Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetChampionCatalogRefreshStatus(
                    $"Champion list update failed. Existing local file was kept. {ex.Message}",
                    "DangerTextBrush",
                    Brushes.IndianRed);
            }
            finally
            {
                RefreshChampionCatalogButton.IsEnabled = true;
            }
        }

        private bool ConfirmChampionCatalogRefresh()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "This will use your internet connection to contact Riot Data Dragon at ddragon.leagueoflegends.com.\n\nThe app downloads Riot's public champion version and champion-name data, then updates only your local champions.json file.\n\nContinue?",
                "Update Champion List",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private bool ConfirmChampionCatalogAutoUpdate()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Check champion data updates on startup?\n\nWhen this is on, JoinGameAfk uses your internet connection at app startup to contact Riot Data Dragon at ddragon.leagueoflegends.com and fetch only the latest version first.\n\nIf your local champion list version is different, it downloads Riot's public champion-name data and updates champions.json. If your local picture cache version is different, it downloads the Data Dragon dragontail archive, extracts champion tiles, then deletes the archive.\n\nIf everything is already current, no champion list or image archive is downloaded.\n\nIf a request fails, the app keeps your existing local champion data and continues normally.",
                "Allow Startup Champion Data Update Check",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private bool ConfirmChampionPictureRefresh()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "This will download the latest Riot Data Dragon dragontail archive into local app storage. The archive can be large.\n\nAfter the download, JoinGameAfk extracts champion tile jpg files into the picture cache, then deletes the archive so only the champion tiles remain on disk.\n\nContinue?",
                "Download Data Dragon Archive",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private void RefreshChampionCatalogSyncStatus(ChampionCatalogRefreshResult? refreshResult = null)
        {
            string? dataDragonVersion = refreshResult?.DataDragonVersion;
            int championCount = refreshResult?.ChampionCount ?? 0;
            DateTime? lastSyncedAtUtc = refreshResult?.LastSyncedAtUtc;

            if (refreshResult is null)
            {
                var syncInfo = ChampionCatalog.GetLocalSyncInfo();
                dataDragonVersion = syncInfo.DataDragonVersion;
                championCount = syncInfo.ChampionCount;
                lastSyncedAtUtc = syncInfo.LastSyncedAtUtc;
            }

            if (string.IsNullOrWhiteSpace(dataDragonVersion))
            {
                SetChampionCatalogSyncStatus(
                    "Champion list has never been synced with Riot Data Dragon.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            SetChampionCatalogSyncStatus(
                $"Synced with Riot Data Dragon {dataDragonVersion} ({championCount} champions). Last sync: {FormatLastSyncedAt(lastSyncedAtUtc)}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private static string FormatLastSyncedAt(DateTime? lastSyncedAtUtc)
        {
            if (lastSyncedAtUtc is null)
                return "unknown";

            return DateTime.SpecifyKind(lastSyncedAtUtc.Value, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("g");
        }

        private static string FormatByteCount(long bytes)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        private void SetChampionCatalogSyncStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionCatalogSyncStatusTextBlock.Text = message;
            ChampionCatalogSyncStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
        }

        private void SetChampionCatalogRefreshStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionCatalogRefreshStatusLabel.Text = message;
            ChampionCatalogRefreshStatusLabel.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            ChampionCatalogRefreshStatusLabel.Visibility = Visibility.Visible;
        }

        private void RefreshChampionPictureCacheStatus(ChampionTileArchiveInstallResult? refreshResult = null)
        {
            if (refreshResult is not null)
            {
                string archiveCleanupText = refreshResult.ArchiveDeleted
                    ? "then removed the archive"
                    : $"but archive cleanup failed ({refreshResult.ArchiveDeleteError})";

                SetChampionPictureCacheStatus(
                    $"Picture cache synced with Riot Data Dragon {refreshResult.DataDragonVersion}. Downloaded {FormatByteCount(refreshResult.ArchiveSizeBytes)} archive, checked {refreshResult.CheckedTileCount} champion tiles, updated {refreshResult.UpdatedTileCount}, unchanged {refreshResult.UnchangedTileCount}, {archiveCleanupText}. Local folder currently has {refreshResult.CachedTileCount} jpg files. Last sync: {FormatLastSyncedAt(refreshResult.LastSyncedAtUtc)}.",
                    refreshResult.ArchiveDeleted ? "TextSoftBrush" : "DangerTextBrush",
                    refreshResult.ArchiveDeleted ? Brushes.SlateGray : Brushes.IndianRed);
                return;
            }

            var syncInfo = ChampionTileCatalog.GetCacheSyncInfo();
            int fileCount = ChampionTileCatalog.GetTileFileCount();
            if (string.IsNullOrWhiteSpace(syncInfo.DataDragonVersion))
            {
                SetChampionPictureCacheStatus(
                    $"Local picture cache has {fileCount} jpg files. It has not been synced with Riot Data Dragon yet.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            if (fileCount <= 0)
            {
                SetChampionPictureCacheStatus(
                    $"Picture cache has Riot Data Dragon {syncInfo.DataDragonVersion} recorded, but no champion tile jpg files were found. Use Download Archive to restore champion pictures.",
                    "DangerTextBrush",
                    Brushes.IndianRed);
                return;
            }

            if (!string.IsNullOrWhiteSpace(syncInfo.ArchiveFilePath))
            {
                SetChampionPictureCacheStatus(
                    $"Picture cache synced with Riot Data Dragon {syncInfo.DataDragonVersion}. Local folder currently has {fileCount} jpg files. Archive cleanup did not complete; {Path.GetFileName(syncInfo.ArchiveFilePath)} ({FormatByteCount(syncInfo.ArchiveSizeBytes)}) is still in local storage. Last sync: {FormatLastSyncedAt(syncInfo.LastSyncedAtUtc)}.",
                    "DangerTextBrush",
                    Brushes.IndianRed);
                return;
            }

            SetChampionPictureCacheStatus(
                $"Picture cache synced with Riot Data Dragon {syncInfo.DataDragonVersion}. Local folder currently has {fileCount} jpg files. Archive files are removed after extraction. Last sync: {FormatLastSyncedAt(syncInfo.LastSyncedAtUtc)}.",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private void SetChampionPictureCacheStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionPictureCacheStatusTextBlock.Text = message;
            ChampionPictureCacheStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
        }

        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshChampionCatalogSyncStatus();
            });
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RefreshChampionPictureCacheStatus();
            });
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _settings.Saved -= Settings_Saved;
            ChampionCatalog.CatalogChanged -= ChampionCatalog_CatalogChanged;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
            Unloaded -= SettingsPage_Unloaded;
        }

        private void LoadThemeOptions()
        {
            _themeOptions.Clear();
            _themeOptions.AddRange(AppThemeManager.Themes.Select(CreateThemePickerOption));
            ThemePickerItemsControl.ItemsSource = _themeOptions;
            SelectTheme(AppThemeManager.CurrentThemeKey);
            UpdateThemePickerExpansionState();
        }

        private void ThemeOptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ThemePickerOption option })
                return;

            SelectTheme(option.Key);
            RefreshDirtyState();
            PreviewTheme(option.Key);
        }

        private void PreviewTheme(string themeKey)
        {
            string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey);
            if (_reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings, normalizedThemeKey, _isThemePickerExpanded);
                return;
            }

            AppThemeManager.ApplyTheme(normalizedThemeKey);
            RefreshThemeDrivenControls();
        }

        private void ThemePickerExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _isThemePickerExpanded = !_isThemePickerExpanded;
            UpdateThemePickerExpansionState();
        }

        private void ThemePickerViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThemePickerExpansionState();
        }

        private void UpdateThemePickerExpansionState()
        {
            UpdatePickerExpansionState(
                ThemePickerViewport,
                ThemePickerExpandButton,
                _themeOptions.Count,
                ThemePickerTileOuterWidth,
                ThemePickerTileOuterHeight,
                _isThemePickerExpanded);
        }

        private static void UpdatePickerExpansionState(
            FrameworkElement viewport,
            Button expandButton,
            int itemCount,
            double itemOuterWidth,
            double itemOuterHeight,
            bool isExpanded)
        {
            double availableWidth = viewport.ActualWidth;
            int itemsPerRow = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / itemOuterWidth))
                : 1;

            bool needsExpansion = itemCount > itemsPerRow * CollapsedPickerRows;
            expandButton.Visibility = needsExpansion ? Visibility.Visible : Visibility.Collapsed;
            expandButton.Content = isExpanded ? "Show fewer" : "Show all";
            viewport.MaxHeight = needsExpansion && !isExpanded
                ? itemOuterHeight * CollapsedPickerRows
                : double.PositiveInfinity;
        }

        private void OpenChampionPictureFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppStorage.EnsureChampionTileDirectoryExists();
                Process.Start(new ProcessStartInfo
                {
                    FileName = ChampionTileCatalog.TileDirectoryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open champion pictures folder: {ex.Message}", "Open Folder Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReloadChampionPicturesButton_Click(object sender, RoutedEventArgs e)
        {
            AppStorage.EnsureChampionTileDirectoryExists();
            ChampionTileCatalog.Reload();
            RefreshChampionPictureCacheStatus();
            ShowStatusMessage($"Champion pictures reloaded from local storage ({ChampionTileCatalog.GetTileFileCount()} files).", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private async void DownloadChampionPictureArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmChampionPictureRefresh())
            {
                ChampionPictureDownloadStatusLabel.Text = "Data Dragon archive download canceled.";
                ChampionPictureDownloadStatusLabel.Visibility = Visibility.Visible;
                LogMessage("Champion picture archive download canceled by user.");
                return;
            }

            LogMessage("Manual champion picture archive install started.");
            SetChampionPictureDownloadControlsEnabled(false);
            ChampionPictureDownloadProgressBar.Visibility = Visibility.Visible;
            ChampionPictureDownloadProgressBar.IsIndeterminate = true;
            ChampionPictureDownloadProgressBar.Value = 0;
            ChampionPictureDownloadStatusLabel.Visibility = Visibility.Visible;
            ChampionPictureDownloadStatusLabel.Foreground = TryFindResource("TextSoftBrush") as Brush ?? Brushes.SlateGray;
            ChampionPictureDownloadStatusLabel.Text = "Preparing Riot Data Dragon archive download...";

            try
            {
                string? lastLoggedArchiveProgressMessage = null;
                var progress = new Progress<ChampionTileArchiveProgress>(snapshot =>
                {
                    ChampionPictureDownloadStatusLabel.Text = snapshot.Message;
                    if (ShouldLogChampionTileArchiveProgress(snapshot.Message)
                        && !string.Equals(snapshot.Message, lastLoggedArchiveProgressMessage, StringComparison.Ordinal))
                    {
                        lastLoggedArchiveProgressMessage = snapshot.Message;
                        if (IsChampionTileArchiveWarning(snapshot.Message))
                            LogErrorMessage(snapshot.Message);
                        else
                            LogMessage(snapshot.Message);
                    }

                    if (snapshot.TotalBytes is long totalBytes && totalBytes > 0)
                    {
                        ChampionPictureDownloadProgressBar.IsIndeterminate = false;
                        ChampionPictureDownloadProgressBar.Maximum = totalBytes;
                        ChampionPictureDownloadProgressBar.Value = Math.Min(snapshot.BytesCompleted, totalBytes);
                    }
                    else
                    {
                        ChampionPictureDownloadProgressBar.IsIndeterminate = true;
                    }
                });

                var result = await ChampionTileCatalog.InstallLatestDataDragonArchiveAsync(progress);
                RefreshChampionPictureCacheStatus(result);
                ChampionPictureDownloadStatusLabel.Foreground = result.ArchiveDeleted
                    ? TryFindResource("AccentGreenTextBrush") as Brush ?? Brushes.ForestGreen
                    : TryFindResource("DangerTextBrush") as Brush ?? Brushes.IndianRed;
                string archiveCleanupText = result.ArchiveDeleted
                    ? "then removed the archive"
                    : $"but could not remove the archive ({result.ArchiveDeleteError})";
                ChampionPictureDownloadStatusLabel.Text =
                    $"Data Dragon archive {result.DataDragonVersion} installed. Downloaded {FormatByteCount(result.ArchiveSizeBytes)}, checked {result.CheckedTileCount} champion tiles, updated {result.UpdatedTileCount}, unchanged {result.UnchangedTileCount}, {archiveCleanupText}. Cache now has {result.CachedTileCount} jpg files.";
                LogMessage($"Manual champion picture archive install completed for Riot Data Dragon {result.DataDragonVersion}. Downloaded {FormatByteCount(result.ArchiveSizeBytes)}; checked {result.CheckedTileCount} champion tiles; updated {result.UpdatedTileCount}; unchanged {result.UnchangedTileCount}; cache now has {result.CachedTileCount} jpg files.");
                if (!result.ArchiveDeleted)
                    LogErrorMessage($"Champion picture archive cleanup failed after successful extraction. {result.ArchiveDeleteError}");
            }
            catch (Exception ex)
            {
                ChampionPictureDownloadStatusLabel.Foreground = TryFindResource("DangerTextBrush") as Brush ?? Brushes.IndianRed;
                ChampionPictureDownloadStatusLabel.Text = $"Data Dragon archive install failed. Existing cache was kept. {ex.Message}";
                LogErrorMessage($"Manual champion picture archive install failed. Existing cache was kept. {FormatException(ex)}");
            }
            finally
            {
                ChampionPictureDownloadProgressBar.IsIndeterminate = false;
                SetChampionPictureDownloadControlsEnabled(true);
            }
        }

        private void SetChampionPictureDownloadControlsEnabled(bool enabled)
        {
            DownloadChampionPictureArchiveButton.IsEnabled = enabled;
            ReloadChampionPicturesButton.IsEnabled = enabled;
            OpenChampionPictureFolderButton.IsEnabled = enabled;
        }

        private void LogMessage(string message)
        {
            _logMessage?.Invoke(message);
        }

        private void LogErrorMessage(string message)
        {
            _logErrorMessage?.Invoke(message);
        }

        private static bool ShouldLogChampionTileArchiveProgress(string message)
        {
            return !string.IsNullOrWhiteSpace(message)
                && !message.StartsWith("Downloading Data Dragon archive:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChampionTileArchiveWarning(string message)
        {
            return message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatException(Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }

        private static ThemePickerOption CreateThemePickerOption(AppThemeDefinition theme)
        {
            var dictionary = CreateThemePreviewDictionary(theme);

            return new ThemePickerOption(
                theme.Key,
                theme.DisplayName,
                GetThemePreviewBrush(dictionary, "AppBackgroundBrush", Brushes.Black),
                GetThemePreviewBrush(dictionary, "AppSurfaceBrush", Brushes.DimGray),
                GetThemePreviewBrush(dictionary, "AppBorderBrush", Brushes.SlateGray),
                GetThemePreviewBrush(dictionary, "AppInputBrush", Brushes.DarkSlateGray),
                GetThemePreviewBrush(dictionary, "AccentBlueActionBrush", Brushes.DodgerBlue),
                GetThemePreviewBrush(dictionary, "TextInverseBrush", Brushes.White),
                GetThemePreviewBrush(dictionary, "TextPrimaryBrush", Brushes.WhiteSmoke),
                GetThemePreviewBrush(dictionary, "TextMutedBrush", Brushes.SlateGray));
        }

        private static ResourceDictionary? CreateThemePreviewDictionary(AppThemeDefinition theme)
        {
            try
            {
                string assemblyName = typeof(AppThemeManager).Assembly.GetName().Name ?? "JoinGameAfk";
                return new ResourceDictionary
                {
                    Source = new Uri($"/{assemblyName};component/{theme.Source}", UriKind.Relative)
                };
            }
            catch
            {
                return null;
            }
        }

        private static Brush GetThemePreviewBrush(ResourceDictionary? dictionary, string key, Brush fallback)
        {
            Brush brush = dictionary?[key] as Brush ?? fallback;
            var clone = brush.CloneCurrentValue();
            if (clone.CanFreeze)
                clone.Freeze();

            return clone;
        }

        private int GetOverlayScalePercent()
        {
            return ChampSelectSettings.NormalizePickBanOverlayScalePercent((int)Math.Round(OverlayScaleSlider.Value));
        }

        private int GetOverlayOpacityPercent()
        {
            return ChampSelectSettings.NormalizePickBanOverlayOpacityPercent((int)Math.Round(OverlayOpacitySlider.Value));
        }

        private OverlaySectionSnapshot CaptureCurrentOverlaySectionSnapshot()
        {
            var snapshot = new OverlaySectionSnapshot(
                OverlayShowPhaseSummaryCheckBox.IsChecked == true,
                OverlayShowTimersCheckBox.IsChecked == true,
                OverlayShowPickPlanCheckBox.IsChecked == true,
                OverlayShowBanPlanCheckBox.IsChecked == true);

            return snapshot.HasVisibleSection
                ? snapshot
                : snapshot with { ShowPhaseSummary = true };
        }

        private void EnsureOverlayControlsHaveVisibleSection()
        {
            if (OverlayShowPhaseSummaryCheckBox.IsChecked == true
                || OverlayShowTimersCheckBox.IsChecked == true
                || OverlayShowPickPlanCheckBox.IsChecked == true
                || OverlayShowBanPlanCheckBox.IsChecked == true)
            {
                return;
            }

            OverlayShowPhaseSummaryCheckBox.IsChecked = true;
        }

        private void RefreshOverlaySliderValueText()
        {
            if (OverlayScaleValueText is null || OverlayOpacityValueText is null)
                return;

            OverlayScaleValueText.Text = $"{GetOverlayScalePercent()}%";
            OverlayOpacityValueText.Text = $"{GetOverlayOpacityPercent()}%";
        }

        private void SelectTheme(string? themeKey)
        {
            string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey);
            _selectedThemeKey = normalizedThemeKey;

            foreach (var option in _themeOptions)
                option.IsSelected = string.Equals(option.Key, normalizedThemeKey, StringComparison.OrdinalIgnoreCase);

            SelectedThemeTextBlock.Text = _themeOptions.FirstOrDefault(option => option.IsSelected)?.DisplayName
                ?? AppThemeManager.Themes[0].DisplayName;
        }

        private string GetSelectedThemeKey()
        {
            return AppThemeManager.NormalizeThemeKey(_selectedThemeKey);
        }

        private bool SelectedThemeRequiresReload()
        {
            return !string.Equals(GetSelectedThemeKey(), AppThemeManager.CurrentThemeKey, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ThemePickerOption : INotifyPropertyChanged
        {
            private bool _isSelected;

            public ThemePickerOption(
                string key,
                string displayName,
                Brush backgroundBrush,
                Brush surfaceBrush,
                Brush borderBrush,
                Brush inputBrush,
                Brush buttonBrush,
                Brush buttonTextBrush,
                Brush textBrush,
                Brush mutedTextBrush)
            {
                Key = key;
                DisplayName = displayName;
                BackgroundBrush = backgroundBrush;
                SurfaceBrush = surfaceBrush;
                BorderBrush = borderBrush;
                InputBrush = inputBrush;
                ButtonBrush = buttonBrush;
                ButtonTextBrush = buttonTextBrush;
                TextBrush = textBrush;
                MutedTextBrush = mutedTextBrush;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public string Key { get; }
            public string DisplayName { get; }
            public Brush BackgroundBrush { get; }
            public Brush SurfaceBrush { get; }
            public Brush BorderBrush { get; }
            public Brush InputBrush { get; }
            public Brush ButtonBrush { get; }
            public Brush ButtonTextBrush { get; }
            public Brush TextBrush { get; }
            public Brush MutedTextBrush { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        private readonly record struct SettingsInputValues(
            int ReadyCheckAcceptDelaySeconds,
            int PickLockDelaySeconds,
            int ChampionHoverDelaySeconds,
            int PlanningHoverDelaySeconds,
            int BanLockDelaySeconds,
            int ChampSelectPollIntervalMs,
            int ChampSelectEventFallbackPollIntervalMs);

        private readonly record struct SettingsPageSnapshot(
            bool StartWatcherOnStartup,
            bool InQueueAutomationEnabled,
            bool AutoReadyCheckEnabled,
            string ReadyCheckAcceptDelaySeconds,
            bool ChampionSelectAutomationEnabled,
            bool AutoShowPickBanOverlayEnabled,
            bool PickBanOverlayAutoCloseAfterChampSelectEnabled,
            bool PickBanOverlayOpenOnStartup,
            int PickBanOverlayScalePercent,
            int PickBanOverlayOpacityPercent,
            bool PickBanOverlayTopmostEnabled,
            bool PickBanOverlayShowPhaseSummary,
            bool PickBanOverlayShowTimers,
            bool PickBanOverlayShowPickPlan,
            bool PickBanOverlayShowBanPlan,
            bool AutoHoverChampionEnabled,
            bool AutoLockSelectionEnabled,
            string PickLockDelaySeconds,
            string ChampionHoverDelaySeconds,
            string PlanningHoverDelaySeconds,
            string BanLockDelaySeconds,
            string ChampSelectPollIntervalMs,
            bool UseChampSelectEventStream,
            bool ChampSelectEventFallbackPollingEnabled,
            string ChampSelectEventFallbackPollIntervalMs,
            string ThemeKey,
            bool AutoUpdateChampionCatalogOnStartup);

        private readonly record struct OverlaySectionSnapshot(
            bool ShowPhaseSummary,
            bool ShowTimers,
            bool ShowPickPlan,
            bool ShowBanPlan)
        {
            public bool HasVisibleSection => ShowPhaseSummary
                || ShowTimers
                || ShowPickPlan
                || ShowBanPlan;
        }
    }
}
