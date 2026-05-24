using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.View
{
    public partial class OverlaySettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly OverlaySettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private bool _isApplyingSettingsToControls;

        public OverlaySettingsPage(OverlaySettings settings)
        {
            _settings = settings;
            InitializeComponent();

            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                FloatingOverlayStatusBar.Visibility = Visibility.Collapsed;
            };

            _settings.Saved += Settings_Saved;
            Unloaded += OverlaySettingsPage_Unloaded;
            ApplySettingsToControls();
            AttachDirtyStateTracking();
            RefreshDirtyState();
        }

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
                PickBanShowTimersCheckBox,
                PickBanShowPickPlanCheckBox,
                PickBanShowBanPlanCheckBox
            ];

            foreach (var checkBox in checkBoxes)
            {
                checkBox.Checked += TrackedControl_Changed;
                checkBox.Unchecked += TrackedControl_Changed;
            }

            QueueOverlayScaleSlider.ValueChanged += OverlaySlider_ValueChanged;
            PickBanScaleSlider.ValueChanged += OverlaySlider_ValueChanged;
            PickBanOpacitySlider.ValueChanged += OverlaySlider_ValueChanged;
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                if (DirtyOverlayBar.Visibility == Visibility.Visible)
                    return;

                ApplySettingsToControls();
                RefreshDirtyState();
            });
        }

        private void OverlaySettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _settings.Saved -= Settings_Saved;
            Unloaded -= OverlaySettingsPage_Unloaded;
        }

        private void ApplySettingsToControls()
        {
            _isApplyingSettingsToControls = true;
            try
            {
                QueueOverlayEnabledCheckBox.IsChecked = _settings.QueueMicroOverlayEnabled;
                QueueOverlayTopmostCheckBox.IsChecked = _settings.QueueMicroOverlayTopmostEnabled;
                QueueOverlayScaleSlider.Value = OverlaySettings.NormalizeQueueMicroOverlayScalePercent(_settings.QueueMicroOverlayScalePercent);

                PickBanAutoShowCheckBox.IsChecked = _settings.AutoShowPickBanOverlayEnabled;
                PickBanAutoCloseCheckBox.IsChecked = _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled;
                PickBanOpenOnStartupCheckBox.IsChecked = _settings.PickBanOverlayOpenOnStartup;
                PickBanTopmostCheckBox.IsChecked = _settings.PickBanOverlayTopmostEnabled;
                PickBanShowPhaseSummaryCheckBox.IsChecked = _settings.PickBanOverlayShowPhaseSummary;
                PickBanShowTimersCheckBox.IsChecked = _settings.PickBanOverlayShowTimers;
                PickBanShowPickPlanCheckBox.IsChecked = _settings.PickBanOverlayShowPickPlan;
                PickBanShowBanPlanCheckBox.IsChecked = _settings.PickBanOverlayShowBanPlan;
                PickBanScaleSlider.Value = OverlaySettings.NormalizePickBanOverlayScalePercent(_settings.PickBanOverlayScalePercent);
                PickBanOpacitySlider.Value = OverlaySettings.NormalizePickBanOverlayOpacityPercent(_settings.PickBanOverlayOpacityPercent);

                RefreshSliderValueText();
            }
            finally
            {
                _isApplyingSettingsToControls = false;
            }
        }

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
            _settings.QueueMicroOverlayEnabled = QueueOverlayEnabledCheckBox.IsChecked == true;
            _settings.QueueMicroOverlayTopmostEnabled = QueueOverlayTopmostCheckBox.IsChecked == true;
            _settings.QueueMicroOverlayScalePercent = GetQueueOverlayScalePercent();

            _settings.AutoShowPickBanOverlayEnabled = PickBanAutoShowCheckBox.IsChecked == true;
            _settings.PickBanOverlayAutoCloseAfterChampSelectEnabled = PickBanAutoCloseCheckBox.IsChecked == true;
            _settings.PickBanOverlayOpenOnStartup = PickBanOpenOnStartupCheckBox.IsChecked == true;
            _settings.PickBanOverlayTopmostEnabled = PickBanTopmostCheckBox.IsChecked == true;
            EnsureVisiblePickBanSection();
            _settings.PickBanOverlayShowPhaseSummary = PickBanShowPhaseSummaryCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowTimers = PickBanShowTimersCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowPickPlan = PickBanShowPickPlanCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowBanPlan = PickBanShowBanPlanCheckBox.IsChecked == true;

            int pickBanScalePercent = GetPickBanScalePercent();
            if (_settings.PickBanOverlayScalePercent != pickBanScalePercent)
            {
                _settings.PickBanOverlayWidth = null;
                _settings.PickBanOverlayHeight = null;
            }

            _settings.PickBanOverlayScalePercent = pickBanScalePercent;
            _settings.PickBanOverlayOpacityPercent = GetPickBanOpacityPercent();
            _settings.NormalizeOptions();
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
                PickBanShowTimersCheckBox.IsChecked == true,
                PickBanShowPickPlanCheckBox.IsChecked == true,
                PickBanShowBanPlanCheckBox.IsChecked == true,
                GetPickBanScalePercent(),
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
                _settings.PickBanOverlayShowTimers,
                _settings.PickBanOverlayShowPickPlan,
                _settings.PickBanOverlayShowBanPlan,
                OverlaySettings.NormalizePickBanOverlayScalePercent(_settings.PickBanOverlayScalePercent),
                OverlaySettings.NormalizePickBanOverlayOpacityPercent(_settings.PickBanOverlayOpacityPercent));
        }

        private void EnsureVisiblePickBanSection()
        {
            if (PickBanShowPhaseSummaryCheckBox.IsChecked == true
                || PickBanShowTimersCheckBox.IsChecked == true
                || PickBanShowPickPlanCheckBox.IsChecked == true
                || PickBanShowBanPlanCheckBox.IsChecked == true)
            {
                return;
            }

            PickBanShowPhaseSummaryCheckBox.IsChecked = true;
        }

        private int GetQueueOverlayScalePercent()
        {
            return OverlaySettings.NormalizeQueueMicroOverlayScalePercent((int)Math.Round(QueueOverlayScaleSlider.Value));
        }

        private int GetPickBanScalePercent()
        {
            return OverlaySettings.NormalizePickBanOverlayScalePercent((int)Math.Round(PickBanScaleSlider.Value));
        }

        private int GetPickBanOpacityPercent()
        {
            return OverlaySettings.NormalizePickBanOverlayOpacityPercent((int)Math.Round(PickBanOpacitySlider.Value));
        }

        private void RefreshSliderValueText()
        {
            if (QueueOverlayScaleValueText is null)
                return;

            QueueOverlayScaleValueText.Text = $"{GetQueueOverlayScalePercent()}%";
            PickBanScaleValueText.Text = $"{GetPickBanScalePercent()}%";
            PickBanOpacityValueText.Text = $"{GetPickBanOpacityPercent()}%";
        }

        private void ShowStatusMessage(string message, string brushKey, Brush fallbackBrush)
        {
            FloatingOverlayStatusText.Text = message;
            FloatingOverlayStatusText.Foreground = TryFindResource(brushKey) as Brush ?? fallbackBrush;
            FloatingOverlayStatusBar.Visibility = Visibility.Visible;
            _savedMessageTimer.Stop();
            _savedMessageTimer.Start();
        }

        private readonly record struct OverlaySettingsSnapshot(
            bool QueueMicroOverlayEnabled,
            bool QueueMicroOverlayTopmostEnabled,
            int QueueMicroOverlayScalePercent,
            bool AutoShowPickBanOverlayEnabled,
            bool PickBanOverlayAutoCloseAfterChampSelectEnabled,
            bool PickBanOverlayOpenOnStartup,
            bool PickBanOverlayTopmostEnabled,
            bool PickBanOverlayShowPhaseSummary,
            bool PickBanOverlayShowTimers,
            bool PickBanOverlayShowPickPlan,
            bool PickBanOverlayShowBanPlan,
            int PickBanOverlayScalePercent,
            int PickBanOverlayOpacityPercent);
    }
}
