using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Presentation.Controller;
using JoinGameAfk.Services;
using JoinGameAfk.Theme;

namespace JoinGameAfk.View
{
    public partial class MainWindow : Window
    {
        private const int MonitorDefaultToNearest = 0x00000002;
        private const int WmGetMinMaxInfoMessage = 0x0024;

        private static readonly Geometry MaximizeWindowIcon = Geometry.Parse("M6,6 H18 V18 H6 Z");
        private static readonly Geometry RestoreWindowIcon = Geometry.Parse("M8,6 H18 V16 H16 V8 H8 Z M6,10 H14 V18 H6 Z");

        private readonly Button[] _tabs;
        private readonly Frame[] _frames;
        private readonly PhaseProgressionPage _dashboardPage;
        private readonly LogsPage _logsPage;
        private readonly ChampionPrioritiesPage _championPrioritiesPage;
        private readonly ChampSelectSettings _settings;
        private readonly OverlaySettings _overlaySettings;
        private PhaseController? _phaseController;
#if DEBUG
        private PhaseProgressionTestWindow? _phaseProgressionTestWindow;
#endif
        private PickBanOverlayWindow? _pickBanOverlayWindow;
        private QueueMicroOverlayWindow? _queueMicroOverlayWindow;
        private DashboardStatus _lastDashboardStatus = new();
        private int _activeTabIndex;
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private string _champSelectSubPhase = string.Empty;
        private string _readyCheckResponse = string.Empty;
        private bool _isPickBanOverlayAutoOpened;
        private bool _isClosingAutoPickBanOverlay;
        private bool _isPickBanOverlayVisibleDuringChampSelect;
        private bool _suppressAutoPickBanOverlayForCurrentChampSelect;
        private bool _keepInactiveFramesMeasured;
        private bool _inactiveFrameWarmupQueued;

        public int ActiveTabIndex => _activeTabIndex;

        public MainWindow(PhaseProgressionPage dashboardPage, LogsPage logsPage, ChampionPrioritiesPage championPrioritiesPage, SettingsPage settingsPage, ChampSelectSettings settings, OverlaySettings overlaySettings)
        {
            InitializeComponent();
            SetApplicationVersion();

            _dashboardPage = dashboardPage;
            _logsPage = logsPage;
            _championPrioritiesPage = championPrioritiesPage;
            _settings = settings;
            _overlaySettings = overlaySettings;
            DashboardFrame.Content = dashboardPage;
            ChampionPrioritiesFrame.Content = championPrioritiesPage;
            SettingsFrame.Content = settingsPage;

            _tabs = [TabDashboard, TabChampionPriorities, TabSettings];
            _frames = [DashboardFrame, ChampionPrioritiesFrame, SettingsFrame];

            SourceInitialized += MainWindow_SourceInitialized;
            ContentRendered += MainWindow_ContentRendered;
            StateChanged += (_, _) => UpdateMaximizeRestoreButton();
            Closed += MainWindow_Closed;
            _dashboardPage.DashboardStatusChanged += UpdateDashboardStatus;
            _settings.Saved += Settings_Saved;
            _overlaySettings.Saved += OverlaySettings_Saved;
            AppThemeManager.ThemeChanged += RefreshTheme;
            ActivateTab(0);
            SetWatcherState(false);
            SetClientConnection(false);
            UpdatePhaseIndicator(ClientPhase.Unknown);
            UpdateMaximizeRestoreButton();
        }

        private void SetApplicationVersion()
        {
            string version = GetDisplayVersion();
            AppVersionText.Text = version;
            Title = $"JoinGameAfk {version}";
            AutomationProperties.SetName(AppVersionText, $"Application version {version}");
        }

