using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using JoinGameAfk.Enums;
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
        private int _activeTabIndex;
        private ClientPhase _currentPhase = ClientPhase.Unknown;

        public int ActiveTabIndex => _activeTabIndex;

        public MainWindow(PhaseProgressionPage dashboardPage, ChampSelectSettingsPage champSelectPage, SettingsPage settingsPage)
        {
            InitializeComponent();

            DashboardFrame.Content = dashboardPage;
            ChampSelectFrame.Content = champSelectPage;
            SettingsFrame.Content = settingsPage;

            _tabs = [TabDashboard, TabChampSelect, TabSettings];
            _frames = [DashboardFrame, ChampSelectFrame, SettingsFrame];

            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += (_, _) => UpdateMaximizeRestoreButton();
            Closed += (_, _) => AppThemeManager.ThemeChanged -= RefreshTheme;
            AppThemeManager.ThemeChanged += RefreshTheme;
            ActivateTab(0);
            UpdateMaximizeRestoreButton();
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WindowProc);
        }

        private void TabDashboard_Click(object sender, RoutedEventArgs e) => ActivateTab(0);
        private void TabChampSelect_Click(object sender, RoutedEventArgs e) => ActivateTab(1);
        private void TabSettings_Click(object sender, RoutedEventArgs e) => ActivateTab(2);

        public void ActivateTab(int index)
        {
            if (index < 0 || index >= _tabs.Length)
                index = 0;

            _activeTabIndex = index;
            Brush activeTabBg = ResourceBrush("TabActiveBackgroundBrush", Brushes.SlateGray);
            Brush inactiveTabBg = ResourceBrush("TabInactiveBackgroundBrush", Brushes.Transparent);
            Brush activeTabFg = ResourceBrush("TabActiveForegroundBrush", Brushes.White);
            Brush inactiveTabFg = ResourceBrush("TabInactiveForegroundBrush", Brushes.Gray);
            Brush activeTabBorder = ResourceBrush("TabActiveBorderBrush", Brushes.DodgerBlue);
            Brush inactiveTabBorder = ResourceBrush("TabInactiveBorderBrush", Brushes.Transparent);

            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Background = i == index ? activeTabBg : inactiveTabBg;
                _tabs[i].Foreground = i == index ? activeTabFg : inactiveTabFg;
                _tabs[i].BorderBrush = i == index ? activeTabBorder : inactiveTabBorder;
                _frames[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
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
                ApplyMaximizedBounds(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static void ApplyMaximizedBounds(IntPtr hwnd, IntPtr lParam)
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

            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        public void UpdatePhaseIndicator(ClientPhase phase)
        {
            Dispatcher.Invoke(() =>
            {
                _currentPhase = phase;
                TitlePhaseIndicator.Background = phase switch
                {
                    ClientPhase.Lobby or ClientPhase.Matchmaking => ResourceBrush("PhaseLobbyBrush", Brushes.DodgerBlue),
                    ClientPhase.ReadyCheck => ResourceBrush("PhaseReadyCheckBrush", Brushes.ForestGreen),
                    ClientPhase.Planning => ResourceBrush("PhaseHoverBrush", Brushes.DarkOrange),
                    ClientPhase.ChampSelect => ResourceBrush("PhaseBanBrush", Brushes.Firebrick),
                    _ => ResourceBrush("PhaseDefaultBrush", Brushes.White)
                };
            });
        }

        private void RefreshTheme()
        {
            Dispatcher.Invoke(() =>
            {
                ActivateTab(_activeTabIndex);
                UpdatePhaseIndicator(_currentPhase);
            });
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
