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
                UpdateTeamSlotList(MyTeamSlotList, status.MyTeamSlots);
                UpdateTeamSlotList(TheirTeamSlotList, status.TheirTeamSlots);
                UpdateChampionPriorityList(PickChampionPriorityList, PickChampionPlaceholderText, status.PickChampionPriority, status.PickChampionText);
                UpdateChampionPriorityList(BanChampionPriorityList, BanChampionPlaceholderText, status.BanChampionPriority, status.BanChampionText);
                UpdateCurrentRoleDisplay(GetCurrentPriorityListPosition(status));
                UpdateCurrentActionDisplay(status);

                ChampSelectSubPhaseText.Text = string.IsNullOrWhiteSpace(status.ChampSelectSubPhase)
                    ? "Idle"
                    : status.ChampSelectSubPhase;
                ChampSelectTimerText.Text = status.TimeLeftSeconds >= 0
                    ? $"{status.TimeLeftSeconds}"
                    : "--";
                QueueLogRowResize();
            });
        }

        private static void UpdateTeamSlotList(ItemsControl itemsControl, IReadOnlyList<DashboardTeamSlotItem> slots)
        {
            itemsControl.ItemsSource = slots;
        }

        private void UpdateCurrentRoleDisplay(Position position)
        {
            position = NormalizeDisplayPosition(position);

            var inactiveStyle = (Style)FindResource("RoleChipStyle");
            var activeStyle = (Style)FindResource("ActiveRoleChipStyle");

            SetRoleChipStyle(TopRoleChip, Position.Top, position, inactiveStyle, activeStyle);
            SetRoleChipStyle(JungleRoleChip, Position.Jungle, position, inactiveStyle, activeStyle);
            SetRoleChipStyle(MidRoleChip, Position.Mid, position, inactiveStyle, activeStyle);
            SetRoleChipStyle(AdcRoleChip, Position.Adc, position, inactiveStyle, activeStyle);
            SetRoleChipStyle(SupportRoleChip, Position.Support, position, inactiveStyle, activeStyle);
            SetRoleChipStyle(DefaultRoleChip, Position.Default, position, inactiveStyle, activeStyle);
        }

        private void UpdateCurrentActionDisplay(DashboardStatus status)
        {
            bool isBanAction = IsBanAction(status);
            IReadOnlyList<DashboardChampionPlanItem> actionPlan = isBanAction
                ? status.BanChampionPriority
                : status.PickChampionPriority;
            string actionName = isBanAction ? "Ban" : "Pick";
            string lockText = isBanAction ? status.BanLockText : status.PickLockText;
            var targetChampion = GetCurrentActionChampion(actionPlan);

            CurrentActionChampionText.Text = targetChampion?.Name ?? "--";
            CurrentActionTitleText.Text = $"{actionName} target";
            CurrentActionLockText.Text = string.IsNullOrWhiteSpace(lockText)
                ? "Lock timing unavailable."
                : lockText;

            if (targetChampion is null)
            {
                bool isWaiting = status.TimeLeftSeconds < 0
                    && string.IsNullOrWhiteSpace(status.ChampSelectSubPhase)
                    && status.PickChampionPriority.Count == 0
                    && status.BanChampionPriority.Count == 0;

                CurrentActionSubtitleText.Visibility = isWaiting ? Visibility.Collapsed : Visibility.Visible;
                CurrentActionSubtitleText.Text = isWaiting
                    ? string.Empty
                    : $"No {actionName.ToLowerInvariant()} champion configured.";
                CurrentActionLockText.Text = isWaiting
                    ? "Lock timing unavailable."
                    : CurrentActionLockText.Text;
                return;
            }

            CurrentActionSubtitleText.Visibility = Visibility.Collapsed;
        }

        private static Position GetCurrentPriorityListPosition(DashboardStatus status)
        {
            bool isBanAction = IsBanAction(status);
            IReadOnlyList<DashboardChampionPlanItem> actionPlan = isBanAction
                ? status.BanChampionPriority
                : status.PickChampionPriority;

            return NormalizeDisplayPosition(GetCurrentActionChampion(actionPlan)?.SourcePosition ?? status.CurrentPosition);
        }

        private static bool IsBanAction(DashboardStatus status)
        {
            return string.Equals(status.ChampSelectSubPhase, "Ban", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetRoleChipStyle(Border roleChip, Position role, Position currentPosition, Style inactiveStyle, Style activeStyle)
        {
            roleChip.Style = role == currentPosition ? activeStyle : inactiveStyle;
        }

        private static Position NormalizeDisplayPosition(Position position)
        {
            return position is Position.Default or Position.None
                ? Position.Default
                : position;
        }

        private static string GetPositionDisplayName(Position position)
        {
            return position switch
            {
                Position.Top => "Top",
                Position.Jungle => "Jungle",
                Position.Mid => "Mid",
                Position.Adc => "ADC",
                Position.Support => "Support",
                _ => "Default",
            };
        }

        private static DashboardChampionPlanItem? GetCurrentActionChampion(IReadOnlyList<DashboardChampionPlanItem> actionPlan)
        {
            return actionPlan.FirstOrDefault(champion => champion.IsAvailable)
                ?? actionPlan.FirstOrDefault();
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
