using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.View
{
    public partial class PhaseProgressionPage : Page
    {
        public event Action<ClientPhase>? PhaseChanged;
        public event Action<bool>? WatcherStateChanged;
        public event Action<bool>? ClientConnectionChanged;
        public event Action<string>? ChampSelectSubPhaseChanged;
        public event Action<DashboardStatus>? DashboardStatusChanged;

        private const double MinimumLogRowHeight = 150;
        private readonly ChampSelectSettings _settings;
        private readonly DraftCountdownTimer _draftCountdownTimer;
        private Button[] _dashboardViewButtons = [];
        private FrameworkElement[] _dashboardTabContents = [];
        private DashboardStatus _lastDashboardStatus = new();
        private ClientPhase _currentPhase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private int _activeDashboardViewIndex;
        private bool _hasManualDashboardViewOverride;

        public PhaseProgressionPage(ChampSelectSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            _draftCountdownTimer = new DraftCountdownTimer(RenderCountdownTimers);
            _dashboardViewButtons = [ReadyAcceptDashboardViewButton, ChampionSelectDashboardViewButton];
            _dashboardTabContents = [ReadyAcceptTabContent, ChampionSelectTabContent];

            SetWatcherState(false);
            SetClientConnection(false);
            UpdatePhase(ClientPhase.Unknown);
            UpdateDashboardStatus(new DashboardStatus());
            ActivateDashboardView(0);
            Loaded += (_, _) => QueueLogRowResize();
            Unloaded += PhaseProgressionPage_Unloaded;
            _settings.Saved += Settings_Saved;
            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
            ChampionImageSelectionStore.SelectionsChanged += ChampionImageSelectionStore_SelectionsChanged;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
        }

        private void PhaseProgressionPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _draftCountdownTimer.Stop();
            _settings.Saved -= Settings_Saved;
            ChampionCatalog.CatalogChanged -= ChampionCatalog_CatalogChanged;
            ChampionImageSelectionStore.SelectionsChanged -= ChampionImageSelectionStore_SelectionsChanged;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.TryInvoke(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.TryInvoke(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        private void ChampionImageSelectionStore_SelectionsChanged(object? sender, EventArgs e)
        {
            Dispatcher.TryInvoke(() => RenderDashboardStatus(_lastDashboardStatus));
        }

        internal void SetLogsPage(LogsPage logsPage)
        {
            EmbeddedLogsFrame.Content = logsPage;
        }

        public void UpdatePhase(ClientPhase phase)
        {
            Dispatcher.TryInvoke(() =>
            {
                bool phaseChanged = phase != _currentPhase;
                _currentPhase = phase;
                if (phaseChanged)
                    _hasManualDashboardViewOverride = false;

                SynchronizeDashboardViewForPhase();
                RefreshReadyAcceptPanel();
                PhaseChanged?.Invoke(phase);
            });
        }

        public void SetWatcherState(bool isRunning)
        {
            Dispatcher.TryInvoke(() =>
            {
                _isWatcherRunning = isRunning;
                if (!isRunning)
                    _hasManualDashboardViewOverride = false;

                SynchronizeDashboardViewForPhase();
                RefreshReadyAcceptPanel();
                WatcherStateChanged?.Invoke(isRunning);
            });
        }

        public void SetClientConnection(bool isConnected)
        {
            Dispatcher.TryInvoke(() =>
            {
                _isClientConnected = isConnected;
                if (!isConnected)
                    _hasManualDashboardViewOverride = false;

                SynchronizeDashboardViewForPhase();
                RefreshReadyAcceptPanel();
                ClientConnectionChanged?.Invoke(isConnected);
            });
        }

        public void UpdateDashboardStatus(DashboardStatus status)
        {
            Dispatcher.TryInvoke(() =>
            {
                _lastDashboardStatus = status;
                RenderDashboardStatus(status);
                SynchronizeDashboardViewForPhase();
                RefreshReadyAcceptPanel();
            });
        }

        private void ReadyAcceptDashboardViewButton_Click(object sender, RoutedEventArgs e)
        {
            _hasManualDashboardViewOverride = true;
            ActivateDashboardView(0);
        }

        private void ChampionSelectDashboardViewButton_Click(object sender, RoutedEventArgs e)
        {
            _hasManualDashboardViewOverride = true;
            ActivateDashboardView(1);
        }

        public void ShowReadyAcceptDashboardView()
        {
            Dispatcher.TryInvoke(() =>
            {
                _hasManualDashboardViewOverride = false;
                ActivateDashboardView(0);
                RefreshReadyAcceptPanel();
            });
        }

        private void SynchronizeDashboardViewForPhase()
        {
            if (_hasManualDashboardViewOverride)
                return;

            ActivateDashboardView(GetPreferredDashboardViewIndex(_currentPhase, _lastDashboardStatus));
        }

        private void ActivateDashboardView(int index)
        {
            if (index < 0 || index >= _dashboardTabContents.Length)
                index = 0;

            _activeDashboardViewIndex = index;

            for (int i = 0; i < _dashboardTabContents.Length; i++)
            {
                _dashboardTabContents[i].Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
                _dashboardViewButtons[i].Tag = i == index ? "Active" : null;
            }

            QueueLogRowResize();
        }

        private static int GetPreferredDashboardViewIndex(ClientPhase phase, DashboardStatus status)
        {
            if (status.IsUnsupportedMode)
                return 0;

            return phase is ClientPhase.ChampSelect or ClientPhase.Planning
                ? 1
                : 0;
        }

        private void RefreshReadyAcceptPanel()
        {
            ReadyAcceptPanel.Update(_currentPhase, _isWatcherRunning, _isClientConnected, _lastDashboardStatus);
        }

        private void RenderDashboardStatus(DashboardStatus status)
        {
            status = ApplyPlanBlockerHighlights(status);

            UpdateChampionPriorityList(MyTeamBansList, MyTeamBansPlaceholderText, status.MyTeamBans, "Your team bans");
            UpdateChampionPriorityList(TheirTeamBansList, TheirTeamBansPlaceholderText, status.TheirTeamBans, "Enemy team bans");
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
            DashboardStatusChanged?.Invoke(status);
            _draftCountdownTimer.Update(status);
            QueueLogRowResize();
        }

        private void RenderCountdownTimers(DraftCountdownTimerSnapshot snapshot)
        {
            ChampSelectTimerText.Text = snapshot.PhaseTimeText;

            if (!snapshot.HasActiveLockTimer)
            {
                PickPlanLockText.Text = "--";
                BanPlanLockText.Text = "--";
                return;
            }

            string lockText = $"Lock in {snapshot.LockTimeText}";

            if (string.Equals(snapshot.ActiveLockActionType, "Hover", StringComparison.Ordinal))
            {
                PickPlanLockText.Text = $"Hover in {snapshot.LockTimeText}";
                BanPlanLockText.Text = "--";
                return;
            }

            if (string.Equals(snapshot.ActiveLockActionType, "Pick", StringComparison.Ordinal))
            {
                PickPlanLockText.Text = lockText;
                BanPlanLockText.Text = "--";
                return;
            }

            if (string.Equals(snapshot.ActiveLockActionType, "Ban", StringComparison.Ordinal))
            {
                PickPlanLockText.Text = "--";
                BanPlanLockText.Text = lockText;
                return;
            }

            PickPlanLockText.Text = "--";
            BanPlanLockText.Text = "--";
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
                MyTeamBans = HighlightChampionSources(status.MyTeamBans, planLabelsByKey, DashboardChampionAvailabilityReason.Banned),
                TheirTeamBans = HighlightChampionSources(status.TheirTeamBans, planLabelsByKey, DashboardChampionAvailabilityReason.Banned),
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

        private static IReadOnlyList<DashboardChampionPlanItem> HighlightChampionSources(IReadOnlyList<DashboardChampionPlanItem> champions, IReadOnlyDictionary<string, List<string>> planLabelsByKey, string planReferenceReasonKind)
        {
            return champions
                .Select(champion =>
                {
                    var planLabels = FindLabels(planLabelsByKey, champion.ChampionId, champion.Name);
                    return planLabels.Count == 0
                        ? champion with { IsPlanReference = false, PlanReferenceText = string.Empty, PlanReferenceReasonKind = DashboardChampionAvailabilityReason.None, IsOwnAction = false }
                        : champion with
                        {
                            IsPlanReference = true,
                            PlanReferenceText = $"Blocks {FormatLabelList(planLabels)}",
                            PlanReferenceReasonKind = planReferenceReasonKind,
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
                        ? slot with { IsPlanReference = false, PlanReferenceText = string.Empty, PlanReferenceReasonKind = DashboardChampionAvailabilityReason.None, IsOwnAction = false }
                        : slot.IsLocalPlayer
                            ? slot with
                            {
                                IsPlanReference = true,
                                PlanReferenceText = $"In {FormatLabelList(planLabels)}",
                                PlanReferenceReasonKind = DashboardChampionAvailabilityReason.Selected,
                                IsOwnAction = true
                            }
                        : slot with
                        {
                            IsPlanReference = true,
                            PlanReferenceText = $"Blocks {FormatLabelList(planLabels)}",
                            PlanReferenceReasonKind = DashboardChampionAvailabilityReason.Selected,
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
                        if (!champion.IsAvailable)
                            return champion with { IsOwnAction = false };

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
                            StatusText = $"Blocked by {FormatLabelList(sourceLabels)}",
                            UnavailableReasonKind = GetSourceBlockerUnavailableReasonKind(sourceLabels)
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

        private static string GetSourceBlockerUnavailableReasonKind(IReadOnlyList<string> sourceLabels)
        {
            if (sourceLabels.Any(label => label.Contains("bans", StringComparison.OrdinalIgnoreCase)))
                return DashboardChampionAvailabilityReason.Banned;

            if (sourceLabels.Any(label => label.Contains("team", StringComparison.OrdinalIgnoreCase)))
                return DashboardChampionAvailabilityReason.Selected;

            return DashboardChampionAvailabilityReason.Blocked;
        }

        private void UpdateTeamSlotList(ItemsControl itemsControl, IReadOnlyList<DashboardTeamSlotItem> slots)
        {
            itemsControl.ItemsSource = slots.Select(CreateTeamSlotViewItem).ToList();
        }

        private void UpdatePlanDisplay(DashboardStatus status)
        {
            PickPlanLockText.ToolTip = GetPlanLockText(status.PickLockText);
            BanPlanLockText.ToolTip = GetPlanLockText(status.BanLockText);
        }

        private static string GetPlanLockText(string lockText)
        {
            return string.IsNullOrWhiteSpace(lockText)
                ? "Lock timing unavailable."
                : lockText;
        }

        private void UpdateChampionPriorityList(ItemsControl itemsControl, TextBlock placeholderText, IReadOnlyList<DashboardChampionPlanItem> champions, string fallbackText)
        {
            itemsControl.ItemsSource = DashboardChampionPlanDisplay.CreateList(champions);

            bool hasChampions = champions.Count > 0;
            placeholderText.Visibility = hasChampions ? Visibility.Collapsed : Visibility.Visible;

            if (hasChampions)
                return;

            placeholderText.Text = string.IsNullOrWhiteSpace(fallbackText)
                ? string.Empty
                : fallbackText;
        }

        private DashboardTeamSlotViewItem CreateTeamSlotViewItem(DashboardTeamSlotItem slot)
        {
            string championName = GetChampionDisplayName(slot.ChampionId, slot.ChampionName);

            return new DashboardTeamSlotViewItem
            {
                ChampionId = slot.ChampionId,
                ChampionName = championName,
                RoleName = slot.RoleName,
                IsLocalPlayer = slot.IsLocalPlayer,
                IsPlanReference = slot.IsPlanReference,
                PlanReferenceText = slot.PlanReferenceText,
                PlanReferenceReasonKind = slot.PlanReferenceReasonKind,
                IsOwnAction = slot.IsOwnAction,
                PortraitImageSource = GetChampionPortrait(slot.ChampionId, championName)
            };
        }

        private ImageSource? GetChampionPortrait(int championId, string championName)
        {
            if (championId > 0)
                return ChampionTileCatalog.GetSelectedImageSource(championId);

            return ChampionCatalog.TryGetByName(championName, out var champion)
                ? ChampionTileCatalog.GetSelectedImageSource(champion!.Id)
                : null;
        }

        private static string GetChampionDisplayName(int championId, string fallbackName)
        {
            if (championId > 0 && ChampionCatalog.TryGetById(championId, out var champion))
                return champion!.Name;

            return string.IsNullOrWhiteSpace(fallbackName)
                ? "No champion"
                : fallbackName;
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueLogRowResize();
        }

        private void QueueLogRowResize()
        {
            Dispatcher.TryInvokeAsync(UpdateLogRowHeight, DispatcherPriority.Loaded);
        }

        private void UpdateLogRowHeight()
        {
            if (ActualHeight <= 0 || DashboardContentHost.ActualHeight <= 0)
                return;

            double availableHeight = ActualHeight
                - DashboardContentHost.ActualHeight
                - 12;
            double targetHeight = Math.Max(MinimumLogRowHeight, availableHeight);

            if (!LogRow.Height.IsAbsolute || Math.Abs(LogRow.Height.Value - targetHeight) > 0.5)
                LogRow.Height = new GridLength(targetHeight);
        }

        private sealed class DashboardTeamSlotViewItem
        {
            public int ChampionId { get; init; }
            public string ChampionName { get; init; } = "No champion";
            public string RoleName { get; init; } = "None";
            public bool IsLocalPlayer { get; init; }
            public bool IsPlanReference { get; init; }
            public string PlanReferenceText { get; init; } = string.Empty;
            public string PlanReferenceReasonKind { get; init; } = DashboardChampionAvailabilityReason.None;
            public bool IsOwnAction { get; init; }
            public ImageSource? PortraitImageSource { get; init; }
        }

    }
}
