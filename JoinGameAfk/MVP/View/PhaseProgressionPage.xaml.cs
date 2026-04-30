using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.MVP.Controller;
using JoinGameAfk.Theme;

namespace JoinGameAfk.View
{
    public partial class PhaseProgressionPage : Page
    {
        public event Action<ClientPhase>? PhaseChanged;

        private PhaseController? _phaseController;
        private ChampSelectSettings? _settings;
        private ClientPhase _currentPhase;
        private bool _isClientConnected;
        private bool _isWatcherRunning;

        public PhaseProgressionPage()
        {
            InitializeComponent();

            SetWatcherState(false);
            SetClientConnection(false);
            UpdatePhase(ClientPhase.Unknown);
            UpdateDashboardStatus(new DashboardStatus());
            Unloaded += (_, _) => AppThemeManager.ThemeChanged -= RefreshTheme;
            AppThemeManager.ThemeChanged += RefreshTheme;
        }

        internal void SetSettings(ChampSelectSettings settings)
        {
            _settings = settings;
            _settings.Saved += RefreshTimingText;
            RefreshTimingText();
        }

        internal void SetController(PhaseController controller)
        {
            _phaseController = controller;
        }

        internal void SetLogsPage(LogsPage logsPage)
        {
            EmbeddedLogsFrame.Content = logsPage;
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_phaseController == null) return;

            if (_phaseController.IsRunning)
                _phaseController.Stop();
            else
                _phaseController.Start();
        }

        public void UpdatePhase(ClientPhase phase)
        {
            Dispatcher.Invoke(() =>
            {
                _currentPhase = phase;
                RefreshStatusText();
                PhaseChanged?.Invoke(phase);
            });
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                _isWatcherRunning = isRunning;
                ToggleButton.Content = isRunning ? "Stop" : "Start";
                ToggleButton.Background = isRunning
                    ? ResourceBrush("WatcherStopBrush", Brushes.Firebrick)
                    : ResourceBrush("WatcherStartBrush", Brushes.ForestGreen);
                RefreshStatusText();
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                _isClientConnected = isConnected;
                ConnectionText.Text = isConnected ? "Client connected" : "Client offline";
                ConnectionText.Foreground = isConnected
                    ? ResourceBrush("PhaseConnectedForegroundBrush", Brushes.LimeGreen)
                    : ResourceBrush("PhaseOfflineForegroundBrush", Brushes.LightCoral);
                RefreshStatusText();
            });
        }

        public void UpdateDashboardStatus(DashboardStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateChampionPriorityList(PickChampionPriorityList, PickChampionPlaceholderText, status.PickChampionPriority, status.PickChampionText);
                UpdateChampionPriorityList(BanChampionPriorityList, BanChampionPlaceholderText, status.BanChampionPriority, status.BanChampionText);

                ChampSelectSubPhaseText.Text = status.ChampSelectSubPhase;
                ChampSelectTimerText.Text = status.TimeLeftSeconds >= 0
                    ? $"{status.TimeLeftSeconds}s"
                    : "";
            });
        }

        private void RefreshTimingText()
        {
            if (_settings is null)
                return;

            Dispatcher.Invoke(() =>
            {
                PickLockValueText.Text = $"The champion will be locked when the timer hits {_settings.PickLockDelaySeconds}s.";
                BanLockValueText.Text = $"The ban will be locked when the timer hits {_settings.BanLockDelaySeconds}s.";
            });
        }

        private static void UpdateChampionPriorityList(ItemsControl itemsControl, TextBlock placeholderText, IReadOnlyList<DashboardChampionPlanItem> champions, string fallbackText)
        {
            itemsControl.ItemsSource = champions;

            bool hasChampions = champions.Count > 0;
            placeholderText.Visibility = hasChampions ? Visibility.Collapsed : Visibility.Visible;

            if (hasChampions)
                return;

            placeholderText.Text = string.IsNullOrWhiteSpace(fallbackText)
                ? string.Empty
                : fallbackText;
        }

        private void RefreshStatusText()
        {
            StatusText.Text = GetStatusLine(_currentPhase);
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
                _ when _isWatcherRunning && !_isClientConnected => "Waiting for client…",
                _ when _isWatcherRunning => "Watching",
                _ => "Stopped"
            };
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        private void RefreshTheme()
        {
            SetWatcherState(_isWatcherRunning);
            SetClientConnection(_isClientConnected);
        }
    }
}
