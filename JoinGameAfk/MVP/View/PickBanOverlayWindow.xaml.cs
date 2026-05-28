using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.View
{
    public partial class PickBanOverlayWindow : Window
    {
        private const double FullLayoutBaseWidth = 360;
        private const double FullLayoutBaseHeight = 435;
        private const double ScreenPadding = 8;
        private const double SettingsPopupWidth = 332;
        private const double SettingsPopupGap = 8;
        private static readonly TimeSpan OverlaySettingsSaveDelay = TimeSpan.FromMilliseconds(350);

        private readonly OverlaySettings _settings;
        private readonly DraftCountdownTimer _draftCountdownTimer;
        private readonly DispatcherTimer _overlaySettingsSaveTimer;
        private DashboardStatus _lastDashboardStatus = new();
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private bool _isApplyingOverlaySettings;
        private bool _isResizingOverlay;
        private Point _resizeStartPoint;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private string _champSelectSubPhase = string.Empty;
        private string _readyCheckResponse = string.Empty;

        public event Action<double, double>? PositionChangedByUser;
        public event Action? ToggleMainAppRequested;

        public PickBanOverlayWindow(OverlaySettings settings)
        {
            _settings = settings;
            _settings.NormalizePickBanOverlayOptions();
            InitializeComponent();
            ScaleSlider.ValueChanged += OverlaySlider_ValueChanged;
            OpacitySlider.ValueChanged += OverlaySlider_ValueChanged;

            _overlaySettingsSaveTimer = new DispatcherTimer
            {
                Interval = OverlaySettingsSaveDelay
            };
            _overlaySettingsSaveTimer.Tick += OverlaySettingsSaveTimer_Tick;

            _draftCountdownTimer = new DraftCountdownTimer(RenderCountdownTimers);

            ApplyOverlaySettings(updateControls: true);
            RefreshStatusDisplay();
            UpdateDashboardStatus(new DashboardStatus());
            _settings.Saved += Settings_Saved;
            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
            ChampionImageSelectionStore.SelectionsChanged += ChampionImageSelectionStore_SelectionsChanged;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatch(() =>
            {
                _isWatcherRunning = isRunning;
                RefreshStatusDisplay();
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatch(() =>
            {
                _isClientConnected = isConnected;
                RefreshStatusDisplay();
            });
        }

        public void UpdatePhase(ClientPhase phase)
        {
            Dispatch(() =>
            {
                _currentPhase = phase;
                RefreshStatusDisplay();
            });
        }

        public void UpdateChampSelectSubPhase(string subPhase)
        {
            Dispatch(() =>
            {
                _champSelectSubPhase = subPhase;
                RefreshStatusDisplay();
            });
        }

        public void UpdateDashboardStatus(DashboardStatus status)
        {
            Dispatch(() =>
            {
                _lastDashboardStatus = status;
                _readyCheckResponse = status.ReadyCheckResponse;
                RenderDashboardStatus(status);
                RefreshStatusDisplay();
            });
        }

        private void Settings_Saved()
        {
            Dispatch(() =>
            {
                ApplyOverlaySettings(updateControls: true);
                RenderDashboardStatus(_lastDashboardStatus);
            });
        }

        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatch(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatch(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionImageSelectionStore_SelectionsChanged(object? sender, EventArgs e)
        {
            Dispatch(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void RenderDashboardStatus(DashboardStatus status)
        {
            var pickDisplayItems = DashboardChampionPlanDisplay.CreateList(status.PickChampionPriority);
            var banDisplayItems = DashboardChampionPlanDisplay.CreateList(status.BanChampionPriority);

            PickPlanList.ItemsSource = pickDisplayItems;
            BanPlanList.ItemsSource = banDisplayItems;

            UpdatePlanPlaceholder(
                PickPlaceholderText,
                pickDisplayItems,
                status.PickChampionText);
            UpdatePlanPlaceholder(
                BanPlaceholderText,
                banDisplayItems,
                status.BanChampionText);

            PickLockText.Text = GetPlanLockText(status.PickLockText);
            BanLockText.Text = GetPlanLockText(status.BanLockText);
            PositionText.Text = GetPositionText(status.CurrentPosition);
            _draftCountdownTimer.Update(status);
        }

        private void Dispatch(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }

        private static void UpdatePlanPlaceholder(
            TextBlock placeholder,
            IReadOnlyCollection<DashboardChampionPlanDisplayItem> champions,
            string fallbackText)
        {
            bool hasChampions = champions.Count > 0;
            placeholder.Visibility = hasChampions ? Visibility.Collapsed : Visibility.Visible;
            if (!hasChampions)
                placeholder.Text = string.IsNullOrWhiteSpace(fallbackText) ? "Waiting for champion select." : fallbackText;
        }

        private void RenderCountdownTimers(DraftCountdownTimerSnapshot snapshot)
        {
            TimerText.Text = snapshot.PhaseTimeText;
            LockTimerText.Text = snapshot.LockTimeText;
            LockTimerLabel.Text = snapshot.HasActiveLockTimer
                ? GetCountdownLabel(snapshot.ActiveLockActionType)
                : "lock";
        }

        private static string GetCountdownLabel(string actionType)
        {
            return string.Equals(actionType, "Hover", StringComparison.Ordinal)
                ? "hover"
                : $"{actionType.ToLowerInvariant()} lock";
        }

        private void RefreshStatusDisplay()
        {
            StatusText.Text = GetStatusLine(_currentPhase);
            PhaseText.Text = GetPhaseText(_currentPhase);
            OverlayPhaseIndicator.Update(
                _currentPhase,
                _isWatcherRunning,
                _isClientConnected,
                GetPhaseIndicatorState(),
                _lastDashboardStatus.ReadyCheckAutoAcceptDelayMilliseconds,
                _lastDashboardStatus.ReadyCheckAutoAcceptTimeLeftMilliseconds,
                _lastDashboardStatus.ReadyCheckAutoAcceptObservedAtUtc,
                _lastDashboardStatus.AllConfiguredOptionsUnavailable);
        }

        private string GetPhaseIndicatorState()
        {
            return _currentPhase == ClientPhase.ReadyCheck
                ? _readyCheckResponse
                : _champSelectSubPhase;
        }

        private string GetStatusLine(ClientPhase phase)
        {
            return phase switch
            {
                ClientPhase.Lobby => "Lobby",
                ClientPhase.Matchmaking => "In Queue",
                ClientPhase.ReadyCheck => "Ready Check",
                ClientPhase.ChampSelect => "Champion Select",
                ClientPhase.Planning => "Planning",
                ClientPhase.InGame => "In Game",
                _ when _isWatcherRunning && !_isClientConnected => "Waiting for client",
                _ when _isWatcherRunning => "Watching",
                _ => "Stopped"
            };
        }

        private string GetPhaseText(ClientPhase phase)
        {
            if (phase is ClientPhase.ChampSelect or ClientPhase.Planning)
            {
                string subPhase = string.IsNullOrWhiteSpace(_champSelectSubPhase)
                    ? "Idle"
                    : _champSelectSubPhase;
                return subPhase;
            }

            return GetStatusLine(phase);
        }

        private static string GetPositionText(Position position)
        {
            return position is Position.None or Position.Default
                ? "No role"
                : position.ToString();
        }

        private static string GetPlanLockText(string lockText)
        {
            return string.IsNullOrWhiteSpace(lockText)
                ? "Lock timing unavailable."
                : lockText;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPopup.IsOpen)
            {
                SettingsPopup.IsOpen = false;
                return;
            }

            UpdateSettingsPopupPlacement();
            SettingsPopup.IsOpen = true;
        }

        private void OpenMainAppButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPopup.IsOpen = false;
            ToggleMainAppRequested?.Invoke();
        }

        private void OverlayOptionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            QueueOverlaySettingsSave();
        }

        private void OverlaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            QueueOverlaySettingsSave();
        }

        private void ResetOverlayViewButton_Click(object sender, RoutedEventArgs e)
        {
            ResetOverlayDisplayDefaults();
            _settings.PickBanOverlayOpacityPercent = OverlaySettings.DefaultPickBanOverlayOpacityPercent;
            _settings.PickBanOverlayTopmostEnabled = true;
            ApplyOverlaySettings(updateControls: true);
            QueueOverlaySettingsSave();
        }

        private void ResetOverlayDisplayDefaults()
        {
            _settings.PickBanOverlayScalePercent = OverlaySettings.DefaultPickBanOverlayScalePercent;
            _settings.PickBanOverlayWidth = null;
            _settings.PickBanOverlayHeight = null;
            _settings.PickBanOverlayShowPhaseSummary = true;
            _settings.PickBanOverlayShowTimers = true;
            _settings.PickBanOverlayShowPickPlan = true;
            _settings.PickBanOverlayShowBanPlan = true;
        }

        private void ApplyOverlaySettings(bool updateControls)
        {
            _settings.NormalizePickBanOverlayOptions();
            EnsureAtLeastOneOverlaySection();
            _isApplyingOverlaySettings = true;
            try
            {
                Topmost = _settings.PickBanOverlayTopmostEnabled;
                OverlayPanel.Opacity = _settings.PickBanOverlayOpacityPercent / 100d;
                ApplyOverlaySectionVisibility();
                ApplyOverlaySize();

                if (updateControls)
                    ApplySettingsToOverlayControls();
            }
            finally
            {
                _isApplyingOverlaySettings = false;
            }

            UpdatePlanDisplayForCurrentLayout();
            ClampToVirtualScreen();
        }

        private void ApplySettingsToOverlayControls()
        {
            ScaleSlider.Value = _settings.PickBanOverlayScalePercent;
            OpacitySlider.Value = _settings.PickBanOverlayOpacityPercent;
            TopmostCheckBox.IsChecked = _settings.PickBanOverlayTopmostEnabled;
            ShowPhaseSummaryCheckBox.IsChecked = _settings.PickBanOverlayShowPhaseSummary;
            ShowTimersCheckBox.IsChecked = _settings.PickBanOverlayShowTimers;
            ShowPickPlanCheckBox.IsChecked = _settings.PickBanOverlayShowPickPlan;
            ShowBanPlanCheckBox.IsChecked = _settings.PickBanOverlayShowBanPlan;
            RefreshSliderValueText();
        }

        private void CaptureOverlayControlsToSettings()
        {
            if (_isApplyingOverlaySettings)
                return;

            int scalePercent = OverlaySettings.NormalizePickBanOverlayScalePercent((int)Math.Round(ScaleSlider.Value));
            if (_settings.PickBanOverlayScalePercent != scalePercent)
            {
                _settings.PickBanOverlayWidth = null;
                _settings.PickBanOverlayHeight = null;
            }

            _settings.PickBanOverlayScalePercent = scalePercent;
            _settings.PickBanOverlayOpacityPercent = OverlaySettings.NormalizePickBanOverlayOpacityPercent((int)Math.Round(OpacitySlider.Value));
            _settings.PickBanOverlayTopmostEnabled = TopmostCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowPhaseSummary = ShowPhaseSummaryCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowTimers = ShowTimersCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowPickPlan = ShowPickPlanCheckBox.IsChecked == true;
            _settings.PickBanOverlayShowBanPlan = ShowBanPlanCheckBox.IsChecked == true;
            EnsureAtLeastOneOverlaySection();
        }

        private void QueueOverlaySettingsSave()
        {
            if (_isApplyingOverlaySettings)
                return;

            CaptureOverlayControlsToSettings();
            ApplyOverlaySettings(updateControls: true);
            QueueOverlaySettingsPersist();
        }

        private void QueueOverlaySettingsPersist()
        {
            _overlaySettingsSaveTimer.Stop();
            _overlaySettingsSaveTimer.Start();
        }

        private void OverlaySettingsSaveTimer_Tick(object? sender, EventArgs e)
        {
            _overlaySettingsSaveTimer.Stop();
            _settings.NormalizePickBanOverlayOptions();
            _settings.Save();
        }

        private void EnsureAtLeastOneOverlaySection()
        {
            _settings.EnsurePickBanOverlayHasVisibleSection();
        }

        private void ApplyOverlaySectionVisibility()
        {
            bool showPhaseSummary = _settings.PickBanOverlayShowPhaseSummary;
            bool showTimers = _settings.PickBanOverlayShowTimers;

            PhaseTimerSection.Visibility = ToVisibility(showPhaseSummary || showTimers);
            PhaseSummaryPanel.Visibility = ToVisibility(showPhaseSummary);
            PhaseTimerCard.Visibility = ToVisibility(showTimers);
            LockTimerCard.Visibility = ToVisibility(showTimers);
            PickPlanSection.Visibility = ToVisibility(_settings.PickBanOverlayShowPickPlan);
            BanPlanSection.Visibility = ToVisibility(_settings.PickBanOverlayShowBanPlan);
            ContentPanel.Margin = new Thickness(12, 12, 18, 18);
        }

        private void ApplyOverlaySize()
        {
            double scale = _settings.PickBanOverlayScalePercent / 100d;
            double scaledWidth = Math.Round(FullLayoutBaseWidth * scale);
            double targetWidth = ClampToRange(
                _settings.PickBanOverlayWidth ?? scaledWidth,
                MinWidth,
                GetMaxOverlayWidth());

            Width = Math.Round(targetWidth);

            double autoFitHeight = MeasureAutoFitOverlayHeight(Width);
            double maxHeight = GetEffectiveMaxOverlayHeight(autoFitHeight);
            double requestedHeight = _settings.PickBanOverlayHeight ?? autoFitHeight;

            MaxWidth = GetMaxOverlayWidth();
            MaxHeight = GetMaxOverlayHeight();
            Height = Math.Round(ClampToRange(requestedHeight, MinHeight, maxHeight));
            OverlayScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            RefreshOpenSettingsPopupPlacement();
        }

        private double MeasureAutoFitOverlayHeight(double targetWidth)
        {
            ScrollBarVisibility previousVisibility = OverlayScrollViewer.VerticalScrollBarVisibility;
            try
            {
                OverlayScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                OverlayPanel.Measure(new Size(Math.Max(MinWidth, targetWidth), double.PositiveInfinity));

                return double.IsFinite(OverlayPanel.DesiredSize.Height) && OverlayPanel.DesiredSize.Height > 0
                    ? Math.Ceiling(OverlayPanel.DesiredSize.Height)
                    : FullLayoutBaseHeight * (_settings.PickBanOverlayScalePercent / 100d);
            }
            finally
            {
                OverlayScrollViewer.VerticalScrollBarVisibility = previousVisibility;
            }
        }

        private double GetEffectiveMaxOverlayHeight(double autoFitHeight)
        {
            return Math.Max(MinHeight, Math.Min(GetMaxOverlayHeight(), autoFitHeight));
        }

        private double GetMaxOverlayWidth()
        {
            return Math.Max(MinWidth, SystemParameters.VirtualScreenWidth - (ScreenPadding * 2));
        }

        private double GetMaxOverlayHeight()
        {
            return Math.Max(MinHeight, SystemParameters.VirtualScreenHeight - (ScreenPadding * 2));
        }

        private void RefreshSliderValueText()
        {
            ScaleValueText.Text = $"{_settings.PickBanOverlayScalePercent}%";
            OpacityValueText.Text = $"{_settings.PickBanOverlayOpacityPercent}%";
        }

        private static Visibility ToVisibility(bool isVisible)
        {
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePlanDisplayForCurrentLayout()
        {
            RenderDashboardStatus(_lastDashboardStatus);
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
                PositionChangedByUser?.Invoke(Left, Top);
                RefreshOpenSettingsPopupPlacement();
            }
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            _isResizingOverlay = true;
            _resizeStartPoint = e.GetPosition(this);
            _resizeStartWidth = ActualWidth > 0 ? ActualWidth : Width;
            _resizeStartHeight = ActualHeight > 0 ? ActualHeight : Height;
            WindowResizeGrip.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizingOverlay)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishOverlayResize(saveSettings: true);
                return;
            }

            Point currentPoint = e.GetPosition(this);
            double targetWidth = ClampToRange(
                _resizeStartWidth + currentPoint.X - _resizeStartPoint.X,
                MinWidth,
                GetMaxOverlayWidth());

            Width = Math.Round(targetWidth);

            double autoFitHeight = MeasureAutoFitOverlayHeight(Width);
            double maxHeight = GetEffectiveMaxOverlayHeight(autoFitHeight);
            double targetHeight = ClampToRange(
                _resizeStartHeight + currentPoint.Y - _resizeStartPoint.Y,
                MinHeight,
                maxHeight);

            Height = Math.Round(targetHeight);
            OverlayScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            RefreshOpenSettingsPopupPlacement();
            e.Handled = true;
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizingOverlay)
                return;

            FinishOverlayResize(saveSettings: true);
            e.Handled = true;
        }

        private void ResizeGrip_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isResizingOverlay)
                FinishOverlayResize(saveSettings: true);
        }

        private void FinishOverlayResize(bool saveSettings)
        {
            _isResizingOverlay = false;

            if (WindowResizeGrip.IsMouseCaptured)
                WindowResizeGrip.ReleaseMouseCapture();

            if (!saveSettings)
                return;

            _settings.PickBanOverlayWidth = Math.Round(ActualWidth > 0 ? ActualWidth : Width);
            _settings.PickBanOverlayHeight = Math.Round(ActualHeight > 0 ? ActualHeight : Height);
            ClampToVirtualScreen();
            QueueOverlaySettingsPersist();
        }

        private void UpdateSettingsPopupPlacement()
        {
            double boundsLeft = SystemParameters.VirtualScreenLeft;
            double boundsRight = boundsLeft + SystemParameters.VirtualScreenWidth;
            double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
            double spaceRight = boundsRight - (Left + currentWidth);
            double spaceLeft = Left - boundsLeft;
            bool placeRight = spaceRight >= SettingsPopupWidth + SettingsPopupGap || spaceRight >= spaceLeft;

            SettingsPopup.Placement = placeRight
                ? PlacementMode.Right
                : PlacementMode.Left;
            SettingsPopup.HorizontalOffset = placeRight
                ? SettingsPopupGap
                : -SettingsPopupGap;
            SettingsPopup.VerticalOffset = 0;
        }

        private void RefreshOpenSettingsPopupPlacement()
        {
            if (!SettingsPopup.IsOpen)
                return;

            UpdateSettingsPopupPlacement();
        }

        private void ClampToVirtualScreen()
        {
            Rect bounds = new(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                Math.Max(1, SystemParameters.VirtualScreenWidth),
                Math.Max(1, SystemParameters.VirtualScreenHeight));

            double clampedLeft = ClampToRange(Left, bounds.Left + ScreenPadding, bounds.Right - Width - ScreenPadding);
            double clampedTop = ClampToRange(Top, bounds.Top + ScreenPadding, bounds.Bottom - Height - ScreenPadding);
            if (Math.Abs(clampedLeft - Left) <= 0.5 && Math.Abs(clampedTop - Top) <= 0.5)
                return;

            Left = clampedLeft;
            Top = clampedTop;
            PositionChangedByUser?.Invoke(Left, Top);
        }

        private static double ClampToRange(double value, double min, double max)
        {
            if (max < min)
                return min;

            return Math.Clamp(value, min, max);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _overlaySettingsSaveTimer.Stop();
            _draftCountdownTimer.Stop();
            _settings.Saved -= Settings_Saved;
            ChampionCatalog.CatalogChanged -= ChampionCatalog_CatalogChanged;
            ChampionImageSelectionStore.SelectionsChanged -= ChampionImageSelectionStore_SelectionsChanged;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
        }

    }
}
