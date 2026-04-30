using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using JoinGameAfk.Enums;

namespace JoinGameAfk.View
{
    public partial class MainWindow : Window
    {
        private const int MonitorDefaultToNearest = 0x00000002;
        private const int WmGetMinMaxInfoMessage = 0x0024;

        private static readonly SolidColorBrush ActiveTabBg = new((Color)ColorConverter.ConvertFromString("#1E293B"));
        private static readonly SolidColorBrush InactiveTabBg = new((Color)ColorConverter.ConvertFromString("#0F172A"));
        private static readonly SolidColorBrush ActiveTabFg = new((Color)ColorConverter.ConvertFromString("#E2E8F0"));
        private static readonly SolidColorBrush InactiveTabFg = new((Color)ColorConverter.ConvertFromString("#94A3B8"));
        private static readonly SolidColorBrush ActiveTabBorder = new((Color)ColorConverter.ConvertFromString("#60A5FA"));
        private static readonly SolidColorBrush InactiveTabBorder = new((Color)ColorConverter.ConvertFromString("#1E293B"));
        private static readonly SolidColorBrush LobbyPhaseBrush = new((Color)ColorConverter.ConvertFromString("#3B82F6"));
        private static readonly SolidColorBrush ReadyCheckPhaseBrush = new((Color)ColorConverter.ConvertFromString("#2E7D32"));
        private static readonly SolidColorBrush HoverPhaseBrush = new((Color)ColorConverter.ConvertFromString("#F59E0B"));
        private static readonly SolidColorBrush BanPhaseBrush = new((Color)ColorConverter.ConvertFromString("#DC2626"));
        private static readonly SolidColorBrush DefaultPhaseBrush = new((Color)ColorConverter.ConvertFromString("#FFFFFF"));
        private static readonly Geometry MaximizeWindowIcon = Geometry.Parse("M6,6 H18 V18 H6 Z");
        private static readonly Geometry RestoreWindowIcon = Geometry.Parse("M8,6 H18 V16 H16 V8 H8 Z M6,10 H14 V18 H6 Z");

        private readonly Button[] _tabs;
        private readonly Frame[] _frames;

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

        private void ActivateTab(int index)
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].Background = i == index ? ActiveTabBg : InactiveTabBg;
                _tabs[i].Foreground = i == index ? ActiveTabFg : InactiveTabFg;
                _tabs[i].BorderBrush = i == index ? ActiveTabBorder : InactiveTabBorder;
                _frames[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            DragMove();
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
                TitlePhaseIndicator.Background = phase switch
                {
                    ClientPhase.Lobby or ClientPhase.Matchmaking => LobbyPhaseBrush,
                    ClientPhase.ReadyCheck => ReadyCheckPhaseBrush,
                    ClientPhase.Planning => HoverPhaseBrush,
                    ClientPhase.ChampSelect => BanPhaseBrush,
                    _ => DefaultPhaseBrush
                };
            });
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