        private static string GetDisplayVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string? informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informationalVersion)
                && assembly.GetName().Version is Version assemblyVersion)
            {
                informationalVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
            }

            return FormatDisplayVersion(informationalVersion);
        }

        private static string FormatDisplayVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "v0.0.0";

            version = version.Trim();
            int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
            if (metadataIndex > 0)
                version = version[..metadataIndex];

            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                return version;

            return char.IsDigit(version[0])
                ? $"v{version}"
                : version;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WindowProc);
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            if (_inactiveFrameWarmupQueued)
                return;

            _inactiveFrameWarmupQueued = true;
            Dispatcher.InvokeAsync(WarmInactiveTabFrames, DispatcherPriority.ApplicationIdle);
            _ = WarmChampionImageCacheAsync();
        }

        private void WarmInactiveTabFrames()
        {
            if (!IsLoaded || !IsVisible)
                return;

            _keepInactiveFramesMeasured = true;
            ActivateTab(_activeTabIndex);
        }

        private static async Task WarmChampionImageCacheAsync()
        {
            try
            {
                await ChampionTileCatalog.PreloadSelectedImageSourcesAsync(ChampionCatalog.All).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _phaseController?.Dispose();
            _phaseController = null;
            ContentRendered -= MainWindow_ContentRendered;
            AppThemeManager.ThemeChanged -= RefreshTheme;
            _settings.Saved -= Settings_Saved;
            _overlaySettings.Saved -= OverlaySettings_Saved;
            _dashboardPage.DashboardStatusChanged -= UpdateDashboardStatus;
            _pickBanOverlayWindow?.Close();
            _queueMicroOverlayWindow?.Close();
        }

        private void TabDashboard_Click(object sender, RoutedEventArgs e) => ActivateTab(0);
        private void TabChampionPriorities_Click(object sender, RoutedEventArgs e) => ActivateTab(1);
        private void TabSettings_Click(object sender, RoutedEventArgs e) => ActivateTab(2);

        public void SetController(PhaseController controller)
        {
            _phaseController = controller;
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatcher.TryInvoke(() =>
            {
                _isWatcherRunning = isRunning;
#if DEBUG
                if (_isWatcherRunning)
                    ClosePhaseProgressionTestWindow();
#endif

                RefreshWatcherButton();
                RefreshPhaseIndicator();
                RefreshPhaseText();
                _pickBanOverlayWindow?.SetWatcherState(_isWatcherRunning);
                SynchronizeAutoPickBanOverlay();
                _queueMicroOverlayWindow?.SetWatcherState(_isWatcherRunning);
                SynchronizeQueueMicroOverlay();
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatcher.TryInvoke(() =>
            {
                _isClientConnected = isConnected;
                RefreshPhaseIndicator();
                RefreshPhaseText();
                _pickBanOverlayWindow?.SetClientConnection(_isClientConnected);
                SynchronizeAutoPickBanOverlay();
                _queueMicroOverlayWindow?.SetClientConnection(_isClientConnected);
                SynchronizeQueueMicroOverlay();
            });
        }

        public void UpdateChampSelectSubPhase(string subPhase)
        {
            Dispatcher.TryInvoke(() =>
            {
                _champSelectSubPhase = subPhase;
                RefreshPhaseIndicator();
                _pickBanOverlayWindow?.UpdateChampSelectSubPhase(_champSelectSubPhase);
            });
        }

        private void GlobalWatcherButton_Click(object sender, RoutedEventArgs e)
        {
            if (_phaseController is null)
                return;

            if (_phaseController.IsRunning)
                _phaseController.Stop();
            else
                _phaseController.Start();
        }

        private void PickBanOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pickBanOverlayWindow?.IsVisible == true
                && _pickBanOverlayWindow.WindowState != WindowState.Minimized)
            {
                _pickBanOverlayWindow.WindowState = WindowState.Minimized;
                return;
            }

            ShowPickBanOverlay(autoOpened: false);
        }

        public void ShowPickBanOverlayOnStartup()
        {
            Dispatcher.TryInvoke(() => ShowPickBanOverlay(autoOpened: true, autoOpenReason: "on startup"));
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
#if DEBUG
            if (e.Key == Key.F12 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                OpenPhaseProgressionTestWindow();
                e.Handled = true;
                return;
            }
#endif

            if (e.Key == Key.Enter && Keyboard.FocusedElement is ButtonBase button && button.IsEnabled)
            {
                ActivateButtonFromKeyboard(button);
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return;

            int? tabIndex = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                _ => null
            };

            if (tabIndex is not int index)
                return;

            ActivateTab(index);
            _tabs[index].Focus();
            e.Handled = true;
        }

#if DEBUG
        private void OpenPhaseProgressionTestWindow()
        {
            if (_isWatcherRunning)
            {
                MessageBox.Show(this, "Stop the watcher before opening the phase tester.", "Watcher Running", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_phaseProgressionTestWindow is not null)
            {
                if (_phaseProgressionTestWindow.WindowState == WindowState.Minimized)
                    _phaseProgressionTestWindow.WindowState = WindowState.Normal;

                _phaseProgressionTestWindow.Activate();
                return;
            }

            _phaseProgressionTestWindow = new PhaseProgressionTestWindow(_dashboardPage, _logsPage, () => !_isWatcherRunning)
            {
                Owner = this
            };
            _phaseProgressionTestWindow.Closed += (_, _) => _phaseProgressionTestWindow = null;
            _phaseProgressionTestWindow.Show();
        }

        private void ClosePhaseProgressionTestWindow()
        {
            _phaseProgressionTestWindow?.Close();
        }
#endif

        private static void ActivateButtonFromKeyboard(ButtonBase button)
        {
            if (button is ToggleButton toggleButton)
            {
                toggleButton.IsChecked = toggleButton.IsThreeState
                    ? toggleButton.IsChecked switch
                    {
                        true => false,
                        false => null,
                        _ => true
                    }
                    : toggleButton.IsChecked != true;
            }

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        }

        public void ActivateTab(int index)
        {
            if (index < 0 || index >= _tabs.Length)
                index = 0;

            _activeTabIndex = index;
            Brush activeTabFg = ResourceBrush("TabActiveForegroundBrush", Brushes.White);
            Brush inactiveTabFg = ResourceBrush("TabInactiveForegroundBrush", Brushes.Gray);
            Brush activeTabBorder = ResourceBrush("TabActiveBorderBrush", Brushes.DodgerBlue);
            Brush inactiveTabBorder = ResourceBrush("TabInactiveBorderBrush", Brushes.Transparent);

            for (int i = 0; i < _tabs.Length; i++)
            {
                bool isActive = i == index;
                _tabs[i].Tag = isActive ? "Active" : null;
                _tabs[i].Background = Brushes.Transparent;
                _tabs[i].Foreground = isActive ? activeTabFg : inactiveTabFg;
                _tabs[i].BorderBrush = isActive ? activeTabBorder : inactiveTabBorder;
                _frames[i].Visibility = isActive
                    ? Visibility.Visible
                    : _keepInactiveFramesMeasured
                        ? Visibility.Hidden
                        : Visibility.Collapsed;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsInsideTitleBarButton(e.OriginalSource as DependencyObject))
                return;

            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private static bool IsInsideTitleBarButton(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is Button)
                    return true;

                current = current is FrameworkElement frameworkElement && frameworkElement.Parent != null
                    ? frameworkElement.Parent
                    : VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void UpdateMaximizeRestoreButton()
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            MaximizeRestoreIcon.Data = isMaximized ? RestoreWindowIcon : MaximizeWindowIcon;
            MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmGetMinMaxInfoMessage)
            {
                ApplyMinMaxInfo(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void ApplyMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);

                RECT workArea = monitorInfo.rcWork;
                RECT monitorArea = monitorInfo.rcMonitor;

                minMaxInfo.ptMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
                minMaxInfo.ptMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
                minMaxInfo.ptMaxSize.X = Math.Abs(workArea.Right - workArea.Left);
                minMaxInfo.ptMaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);
            }

            ApplyMinimumTrackSize(ref minMaxInfo);
            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        private void ApplyMinimumTrackSize(ref MINMAXINFO minMaxInfo)
        {
            double scaleX = 1;
            double scaleY = 1;

            if (PresentationSource.FromVisual(this) is HwndSource source
                && source.CompositionTarget is not null)
            {
                Matrix transform = source.CompositionTarget.TransformToDevice;
                scaleX = transform.M11;
                scaleY = transform.M22;
            }

            minMaxInfo.ptMinTrackSize.X = Math.Max(
                minMaxInfo.ptMinTrackSize.X,
                (int)Math.Ceiling(MinWidth * scaleX));
            minMaxInfo.ptMinTrackSize.Y = Math.Max(
                minMaxInfo.ptMinTrackSize.Y,
                (int)Math.Ceiling(MinHeight * scaleY));
        }

        public void UpdatePhaseIndicator(ClientPhase phase)
        {
            Dispatcher.TryInvoke(() =>
            {
                bool wasChampSelectFlow = IsChampSelectFlow(_currentPhase);
                bool isChampSelectFlow = IsChampSelectFlow(phase);
                if (wasChampSelectFlow != isChampSelectFlow)
                    _suppressAutoPickBanOverlayForCurrentChampSelect = false;

                _currentPhase = phase;
                _championPrioritiesPage.SetChampionSelectActive(isChampSelectFlow);
                RefreshPhaseIndicator();
                RefreshPhaseText();
                _pickBanOverlayWindow?.UpdatePhase(_currentPhase);
                SynchronizeAutoPickBanOverlay();
                _queueMicroOverlayWindow?.UpdatePhase(_currentPhase);
                SynchronizeQueueMicroOverlay();
            });
        }

        private void UpdateDashboardStatus(DashboardStatus status)
        {
            Dispatcher.TryInvoke(() =>
            {
                _lastDashboardStatus = status;
                _readyCheckResponse = status.ReadyCheckResponse;
                RefreshPhaseIndicator();
                RefreshPhaseText();
                _pickBanOverlayWindow?.UpdateDashboardStatus(status);
                _queueMicroOverlayWindow?.UpdateDashboardStatus(status);
                SynchronizeAutoPickBanOverlay();
            });
        }

        private void RefreshTheme()
        {
            Dispatcher.TryInvoke(() =>
            {
                ActivateTab(_activeTabIndex);
                RefreshPhaseIndicator();
                RefreshWatcherButton();
                RefreshPhaseText();
            });
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(SynchronizeAutoPickBanOverlay);
        }

        private void OverlaySettings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                SynchronizeAutoPickBanOverlay();
                _queueMicroOverlayWindow?.ApplySettings();
                SynchronizeQueueMicroOverlay();
            });
        }

        private void RefreshPhaseIndicator()
        {
            TitlePhaseIndicator.Update(
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

        private void RefreshWatcherButton()
        {
            GlobalWatcherButton.Content = _isWatcherRunning ? "Stop watcher" : "Start watcher";
            GlobalWatcherButton.Tag = _isWatcherRunning ? "Running" : null;
            GlobalWatcherButton.ToolTip = _isWatcherRunning ? "Stop watcher" : "Start watcher";
            AutomationProperties.SetName(GlobalWatcherButton, _isWatcherRunning ? "Stop watcher" : "Start watcher");
            GlobalWatcherButton.Background = _isWatcherRunning
                ? ResourceBrush("WatcherStopBrush", Brushes.Firebrick)
                : ResourceBrush("WatcherStartBrush", Brushes.ForestGreen);
        }

        private void RefreshPhaseText()
        {
            GlobalPhaseText.Text = GetStatusLine(_currentPhase);
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

        private void ShowPickBanOverlay(bool autoOpened, string autoOpenReason = "for champion select")
        {
            bool shouldLogAutoOpen = autoOpened
                && (_pickBanOverlayWindow is null || !_pickBanOverlayWindow.IsVisible);

            if (_pickBanOverlayWindow is null)
            {
                _pickBanOverlayWindow = new PickBanOverlayWindow(_overlaySettings);
                _pickBanOverlayWindow.ToggleMainAppRequested += ToggleMainWindowFromOverlay;
                _pickBanOverlayWindow.PositionChangedByUser += PickBanOverlayWindow_PositionChangedByUser;
                _pickBanOverlayWindow.Closed += (_, _) =>
                {
                    bool wasAutoOpened = _isPickBanOverlayAutoOpened;
                    _pickBanOverlayWindow = null;
                    _isPickBanOverlayAutoOpened = false;
                    _isPickBanOverlayVisibleDuringChampSelect = false;

                    if (wasAutoOpened
                        && !_isClosingAutoPickBanOverlay
                        && IsChampSelectFlow(_currentPhase))
                    {
                        _suppressAutoPickBanOverlayForCurrentChampSelect = true;
                        LogAutoOverlay("Pick/ban overlay auto-open suppressed for this champion select because the overlay was closed manually.");
                    }
                };
                PositionPickBanOverlay(_pickBanOverlayWindow);
            }

            if (!autoOpened)
                _suppressAutoPickBanOverlayForCurrentChampSelect = false;

            if (autoOpened || _pickBanOverlayWindow.IsVisible)
                _isPickBanOverlayAutoOpened = autoOpened;

            RefreshPickBanOverlayState(_pickBanOverlayWindow);

            if (!_pickBanOverlayWindow.IsVisible)
                _pickBanOverlayWindow.Show();

            if (_pickBanOverlayWindow.WindowState == WindowState.Minimized)
                _pickBanOverlayWindow.WindowState = WindowState.Normal;

            TrackPickBanOverlayVisibleDuringChampSelect();

            if (shouldLogAutoOpen)
                LogAutoOverlay($"Pick/ban overlay auto-opened {autoOpenReason}.");

            _pickBanOverlayWindow.Activate();
        }

        private void ToggleMainWindowFromOverlay()
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            if (!IsVisible)
                Show();

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
            Focus();
        }

        private void SynchronizeQueueMicroOverlay()
        {
            if (ShouldAutoShowQueueMicroOverlay())
            {
                if (_queueMicroOverlayWindow is null || !_queueMicroOverlayWindow.IsVisible)
                    ShowQueueMicroOverlay();
                else
                    RefreshQueueMicroOverlayState(_queueMicroOverlayWindow);

                return;
            }

            CloseQueueMicroOverlay();
        }

        private bool ShouldAutoShowQueueMicroOverlay()
        {
            return _overlaySettings.QueueMicroOverlayEnabled
                && _isWatcherRunning
                && _isClientConnected
                && IsQueueMicroOverlayPhase(_currentPhase);
        }

        private void ShowQueueMicroOverlay()
        {
            if (_queueMicroOverlayWindow is null)
            {
                _queueMicroOverlayWindow = new QueueMicroOverlayWindow(_overlaySettings);
                _queueMicroOverlayWindow.PositionChangedByUser += QueueMicroOverlayWindow_PositionChangedByUser;
                _queueMicroOverlayWindow.Closed += (_, _) => _queueMicroOverlayWindow = null;
                PositionQueueMicroOverlay(_queueMicroOverlayWindow);
            }

            RefreshQueueMicroOverlayState(_queueMicroOverlayWindow);

            if (!_queueMicroOverlayWindow.IsVisible)
                _queueMicroOverlayWindow.Show();
        }

        private void CloseQueueMicroOverlay()
        {
            if (_queueMicroOverlayWindow?.IsVisible != true)
                return;

            _queueMicroOverlayWindow.Close();
            _queueMicroOverlayWindow = null;
        }

        private void SynchronizeAutoPickBanOverlay()
        {
            TrackPickBanOverlayVisibleDuringChampSelect();

            if (ShouldAutoShowPickBanOverlay())
            {
                if (_pickBanOverlayWindow is null || !_pickBanOverlayWindow.IsVisible)
                    ShowPickBanOverlay(autoOpened: true);
                else
                    _isPickBanOverlayVisibleDuringChampSelect = true;

                return;
            }

            if (ShouldAutoClosePickBanOverlay())
                CloseAutoPickBanOverlay();
        }

        private bool ShouldAutoShowPickBanOverlay()
        {
            return _overlaySettings.AutoShowPickBanOverlayEnabled
                && _isWatcherRunning
                && _isClientConnected
                && !_lastDashboardStatus.IsUnsupportedMode
                && !_suppressAutoPickBanOverlayForCurrentChampSelect
                && IsChampSelectFlow(_currentPhase);
        }

        private bool ShouldAutoClosePickBanOverlay()
        {
            return _overlaySettings.PickBanOverlayAutoCloseAfterChampSelectEnabled
                && _isPickBanOverlayVisibleDuringChampSelect
                && _pickBanOverlayWindow?.IsVisible == true
                && (!_isWatcherRunning || !_isClientConnected || !IsChampSelectFlow(_currentPhase) || _lastDashboardStatus.IsUnsupportedMode);
        }

        private void TrackPickBanOverlayVisibleDuringChampSelect()
        {
            if (_pickBanOverlayWindow?.IsVisible == true && IsChampSelectFlow(_currentPhase))
                _isPickBanOverlayVisibleDuringChampSelect = true;
        }

        private void CloseAutoPickBanOverlay()
        {
            if (_pickBanOverlayWindow?.IsVisible != true)
                return;

            string closeReason = GetAutoPickBanOverlayCloseReason();
            _isClosingAutoPickBanOverlay = true;
            try
            {
                _pickBanOverlayWindow?.Close();
                _pickBanOverlayWindow = null;
                _isPickBanOverlayAutoOpened = false;
                _isPickBanOverlayVisibleDuringChampSelect = false;
                LogAutoOverlay($"Pick/ban overlay auto-closed ({closeReason}).");
            }
            finally
            {
                _isClosingAutoPickBanOverlay = false;
            }
        }

        private string GetAutoPickBanOverlayCloseReason()
        {
            if (!_isWatcherRunning)
                return "watcher stopped";

            if (!_isClientConnected)
                return "League Client disconnected";

            if (!IsChampSelectFlow(_currentPhase))
                return "champion select ended";

            if (_lastDashboardStatus.IsUnsupportedMode)
                return "unsupported mode";

            if (_suppressAutoPickBanOverlayForCurrentChampSelect)
                return "manual suppression active";

            return "conditions no longer match";
        }

        private void LogAutoOverlay(string message)
        {
            _logsPage.WriteLine(message);
        }

        private static bool IsChampSelectFlow(ClientPhase phase)
        {
            return phase is ClientPhase.ChampSelect or ClientPhase.Planning;
        }

        private static bool IsQueueMicroOverlayPhase(ClientPhase phase)
        {
            return phase is ClientPhase.Matchmaking or ClientPhase.ReadyCheck;
        }

        private void RefreshQueueMicroOverlayState(QueueMicroOverlayWindow overlayWindow)
        {
            overlayWindow.ApplySettings();
            if (double.IsFinite(overlayWindow.Left) && double.IsFinite(overlayWindow.Top))
                ApplyQueueMicroOverlayPosition(overlayWindow, overlayWindow.Left, overlayWindow.Top, GetVirtualScreenBounds());
            overlayWindow.SetWatcherState(_isWatcherRunning);
            overlayWindow.SetClientConnection(_isClientConnected);
            overlayWindow.UpdatePhase(_currentPhase);
            overlayWindow.UpdateDashboardStatus(_lastDashboardStatus);
        }

        private void RefreshPickBanOverlayState(PickBanOverlayWindow overlayWindow)
        {
            overlayWindow.SetWatcherState(_isWatcherRunning);
            overlayWindow.SetClientConnection(_isClientConnected);
            overlayWindow.UpdatePhase(_currentPhase);
            overlayWindow.UpdateChampSelectSubPhase(_champSelectSubPhase);
            overlayWindow.UpdateDashboardStatus(_lastDashboardStatus);
        }

        private void PositionPickBanOverlay(PickBanOverlayWindow overlayWindow)
        {
            Rect virtualScreenBounds = GetVirtualScreenBounds();
            if (TryGetSavedPickBanOverlayPosition(out double savedLeft, out double savedTop))
            {
                ApplyOverlayPosition(overlayWindow, savedLeft, savedTop, virtualScreenBounds);
                return;
            }

            Rect anchorBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height)
                : RestoreBounds;

            Rect workArea = SystemParameters.WorkArea;
            double targetLeft = anchorBounds.Right - overlayWindow.Width - 18;
            double targetTop = anchorBounds.Top + 76;

            ApplyOverlayPosition(overlayWindow, targetLeft, targetTop, workArea);
        }

        private void PositionQueueMicroOverlay(QueueMicroOverlayWindow overlayWindow)
        {
            Rect virtualScreenBounds = GetVirtualScreenBounds();
            if (TryGetSavedQueueMicroOverlayPosition(out double savedLeft, out double savedTop))
            {
                ApplyQueueMicroOverlayPosition(overlayWindow, savedLeft, savedTop, virtualScreenBounds);
                return;
            }

            Rect anchorBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height)
                : RestoreBounds;

            Rect workArea = SystemParameters.WorkArea;
            double targetLeft = anchorBounds.Right - GetQueueMicroOverlayWidth(overlayWindow) - 18;
            double targetTop = anchorBounds.Top + 76;

            ApplyQueueMicroOverlayPosition(overlayWindow, targetLeft, targetTop, workArea);
        }

        private void PickBanOverlayWindow_PositionChangedByUser(double left, double top)
        {
            if (!double.IsFinite(left) || !double.IsFinite(top))
                return;

            Rect bounds = GetVirtualScreenBounds();
            _overlaySettings.PickBanOverlayLeft = ClampToRange(left, bounds.Left + 8, bounds.Right - GetOverlayWidth(_pickBanOverlayWindow) - 8);
            _overlaySettings.PickBanOverlayTop = ClampToRange(top, bounds.Top + 8, bounds.Bottom - GetOverlayHeight(_pickBanOverlayWindow) - 8);
            _overlaySettings.Save();
        }

        private void QueueMicroOverlayWindow_PositionChangedByUser(double left, double top)
        {
            if (!double.IsFinite(left) || !double.IsFinite(top))
                return;

            Rect bounds = GetVirtualScreenBounds();
            _overlaySettings.QueueMicroOverlayLeft = ClampToRange(left, bounds.Left + 8, bounds.Right - GetQueueMicroOverlayWidth(_queueMicroOverlayWindow) - 8);
            _overlaySettings.QueueMicroOverlayTop = ClampToRange(top, bounds.Top + 8, bounds.Bottom - GetQueueMicroOverlayHeight(_queueMicroOverlayWindow) - 8);
            _overlaySettings.NormalizeOptions();
            _overlaySettings.Save();
        }

        private bool TryGetSavedPickBanOverlayPosition(out double left, out double top)
        {
            left = 0;
            top = 0;

            if (_overlaySettings.PickBanOverlayLeft is not double savedLeft
                || _overlaySettings.PickBanOverlayTop is not double savedTop
                || !double.IsFinite(savedLeft)
                || !double.IsFinite(savedTop))
            {
                return false;
            }

            left = savedLeft;
            top = savedTop;
            return true;
        }

        private bool TryGetSavedQueueMicroOverlayPosition(out double left, out double top)
        {
            left = 0;
            top = 0;

            if (_overlaySettings.QueueMicroOverlayLeft is not double savedLeft
                || _overlaySettings.QueueMicroOverlayTop is not double savedTop
                || !double.IsFinite(savedLeft)
                || !double.IsFinite(savedTop))
            {
                return false;
            }

            left = savedLeft;
            top = savedTop;
            return true;
        }

        private static void ApplyOverlayPosition(PickBanOverlayWindow overlayWindow, double left, double top, Rect bounds)
        {
            overlayWindow.Left = ClampToRange(left, bounds.Left + 8, bounds.Right - GetOverlayWidth(overlayWindow) - 8);
            overlayWindow.Top = ClampToRange(top, bounds.Top + 8, bounds.Bottom - GetOverlayHeight(overlayWindow) - 8);
        }

        private static void ApplyQueueMicroOverlayPosition(QueueMicroOverlayWindow overlayWindow, double left, double top, Rect bounds)
        {
            overlayWindow.Left = ClampToRange(left, bounds.Left + 8, bounds.Right - GetQueueMicroOverlayWidth(overlayWindow) - 8);
            overlayWindow.Top = ClampToRange(top, bounds.Top + 8, bounds.Bottom - GetQueueMicroOverlayHeight(overlayWindow) - 8);
        }

        private static Rect GetVirtualScreenBounds()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                Math.Max(1, SystemParameters.VirtualScreenWidth),
                Math.Max(1, SystemParameters.VirtualScreenHeight));
        }

        private static double GetOverlayWidth(PickBanOverlayWindow? overlayWindow)
        {
            if (overlayWindow is null)
                return 360;

            return overlayWindow.ActualWidth > 0 ? overlayWindow.ActualWidth : overlayWindow.Width;
        }

        private static double GetOverlayHeight(PickBanOverlayWindow? overlayWindow)
        {
            if (overlayWindow is null)
                return 420;

            return overlayWindow.ActualHeight > 0 ? overlayWindow.ActualHeight : overlayWindow.Height;
        }

        private static double GetQueueMicroOverlayWidth(QueueMicroOverlayWindow? overlayWindow)
        {
            if (overlayWindow is null)
                return 56;

            return overlayWindow.ActualWidth > 0 ? overlayWindow.ActualWidth : overlayWindow.Width;
        }

        private static double GetQueueMicroOverlayHeight(QueueMicroOverlayWindow? overlayWindow)
        {
            if (overlayWindow is null)
                return 56;

            return overlayWindow.ActualHeight > 0 ? overlayWindow.ActualHeight : overlayWindow.Height;
        }

        private static double ClampToRange(double value, double min, double max)
        {
            if (max < min)
                return min;

            return Math.Clamp(value, min, max);
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf<MONITORINFO>();
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
