using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.View
{
    public partial class PickBanOverlayWindow : Window
    {
        private readonly ChampSelectSettings _settings;
        private readonly DraftCountdownTimer _draftCountdownTimer;
        private DashboardStatus _lastDashboardStatus = new();
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private string _champSelectSubPhase = string.Empty;

        public event Action<double, double>? PositionChangedByUser;

        public PickBanOverlayWindow(ChampSelectSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            _draftCountdownTimer = new DraftCountdownTimer(RenderCountdownTimers);

            RefreshStatusDisplay();
            RefreshTopmostButton();
            UpdateDashboardStatus(new DashboardStatus());
            _settings.Saved += Settings_Saved;
            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
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
                RenderDashboardStatus(status);
            });
        }

        private void Settings_Saved()
        {
            Dispatch(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatch(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatch(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void RenderDashboardStatus(DashboardStatus status)
        {
            PickPlanList.ItemsSource = DashboardChampionPlanDisplay.CreateList(status.PickChampionPriority, _settings);
            BanPlanList.ItemsSource = DashboardChampionPlanDisplay.CreateList(status.BanChampionPriority, _settings);

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
            IReadOnlyCollection<DashboardChampionPlanItem> champions,
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
                _champSelectSubPhase);
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

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
                PositionChangedByUser?.Invoke(Left, Top);
            }
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
            _draftCountdownTimer.Stop();
            _settings.Saved -= Settings_Saved;
            ChampionCatalog.CatalogChanged -= ChampionCatalog_CatalogChanged;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
        }
    }
}
