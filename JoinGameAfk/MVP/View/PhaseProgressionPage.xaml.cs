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
        public event Action<string>? ChampSelectSubPhaseChanged;

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
                status = ApplyPlanBlockerHighlights(status);

                UpdateChampionPriorityList(MyTeamBansList, MyTeamBansPlaceholderText, status.MyTeamBans, "No bans yet.");
                UpdateChampionPriorityList(TheirTeamBansList, TheirTeamBansPlaceholderText, status.TheirTeamBans, "No bans yet.");
                UpdateTeamSlotList(MyTeamSlotList, status.MyTeamSlots);
                UpdateTeamSlotList(TheirTeamSlotList, status.TheirTeamSlots);
                UpdateChampionPriorityList(PickChampionPriorityList, PickChampionPlaceholderText, status.PickChampionPriority, status.PickChampionText);
                UpdateChampionPriorityList(BanChampionPriorityList, BanChampionPlaceholderText, status.BanChampionPriority, status.BanChampionText);
                UpdatePlanDisplay(status);

                string champSelectSubPhase = string.IsNullOrWhiteSpace(status.ChampSelectSubPhase)
                    ? "Idle"
                    : status.ChampSelectSubPhase;
                ChampSelectSubPhaseText.Text = champSelectSubPhase;
                ChampSelectSubPhaseChanged?.Invoke(champSelectSubPhase);
                ChampSelectTimerText.Text = status.TimeLeftSeconds >= 0
                    ? $"{status.TimeLeftSeconds}"
                    : "--";
                QueueLogRowResize();
            });
        }

        private static DashboardStatus ApplyPlanBlockerHighlights(DashboardStatus status)
        {
            var sourceLabelsByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            AddSourceBlockers(sourceLabelsByKey, status.MyTeamBans, "your bans");
            AddSourceBlockers(sourceLabelsByKey, status.TheirTeamBans, "enemy bans");
            AddSourceBlockers(sourceLabelsByKey, status.MyTeamSlots.Where(slot => !slot.IsLocalPlayer), "your team");
            AddSourceBlockers(sourceLabelsByKey, status.TheirTeamSlots, "enemy team");

            var ownActionLabelsByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            AddSourceBlockers(ownActionLabelsByKey, status.MyTeamSlots.Where(slot => slot.IsLocalPlayer), "you");

            var planLabelsByKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            AddPlanReferences(planLabelsByKey, status.PickChampionPriority, "pick plan");
            AddPlanReferences(planLabelsByKey, status.BanChampionPriority, "ban plan");

            return status with
            {
                MyTeamBans = HighlightChampionSources(status.MyTeamBans, planLabelsByKey),
                TheirTeamBans = HighlightChampionSources(status.TheirTeamBans, planLabelsByKey),
                MyTeamSlots = HighlightTeamSources(status.MyTeamSlots, planLabelsByKey),
                TheirTeamSlots = HighlightTeamSources(status.TheirTeamSlots, planLabelsByKey),
                PickChampionPriority = HighlightPlanItems(status.PickChampionPriority, sourceLabelsByKey, ownActionLabelsByKey),
                BanChampionPriority = HighlightPlanItems(status.BanChampionPriority, sourceLabelsByKey, ownActionLabelsByKey)
            };
        }

        private static void AddSourceBlockers(Dictionary<string, List<string>> sourceLabelsByKey, IEnumerable<DashboardChampionPlanItem> champions, string sourceLabel)
        {
            foreach (var champion in champions)
                AddLabelForChampion(sourceLabelsByKey, champion.ChampionId, champion.Name, sourceLabel);
        }

        private static void AddSourceBlockers(Dictionary<string, List<string>> sourceLabelsByKey, IEnumerable<DashboardTeamSlotItem> slots, string sourceLabel)
        {
            foreach (var slot in slots)
                AddLabelForChampion(sourceLabelsByKey, slot.ChampionId, slot.ChampionName, sourceLabel);
        }

        private static void AddPlanReferences(Dictionary<string, List<string>> planLabelsByKey, IEnumerable<DashboardChampionPlanItem> champions, string planLabel)
        {
            foreach (var champion in champions)
                AddLabelForChampion(planLabelsByKey, champion.ChampionId, champion.Name, planLabel);
        }

        private static IReadOnlyList<DashboardChampionPlanItem> HighlightChampionSources(IReadOnlyList<DashboardChampionPlanItem> champions, IReadOnlyDictionary<string, List<string>> planLabelsByKey)
        {
            return champions
                .Select(champion =>
                {
                    var planLabels = FindLabels(planLabelsByKey, champion.ChampionId, champion.Name);
                    return planLabels.Count == 0
                        ? champion with { IsPlanReference = false, PlanReferenceText = string.Empty, IsOwnAction = false }
                        : champion with
                        {
                            IsPlanReference = true,
                            PlanReferenceText = $"Blocks {FormatLabelList(planLabels)}",
                            IsOwnAction = false
                        };
                })
                .ToList();
        }

        private static IReadOnlyList<DashboardTeamSlotItem> HighlightTeamSources(IReadOnlyList<DashboardTeamSlotItem> slots, IReadOnlyDictionary<string, List<string>> planLabelsByKey)
        {
            return slots
                .Select(slot =>
                {
                    var planLabels = FindLabels(planLabelsByKey, slot.ChampionId, slot.ChampionName);
                    return planLabels.Count == 0
                        ? slot with { IsPlanReference = false, PlanReferenceText = string.Empty, IsOwnAction = false }
                        : slot.IsLocalPlayer
                            ? slot with
                            {
                                IsPlanReference = true,
                                PlanReferenceText = $"In {FormatLabelList(planLabels)}",
                                IsOwnAction = true
                            }
                        : slot with
                        {
                            IsPlanReference = true,
                            PlanReferenceText = $"Blocks {FormatLabelList(planLabels)}",
                            IsOwnAction = false
                        };
                })
                .ToList();
        }

        private static IReadOnlyList<DashboardChampionPlanItem> HighlightPlanItems(IReadOnlyList<DashboardChampionPlanItem> champions, IReadOnlyDictionary<string, List<string>> sourceLabelsByKey, IReadOnlyDictionary<string, List<string>> ownActionLabelsByKey)
        {
            return champions
                .Select(champion =>
                {
                    var ownActionLabels = FindLabels(ownActionLabelsByKey, champion.ChampionId, champion.Name);
                    if (ownActionLabels.Count > 0)
                    {
                        return champion with
                        {
                            IsOwnAction = true,
                            StatusText = $"Selected by {FormatLabelList(ownActionLabels)}"
                        };
                    }

                    var sourceLabels = FindLabels(sourceLabelsByKey, champion.ChampionId, champion.Name);
                    return sourceLabels.Count == 0
                        ? champion with { IsOwnAction = false }
                        : champion with
                        {
                            IsOwnAction = false,
                            IsAvailable = false,
                            StatusText = $"Blocked by {FormatLabelList(sourceLabels)}"
                        };
                })
                .ToList();
        }

        private static void AddLabelForChampion(Dictionary<string, List<string>> labelsByKey, int championId, string championName, string label)
        {
            foreach (string key in GetChampionMatchKeys(championId, championName))
                AddLabel(labelsByKey, key, label);
        }

        private static IReadOnlyList<string> FindLabels(IReadOnlyDictionary<string, List<string>> labelsByKey, int championId, string championName)
        {
            foreach (string key in GetChampionMatchKeys(championId, championName))
            {
                if (labelsByKey.TryGetValue(key, out var labels))
                    return labels;
            }

            return [];
        }

        private static IEnumerable<string> GetChampionMatchKeys(int championId, string championName)
        {
            if (championId > 0)
                yield return $"id:{championId}";

            string normalizedName = NormalizeChampionName(championName);
            if (!string.IsNullOrEmpty(normalizedName)
                && !string.Equals(normalizedName, "NOCHAMPION", StringComparison.Ordinal))
            {
                yield return $"name:{normalizedName}";
            }
        }

        private static string NormalizeChampionName(string championName)
        {
            return new string(championName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static void AddLabel(Dictionary<string, List<string>> labelsByKey, string key, string label)
        {
            if (!labelsByKey.TryGetValue(key, out var labels))
            {
                labels = [];
                labelsByKey[key] = labels;
            }

            if (!labels.Contains(label, StringComparer.Ordinal))
                labels.Add(label);
        }

        private static string FormatLabelList(IReadOnlyList<string> labels)
        {
            return labels.Count switch
            {
                0 => string.Empty,
                1 => labels[0],
                2 => $"{labels[0]} and {labels[1]}",
                _ => $"{string.Join(", ", labels.Take(labels.Count - 1))}, and {labels[^1]}"
            };
        }

        private static void UpdateTeamSlotList(ItemsControl itemsControl, IReadOnlyList<DashboardTeamSlotItem> slots)
        {
            itemsControl.ItemsSource = slots;
        }

        private void UpdatePlanDisplay(DashboardStatus status)
        {
            PickPlanLockText.Text = GetPlanLockText(status.PickLockText);
            BanPlanLockText.Text = GetPlanLockText(status.BanLockText);
        }

        private static string GetPlanLockText(string lockText)
        {
            return string.IsNullOrWhiteSpace(lockText)
                ? "Lock timing unavailable."
                : lockText;
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
