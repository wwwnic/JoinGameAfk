using System.Windows;
using System.Windows.Controls;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.View
{
    public partial class PhaseProgressionTestWindow : Window
    {
        private readonly PhaseProgressionPage _phaseProgressionPage;
        private readonly LogsPage _logsPage;
        private readonly Func<bool> _canApply;
        private readonly List<DashboardChampionPlanItem> _myTeamBans = [];
        private readonly List<DashboardChampionPlanItem> _enemyTeamBans = [];
        private readonly List<DashboardTeamSlotItem> _myTeamSlots = [];
        private readonly List<DashboardTeamSlotItem> _enemyTeamSlots = [];
        private readonly List<DashboardChampionPlanItem> _pickPlan = [];
        private readonly List<DashboardChampionPlanItem> _banPlan = [];

        public PhaseProgressionTestWindow(PhaseProgressionPage phaseProgressionPage, LogsPage logsPage, Func<bool> canApply)
        {
            InitializeComponent();
            _phaseProgressionPage = phaseProgressionPage;
            _logsPage = logsPage;
            _canApply = canApply;
            PreviewStoppedIndicator();
            PreviewReadyAccept(ClientPhase.Unknown, isWatcherRunning: false, isClientConnected: false);
        }

        private void PreviewNeutralIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator(string.Empty);
        }

        private void PreviewClientStoppedIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.Unknown, isWatcherRunning: true, isClientConnected: false);
        }

        private void PreviewWatchingIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.Unknown, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewLobbyIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.Lobby, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewInQueueIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.Matchmaking, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyCheckCountdownIndicator_Click(object sender, RoutedEventArgs e)
        {
            const long countdownMilliseconds = 5000;
            PreviewIndicator(
                ClientPhase.ReadyCheck,
                isWatcherRunning: true,
                isClientConnected: true,
                champSelectSubPhase: string.Empty,
                readyCheckAutoAcceptDelayMilliseconds: countdownMilliseconds,
                readyCheckAutoAcceptTimeLeftMilliseconds: countdownMilliseconds,
                readyCheckAutoAcceptObservedAtUtc: DateTime.UtcNow);
        }

        private void PreviewReadyCheckAutoDisabledIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.ReadyCheck, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyCheckAcceptedIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.ReadyCheck, isWatcherRunning: true, isClientConnected: true, "Accepted");
        }

        private void PreviewReadyCheckDeclinedIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.ReadyCheck, isWatcherRunning: true, isClientConnected: true, "Declined");
        }

        private void PreviewPlanningIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.Planning, isWatcherRunning: true, isClientConnected: true, "Planning");
        }

        private void PreviewPlanningDoneIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.Planning, isWatcherRunning: true, isClientConnected: true, "Planning done");
        }

        private void PreviewBanIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator("Ban");
        }

        private void PreviewBanDoneIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator("Ban done");
        }

        private void PreviewPickIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator("Pick");
        }

        private void PreviewOptionsUnavailableIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator("Pick", showChampionWarningGlyph: true);
        }

        private void PreviewPickDoneIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator("Pick done");
        }

        private void PreviewFinalizationIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewChampionSelectIndicator("Finalization");
        }

        private void PreviewStoppedIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewStoppedIndicator();
        }

        private void PreviewInGameIndicator_Click(object sender, RoutedEventArgs e)
        {
            PreviewIndicator(ClientPhase.InGame, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyAcceptStopped_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(ClientPhase.Unknown, isWatcherRunning: false, isClientConnected: false);
        }

        private void PreviewReadyAcceptLobby_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(ClientPhase.Lobby, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyAcceptQueue_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(ClientPhase.Matchmaking, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyAcceptCountdown_Click(object sender, RoutedEventArgs e)
        {
            const long countdownMilliseconds = 5000;
            PreviewReadyAccept(
                ClientPhase.ReadyCheck,
                isWatcherRunning: true,
                isClientConnected: true,
                new DashboardStatus
                {
                    ReadyCheckAutoAcceptDelayMilliseconds = countdownMilliseconds,
                    ReadyCheckAutoAcceptTimeLeftMilliseconds = countdownMilliseconds,
                    ReadyCheckAutoAcceptObservedAtUtc = DateTime.UtcNow
                });
        }

        private void PreviewReadyAcceptAccepted_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(
                ClientPhase.ReadyCheck,
                isWatcherRunning: true,
                isClientConnected: true,
                new DashboardStatus { ReadyCheckResponse = "Accepted" });
        }

        private void PreviewReadyAcceptDeclined_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(
                ClientPhase.ReadyCheck,
                isWatcherRunning: true,
                isClientConnected: true,
                new DashboardStatus { ReadyCheckResponse = "Declined" });
        }

        private void PreviewReadyAcceptSkippedReadyCheck_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(
                ClientPhase.ReadyCheck,
                isWatcherRunning: true,
                isClientConnected: true,
                new DashboardStatus { SkipsReadyCheck = true });
        }

        private void PreviewReadyAcceptChampionSelect_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(ClientPhase.ChampSelect, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyAcceptInGame_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(ClientPhase.InGame, isWatcherRunning: true, isClientConnected: true);
        }

        private void PreviewReadyAcceptUnsupportedQueue_Click(object sender, RoutedEventArgs e)
        {
            PreviewReadyAccept(
                ClientPhase.ChampSelect,
                isWatcherRunning: true,
                isClientConnected: true,
                new DashboardStatus
                {
                    IsUnsupportedMode = true,
                    UnsupportedQueueText = "Quickplay",
                    UnsupportedModeText = "Quickplay (queue 490) is not supported for draft tools. Auto-accept can still work here."
                });
        }

        private void TriggerUnsupportedMode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCanApply())
                return;

            const string unsupportedModeText = "Quickplay (queue 490) is not supported for draft tools. Use Normal Draft, Ranked Solo/Duo, or Ranked Flex; auto-accept can still work here.";
            _logsPage.WriteLine($"Unsupported queue detected: {unsupportedModeText}");
        }

        private void AddMyBan_Click(object sender, RoutedEventArgs e)
        {
            AddChampionPlanItem(MyBanBox.Text, _myTeamBans, string.Empty);
        }

        private void AddEnemyBan_Click(object sender, RoutedEventArgs e)
        {
            AddChampionPlanItem(EnemyBanBox.Text, _enemyTeamBans, string.Empty);
        }

        private void AddMyTeamChampion_Click(object sender, RoutedEventArgs e)
        {
            AddTeamChampion(MyTeamChampionBox.Text, MyTeamRoleBox.Text, _myTeamSlots);
        }

        private void AddEnemyTeamChampion_Click(object sender, RoutedEventArgs e)
        {
            AddTeamChampion(EnemyTeamChampionBox.Text, EnemyTeamRoleBox.Text, _enemyTeamSlots);
        }

        private void AddPickPlan_Click(object sender, RoutedEventArgs e)
        {
            AddChampionPlanItem(PickPlanBox.Text, _pickPlan, "Pick");
        }

        private void AddBanPlan_Click(object sender, RoutedEventArgs e)
        {
            AddChampionPlanItem(BanPlanBox.Text, _banPlan, "Ban");
        }

        private void AddLog_Click(object sender, RoutedEventArgs e)
        {
            string message = NormalizeText(LogLineBox.Text);
            if (string.IsNullOrWhiteSpace(message))
                return;

            _logsPage.WriteLine(message);
        }

        private void ClearTextBox_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is TextBox textBox)
                textBox.Clear();
        }

        private void ClearDashboard_Click(object sender, RoutedEventArgs e)
        {
            _myTeamBans.Clear();
            _enemyTeamBans.Clear();
            _myTeamSlots.Clear();
            _enemyTeamSlots.Clear();
            _pickPlan.Clear();
            _banPlan.Clear();
            ApplyDashboardStatus();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PreviewChampionSelectIndicator(string champSelectSubPhase, bool showChampionWarningGlyph = false)
        {
            PreviewIndicator(
                ClientPhase.ChampSelect,
                isWatcherRunning: true,
                isClientConnected: true,
                champSelectSubPhase,
                showChampionWarningGlyph: showChampionWarningGlyph);
        }

        private void PreviewStoppedIndicator()
        {
            PreviewIndicator(ClientPhase.Unknown, isWatcherRunning: false, isClientConnected: false);
        }

        private void PreviewIndicator(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            string champSelectSubPhase = "",
            long readyCheckAutoAcceptDelayMilliseconds = -1,
            long readyCheckAutoAcceptTimeLeftMilliseconds = -1,
            DateTime readyCheckAutoAcceptObservedAtUtc = default,
            bool showChampionWarningGlyph = false)
        {
            TestPhaseIndicator.Update(
                phase,
                isWatcherRunning,
                isClientConnected,
                champSelectSubPhase,
                readyCheckAutoAcceptDelayMilliseconds,
                readyCheckAutoAcceptTimeLeftMilliseconds,
                readyCheckAutoAcceptObservedAtUtc,
                showChampionWarningGlyph);
        }

        private void PreviewReadyAccept(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            DashboardStatus? status = null)
        {
            TestReadyAcceptPanel.Update(
                phase,
                isWatcherRunning,
                isClientConnected,
                status ?? new DashboardStatus());
        }

        private void AddChampionPlanItem(string text, List<DashboardChampionPlanItem> target, string statusText)
        {
            string championName = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(championName))
                return;

            target.Add(new DashboardChampionPlanItem
            {
                ChampionId = ResolveChampionId(championName),
                Name = championName,
                SourcePosition = Position.Default,
                IsAvailable = true,
                StatusText = statusText
            });

            ApplyDashboardStatus();
        }

        private void AddTeamChampion(string championText, string roleText, List<DashboardTeamSlotItem> target)
        {
            string championName = NormalizeText(championText);
            if (string.IsNullOrWhiteSpace(championName))
                return;

            string roleName = NormalizeText(roleText);
            int championId = ResolveChampionId(championName);
            target.Add(new DashboardTeamSlotItem
            {
                ChampionId = championId,
                ChampionName = championName,
                RoleName = string.IsNullOrWhiteSpace(roleName) ? "Manual test" : roleName
            });

            ApplyDashboardStatus();
        }

        private void ApplyDashboardStatus()
        {
            if (!EnsureCanApply())
                return;

            _phaseProgressionPage.UpdateDashboardStatus(new DashboardStatus
            {
                MyTeamBans = _myTeamBans.ToList(),
                TheirTeamBans = _enemyTeamBans.ToList(),
                MyTeamSlots = _myTeamSlots.ToList(),
                TheirTeamSlots = _enemyTeamSlots.ToList(),
                PickChampionPriority = _pickPlan.ToList(),
                BanChampionPriority = _banPlan.ToList(),
                PickChampionText = "No test picks added.",
                BanChampionText = "No test ban plan added.",
                PickLockText = "Test mode",
                BanLockText = "Test mode",
                ChampSelectSubPhase = "Test",
                TimeLeftSeconds = -1
            });
        }

        private bool EnsureCanApply()
        {
            if (_canApply())
                return true;

            Close();
            return false;
        }

        private static string NormalizeText(string text)
        {
            return text.Trim();
        }

        private static int ResolveChampionId(string championName)
        {
            return ChampionCatalog.TryGetByName(championName, out var champion)
                ? champion!.Id
                : 0;
        }

    }
}
