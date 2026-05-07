using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.View
{
    public partial class PickBanOverlayWindow : Window
    {
        private const double TimerRenderBoundaryPaddingMs = 10;
        private const double MinimumTimerRenderDelayMs = 25;
        private const double MaximumTimerRenderDelayMs = 1000;

        private readonly DispatcherTimer _timerRenderTimer;
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private string _champSelectSubPhase = string.Empty;
        private long _timerBaselineTimeLeftMs = -1;
        private DateTime _timerBaselineObservedAtUtc = DateTime.MinValue;
        private long _lockTimerBaselineTimeLeftMs = -1;
        private DateTime _lockTimerBaselineObservedAtUtc = DateTime.MinValue;

        public PickBanOverlayWindow()
        {
            InitializeComponent();

            _timerRenderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(MaximumTimerRenderDelayMs)
            };
            _timerRenderTimer.Tick += (_, _) => RenderTimers();

            RefreshStatusDisplay();
            RefreshTopmostButton();
            UpdateDashboardStatus(new DashboardStatus());
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
                PickPlanList.ItemsSource = status.PickChampionPriority;
                BanPlanList.ItemsSource = status.BanChampionPriority;

                UpdatePlanPlaceholder(
                    PickPlaceholderText,
                    status.PickChampionPriority,
                    status.PickChampionText);
                UpdatePlanPlaceholder(
                    BanPlaceholderText,
                    status.BanChampionPriority,
                    status.BanChampionText);

                PickLockText.Text = GetPlanLockText(status.PickLockText);
                BanLockText.Text = GetPlanLockText(status.BanLockText);
                PositionText.Text = GetPositionText(status.CurrentPosition);
                UpdateTimerBaselines(status);
            });
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
            IReadOnlyCollection<DashboardChampionPlanItem> champions,
            string fallbackText)
        {
            bool hasChampions = champions.Count > 0;
            placeholder.Visibility = hasChampions ? Visibility.Collapsed : Visibility.Visible;
            if (!hasChampions)
                placeholder.Text = string.IsNullOrWhiteSpace(fallbackText) ? "Waiting for champion select." : fallbackText;
        }

        private void UpdateTimerBaselines(DashboardStatus status)
        {
            long timeLeftMs = status.TimeLeftMilliseconds >= 0
                ? status.TimeLeftMilliseconds
                : status.TimeLeftSeconds >= 0
                    ? status.TimeLeftSeconds * 1000L
                    : -1;

            if (timeLeftMs < 0)
            {
                ClearPhaseTimer();
            }
            else
            {
                _timerBaselineTimeLeftMs = Math.Max(0, timeLeftMs);
                _timerBaselineObservedAtUtc = status.TimeLeftObservedAtUtc == DateTime.MinValue
                    ? DateTime.UtcNow
                    : status.TimeLeftObservedAtUtc;
            }

            if (status.ActiveLockTimeLeftMilliseconds < 0 || string.IsNullOrWhiteSpace(status.ActiveLockActionType))
            {
                ClearLockTimer();
                LockTimerLabel.Text = "lock";
            }
            else
            {
                _lockTimerBaselineTimeLeftMs = Math.Max(0, status.ActiveLockTimeLeftMilliseconds);
                _lockTimerBaselineObservedAtUtc = status.ActiveLockTimeLeftObservedAtUtc == DateTime.MinValue
                    ? DateTime.UtcNow
                    : status.ActiveLockTimeLeftObservedAtUtc;
                LockTimerLabel.Text = $"{status.ActiveLockActionType.ToLowerInvariant()} lock";
            }

            RenderTimers();
        }

        private void ClearPhaseTimer()
        {
            _timerBaselineTimeLeftMs = -1;
            _timerBaselineObservedAtUtc = DateTime.MinValue;
            TimerText.Text = "--";
        }

        private void ClearLockTimer()
        {
            _lockTimerBaselineTimeLeftMs = -1;
            _lockTimerBaselineObservedAtUtc = DateTime.MinValue;
            LockTimerText.Text = "--";
        }

        private void RenderTimers()
        {
            double phaseRemainingMs = RenderPhaseTimer();
            double lockRemainingMs = RenderLockTimer();
            ScheduleNextTimerRender(phaseRemainingMs, lockRemainingMs);
        }

        private double RenderPhaseTimer()
        {
            if (_timerBaselineTimeLeftMs < 0 || _timerBaselineObservedAtUtc == DateTime.MinValue)
            {
                TimerText.Text = "--";
                return -1;
            }

            double elapsedMs = Math.Max(0, (DateTime.UtcNow - _timerBaselineObservedAtUtc).TotalMilliseconds);
            double remainingMs = Math.Max(0, _timerBaselineTimeLeftMs - elapsedMs);
            TimerText.Text = GetDisplayTimeLeftSeconds(remainingMs).ToString(CultureInfo.InvariantCulture);
            return remainingMs;
        }

        private double RenderLockTimer()
        {
            if (_lockTimerBaselineTimeLeftMs < 0 || _lockTimerBaselineObservedAtUtc == DateTime.MinValue)
            {
                LockTimerText.Text = "--";
                return -1;
            }

            double elapsedMs = Math.Max(0, (DateTime.UtcNow - _lockTimerBaselineObservedAtUtc).TotalMilliseconds);
            double remainingMs = Math.Max(0, _lockTimerBaselineTimeLeftMs - elapsedMs);
            LockTimerText.Text = FormatPreciseTimeLeft(remainingMs);
            return remainingMs;
        }

        private void ScheduleNextTimerRender(double phaseRemainingMs, double lockRemainingMs)
        {
            _timerRenderTimer.Stop();
            double delayMs = double.PositiveInfinity;

            if (phaseRemainingMs > 0)
                delayMs = Math.Min(delayMs, GetDelayToNextWholeSecond(phaseRemainingMs));

            if (lockRemainingMs > 0)
                delayMs = Math.Min(delayMs, GetDelayToNextTenthSecond(lockRemainingMs));

            if (double.IsPositiveInfinity(delayMs))
                return;

            delayMs = Math.Clamp(
                delayMs,
                MinimumTimerRenderDelayMs,
                MaximumTimerRenderDelayMs);

            _timerRenderTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _timerRenderTimer.Start();
        }

        private static double GetDelayToNextWholeSecond(double remainingMs)
        {
            double millisecondsUntilNextVisibleChange = remainingMs % 1000d;
            if (millisecondsUntilNextVisibleChange <= 0)
                millisecondsUntilNextVisibleChange = 1000d;

            return millisecondsUntilNextVisibleChange + TimerRenderBoundaryPaddingMs;
        }

        private static double GetDelayToNextTenthSecond(double remainingMs)
        {
            double millisecondsUntilNextVisibleChange = remainingMs % 100d;
            if (millisecondsUntilNextVisibleChange <= 0)
                millisecondsUntilNextVisibleChange = 100d;

            return millisecondsUntilNextVisibleChange + TimerRenderBoundaryPaddingMs;
        }

        private static int GetDisplayTimeLeftSeconds(double timeLeftMs)
        {
            if (timeLeftMs <= 0)
                return 0;

            return (int)(timeLeftMs / 1000d);
        }

        private static string FormatPreciseTimeLeft(double timeLeftMs)
        {
            if (timeLeftMs <= 0)
                return "0.0s";

            return (timeLeftMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        private void RefreshStatusDisplay()
        {
            StatusText.Text = GetStatusLine(_currentPhase);
            PhaseText.Text = GetPhaseText(_currentPhase);
            StatusDot.Background = GetStatusBrush(_currentPhase);
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

        private Brush GetStatusBrush(ClientPhase phase)
        {
            if (_isWatcherRunning && !_isClientConnected)
                return ResourceBrush("PhaseBanBrush", Brushes.IndianRed);

            return phase switch
            {
                ClientPhase.Lobby => ResourceBrush("PhaseHoverBrush", Brushes.Goldenrod),
                ClientPhase.Matchmaking => ResourceBrush("PhaseLobbyBrush", Brushes.DodgerBlue),
                ClientPhase.ReadyCheck => ResourceBrush("PhaseReadyCheckBrush", Brushes.ForestGreen),
                ClientPhase.ChampSelect => ResourceBrush("AccentBlueBrush", Brushes.DodgerBlue),
                ClientPhase.Planning => ResourceBrush("PhaseHoverBrush", Brushes.DarkOrange),
                ClientPhase.InGame => ResourceBrush("PhaseDefaultBrush", Brushes.White),
                _ => ResourceBrush("PhaseDefaultBrush", Brushes.White)
            };
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
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

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void TopmostButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            RefreshTopmostButton();
        }

        private void RefreshTopmostButton()
        {
            TopmostIcon.Opacity = Topmost ? 1 : 0.45;
            string label = Topmost ? "Unpin overlay from top" : "Pin overlay on top";
            TopmostButton.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(TopmostButton, label);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _timerRenderTimer.Stop();
        }
    }
}
