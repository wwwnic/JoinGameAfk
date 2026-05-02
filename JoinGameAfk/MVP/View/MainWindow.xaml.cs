using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using JoinGameAfk.Enums;
using JoinGameAfk.MVP.Controller;
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
        private PhaseController? _phaseController;
        private int _activeTabIndex;
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;

        public int ActiveTabIndex => _activeTabIndex;

        public MainWindow(PhaseProgressionPage dashboardPage, ChampionPrioritiesPage championPrioritiesPage, SettingsPage settingsPage)
        {
            InitializeComponent();

            DashboardFrame.Content = dashboardPage;
            ChampionPrioritiesFrame.Content = championPrioritiesPage;
            SettingsFrame.Content = settingsPage;

            _tabs = [TabDashboard, TabChampionPriorities, TabSettings];
            _frames = [DashboardFrame, ChampionPrioritiesFrame, SettingsFrame];

            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += (_, _) => UpdateMaximizeRestoreButton();
            Closed += (_, _) => AppThemeManager.ThemeChanged -= RefreshTheme;
            AppThemeManager.ThemeChanged += RefreshTheme;
            ActivateTab(0);
            SetWatcherState(false);
            SetClientConnection(false);
            UpdatePhaseIndicator(ClientPhase.Unknown);
            UpdateMaximizeRestoreButton();
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WindowProc);
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
            Dispatcher.Invoke(() =>
            {
                _isWatcherRunning = isRunning;
                RefreshWatcherButton();
                RefreshPhaseText();
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                _isClientConnected = isConnected;
                RefreshPhaseText();
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
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
                _frames[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
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
            Dispatcher.Invoke(() =>
            {
                _currentPhase = phase;
                RefreshPhaseIndicator();
                RefreshPhaseText();
            });
        }

        private void RefreshTheme()
        {
            Dispatcher.Invoke(() =>
            {
                ActivateTab(_activeTabIndex);
                RefreshPhaseIndicator();
                RefreshWatcherButton();
                RefreshPhaseText();
            });
        }

        private void RefreshPhaseIndicator()
        {
            Brush phaseBrush = _currentPhase switch
            {
                ClientPhase.Lobby or ClientPhase.Matchmaking => ResourceBrush("PhaseLobbyBrush", Brushes.DodgerBlue),
                ClientPhase.ReadyCheck => ResourceBrush("PhaseReadyCheckBrush", Brushes.ForestGreen),
                ClientPhase.Planning => ResourceBrush("PhaseHoverBrush", Brushes.DarkOrange),
                ClientPhase.ChampSelect => ResourceBrush("PhaseBanBrush", Brushes.Firebrick),
                _ => ResourceBrush("PhaseDefaultBrush", Brushes.White)
            };

            TitlePhaseIndicator.Background = phaseBrush;
            GlobalPhaseIndicator.Background = phaseBrush;
        }

        private void RefreshWatcherButton()
        {
            GlobalWatcherButton.Content = _isWatcherRunning ? "Stop watcher" : "Start watcher";
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
