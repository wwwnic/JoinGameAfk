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
                ChampionInitial = GetChampionInitial(championName),
                ChampionName = championName,
                RoleName = string.IsNullOrWhiteSpace(roleName) ? "Manual test" : roleName
            });

            ApplyDashboardStatus();
        }

        private void ApplyDashboardStatus()
        {
            if (!_canApply())
            {
                Close();
                return;
            }

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

        private static string GetChampionInitial(string championName)
        {
            char firstLetter = championName.FirstOrDefault(char.IsLetterOrDigit);
            return firstLetter == default
                ? "?"
                : char.ToUpperInvariant(firstLetter).ToString();
        }
    }
}
