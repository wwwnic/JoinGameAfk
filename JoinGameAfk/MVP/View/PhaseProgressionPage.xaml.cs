using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.View
{
    public partial class PhaseProgressionPage : Page
    {
        public event Action<ClientPhase>? PhaseChanged;
        public event Action<bool>? WatcherStateChanged;
        public event Action<bool>? ClientConnectionChanged;

        private const double MinimumLogRowHeight = 150;

        public PhaseProgressionPage()
        {
            InitializeComponent();

            SetWatcherState(false);
            SetClientConnection(false);
            UpdatePhase(ClientPhase.Unknown);
            UpdateDashboardStatus(new DashboardStatus());
            Loaded += (_, _) => QueueLogRowResize();
        }

        internal void SetLogsPage(LogsPage logsPage)
        {
            EmbeddedLogsFrame.Content = logsPage;
        }

        public void UpdatePhase(ClientPhase phase)
        {
            Dispatcher.Invoke(() =>
            {
                PhaseChanged?.Invoke(phase);
            });
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                WatcherStateChanged?.Invoke(isRunning);
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                ClientConnectionChanged?.Invoke(isConnected);
            });
        }

        public void UpdateDashboardStatus(DashboardStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateChampionPriorityList(MyTeamBansList, MyTeamBansPlaceholderText, status.MyTeamBans, "No bans yet.");
                UpdateChampionPriorityList(TheirTeamBansList, TheirTeamBansPlaceholderText, status.TheirTeamBans, "No bans yet.");
                UpdateChampionPriorityList(PickChampionPriorityList, PickChampionPlaceholderText, status.PickChampionPriority, status.PickChampionText);
                UpdateChampionPriorityList(BanChampionPriorityList, BanChampionPlaceholderText, status.BanChampionPriority, status.BanChampionText);

                ChampSelectSubPhaseText.Text = string.IsNullOrWhiteSpace(status.ChampSelectSubPhase)
                    ? "Idle"
                    : status.ChampSelectSubPhase;
                ChampSelectTimerText.Text = status.TimeLeftSeconds >= 0
                    ? $"{status.TimeLeftSeconds}"
                    : "--";
                QueueLogRowResize();
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

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueLogRowResize();
        }

        private void QueueLogRowResize()
        {
            Dispatcher.InvokeAsync(UpdateLogRowHeight, DispatcherPriority.Loaded);
        }

        private void UpdateLogRowHeight()
        {
            if (ActualHeight <= 0 || HeaderPanel.ActualHeight <= 0 || DraftLayoutGrid.ActualHeight <= 0)
                return;

            double availableHeight = ActualHeight
                - HeaderPanel.ActualHeight
                - DraftLayoutGrid.ActualHeight
                - 24;
            double targetHeight = Math.Max(MinimumLogRowHeight, availableHeight);

            if (!LogRow.Height.IsAbsolute || Math.Abs(LogRow.Height.Value - targetHeight) > 0.5)
                LogRow.Height = new GridLength(targetHeight);
        }
    }
}
