using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JoinGameAfk.Tools.MockLeagueClient;

public partial class MainWindow : Window
{
    private readonly MockLeagueClientState _state = new();
    private readonly ObservableCollection<string> _logs = [];
    private readonly ObservableCollection<TeamSlot> _myTeamSlots = [];
    private readonly ObservableCollection<TeamSlot> _theirTeamSlots = [];
    private readonly ObservableCollection<ChampSelectAction> _actions = [];
    private readonly ObservableCollection<TimedActionPlan> _timedActionPlans = [];
    private readonly IReadOnlyList<ChampionOption> _champions = ChampionOption.LoadAll();
    private readonly DispatcherTimer _draftCountdownTimer = new();
    private MockLeagueClientSnapshot? _draftPlaybackEditSnapshot;
    private bool _isRefreshingUi;
    private bool _isDraftCountdownTicking;
    private MockLeagueClientServer? _server;

    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = _logs;
        DraftStepList.ItemsSource = DraftPickSteps.All;
        DraftMyTeamGrid.ItemsSource = _myTeamSlots;
        DraftTheirTeamGrid.ItemsSource = _theirTeamSlots;
        ActionsGrid.ItemsSource = _timedActionPlans;
        ConfigureChampionColumn(DraftMyTeamChampionColumn);
        ConfigureChampionColumn(DraftMyTeamIntentColumn);
        ConfigureChampionColumn(DraftMyTeamBanColumn);
        ConfigureChampionColumn(DraftTheirTeamChampionColumn);
        ConfigureChampionColumn(DraftTheirTeamIntentColumn);
        ConfigureChampionColumn(DraftTheirTeamBanColumn);
        ConfigureChampionColumn(ActionPickChampionColumn);
        ConfigureChampionColumn(ActionBanChampionColumn);
        SetRoleBoxFromValue(LocalPlayerRoleBox, MockLeagueClientRoles.DefaultRole);
        SetQueueModeBoxFromValue(QueueModeBox, MockQueueMode.DraftPick);
        SetLocalPlayerCellBoxFromValue(LocalPlayerCellBox, 1);
        ReadyStateBox.SelectedIndex = 0;
        ReadyResponseBox.SelectedIndex = 0;
        TimerPhaseBox.SelectedIndex = 1;
        _draftCountdownTimer.Interval = TimeSpan.FromSeconds(1);
        _draftCountdownTimer.Tick += DraftCountdownTimer_Tick;
        _state.Changed += State_Changed;
        RefreshUiFromState();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server is not null)
            return;

        if (!int.TryParse(PortBox.Text.Trim(), out int port) || port is <= 0 or > 65535)
        {
            ServerStatusText.Text = "Enter a valid TCP port.";
            return;
        }

        string token = string.IsNullOrWhiteSpace(TokenBox.Text)
            ? "mock-league-client"
            : TokenBox.Text.Trim();

        _server = new MockLeagueClientServer(_state, port, token, AddLog);
        try
        {
            await _server.StartAsync();
            await _server.EmitSnapshotAsync();
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            PortBox.IsEnabled = false;
            TokenBox.IsEnabled = false;
            ServerStatusText.Text = $"Running on https://127.0.0.1:{port}/";
        }
        catch (Exception ex)
        {
            await _server.DisposeAsync();
            _server = null;
            ServerStatusText.Text = FormatStartupError(ex);
            AddLog(ServerStatusText.Text);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopServerAsync();
    }

    private void CopyEnvButton_Click(object sender, RoutedEventArgs e)
    {
        string port = string.IsNullOrWhiteSpace(PortBox.Text) ? "2999" : PortBox.Text.Trim();
        string token = string.IsNullOrWhiteSpace(TokenBox.Text) ? "mock-league-client" : TokenBox.Text.Trim();
        string snippet = $"$env:JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_PORT=\"{port}\"\r\n$env:JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_TOKEN=\"{token}\"";

        try
        {
            Clipboard.SetText(snippet);
            AddLog("Copied Debug environment variable snippet to clipboard.");
        }
        catch (Exception ex)
        {
            AddLog($"Clipboard failed: {ex.Message}");
        }
    }

    private void LaunchAppButton_Click(object sender, RoutedEventArgs e)
    {
        string appPath = GetDebugAppPath();
        if (!File.Exists(appPath))
        {
            AddLog($"JoinGameAfk Debug executable was not found at {appPath}. Build the Debug solution first.");
            return;
        }

        string port = string.IsNullOrWhiteSpace(PortBox.Text) ? "2999" : PortBox.Text.Trim();
        string token = string.IsNullOrWhiteSpace(TokenBox.Text) ? "mock-league-client" : TokenBox.Text.Trim();

        var startInfo = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath) ?? AppContext.BaseDirectory
        };
        startInfo.Environment["JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_PORT"] = port;
        startInfo.Environment["JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_TOKEN"] = token;

        try
        {
            Process.Start(startInfo);
            AddLog($"Launched JoinGameAfk with mock env on port {port}.");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to launch JoinGameAfk: {ex.Message}");
        }
    }

    private async void ScenarioButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string scenarioName
            || !Enum.TryParse(scenarioName, out MockLeagueClientScenario scenario))
        {
            return;
        }

        string localPlayerRole = GetRoleBoxValue(LocalPlayerRoleBox);
        _state.ApplyScenario(scenario, localPlayerRole);
        AddLog($"Scenario applied: {scenario}.");
        if (scenario == MockLeagueClientScenario.ClientOffline)
        {
            await StopServerAsync();
            return;
        }

        await EmitSnapshotIfRunningAsync();
    }

    private async void QueueModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingUi || !IsLoaded)
            return;

        var queueMode = GetQueueModeBoxValue(QueueModeBox);
        _state.UpdateQueueMode(queueMode);
        AddLog($"Queue mode applied: {FormatQueueMode(queueMode)}.");
        await EmitSnapshotIfRunningAsync();
    }

    private async void LocalPlayerCellBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingUi || !IsLoaded)
            return;

        int localPlayerCellId = GetLocalPlayerCellBoxValue(LocalPlayerCellBox);
        _state.UpdateLocalPlayerCellId(localPlayerCellId);
        AddLog($"Local player slot applied: Blue {localPlayerCellId}.");
        await EmitSnapshotIfRunningAsync();
    }

    private async void DraftStepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingUi || !IsLoaded)
            return;

        if (DraftStepList.SelectedItem is not DraftPickStepOption option)
            return;

        CommitChampionSelectEditorsToState();
        _state.UpdateDraftStep(option.Step);
        AddLog($"Draft step applied: {option.DisplayName}.");
        await EmitSnapshotIfRunningAsync();
    }

    private async void NormalDraftButton_Click(object sender, RoutedEventArgs e)
    {
        SetQueueModeBoxSilently(MockQueueMode.DraftPick);
        _state.UpdateQueueMode(MockQueueMode.DraftPick);
        QueueIdBox.Text = "400";
        QueueNameBox.Text = "Normal Draft";
        await ApplyQueueAsync();
    }

    private async void BlindPickButton_Click(object sender, RoutedEventArgs e)
    {
        SetQueueModeBoxSilently(MockQueueMode.BlindPick);
        _state.UpdateQueueMode(MockQueueMode.BlindPick);
        QueueIdBox.Text = "430";
        QueueNameBox.Text = "Blind Pick";
        await ApplyQueueAsync();
    }

    private async void QuickplayButton_Click(object sender, RoutedEventArgs e)
    {
        QueueIdBox.Text = "490";
        QueueNameBox.Text = "Quickplay";
        await ApplyQueueAsync();
    }

    private async void ApplyQueueButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyQueueAsync();
    }

    private async void ApplyReadyButton_Click(object sender, RoutedEventArgs e)
    {
        _state.UpdateReadyCheck(GetComboText(ReadyStateBox), GetComboText(ReadyResponseBox));
        AddLog("Ready check state applied.");
        await EmitSnapshotIfRunningAsync();
    }

    private async void ApplyChampSelectButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyChampionSelectStateAsync("Champion select state applied.");
    }

    private async void EmitStateButton_Click(object sender, RoutedEventArgs e)
    {
        CommitChampionSelectEditorsToState();
        await EmitSnapshotIfRunningAsync();
    }

    private async void DraftBackButton_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _state.GetSnapshot();
        await ApplyDraftStepAsync(DraftPickSteps.Previous(snapshot.DraftStep));
    }

    private async void DraftNextButton_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _state.GetSnapshot();
        await ApplyDraftStepAsync(DraftPickSteps.Next(snapshot.DraftStep));
    }

    private async void DraftResetButton_Click(object sender, RoutedEventArgs e)
    {
        StopDraftCountdown();
        _draftPlaybackEditSnapshot = null;
        _state.UpdateQueueMode(MockQueueMode.DraftPick);
        _state.ResetGuidedChampSelect();
        AddLog("Draft Pick state reset.");
        await EmitSnapshotIfRunningAsync();
    }

    private async void DraftStartTimerButton_Click(object sender, RoutedEventArgs e)
    {
        CommitChampionSelectEditorsToState();
        bool isNewPlayback = _draftPlaybackEditSnapshot is null;
        _draftPlaybackEditSnapshot ??= _state.GetSnapshot();

        if (!_state.BeginChampionSelectPlayback())
        {
            if (isNewPlayback)
                _draftPlaybackEditSnapshot = null;

            DraftTimerStatusText.Text = "Playback stopped";
            AddLog("Draft playback not started. Select a draft step with time left above 0s.");
            return;
        }

        var dueResult = _state.ApplyDueScheduledActions();
        foreach (string appliedAction in dueResult.AppliedActions)
            AddLog(appliedAction);

        _draftCountdownTimer.Start();
        DraftTimerStatusText.Text = "Countdown running";
        AddLog(isNewPlayback ? "Draft playback started." : "Draft playback resumed.");
        await EmitChampSelectSessionIfRunningAsync();
    }

    private void DraftPauseTimerButton_Click(object sender, RoutedEventArgs e)
    {
        StopDraftCountdown("Draft countdown paused.");
    }

    private async void DraftStopTimerButton_Click(object sender, RoutedEventArgs e)
    {
        await StopDraftPlaybackAsync("Draft playback stopped. Restored edit state.");
    }

    private async void DraftCountdownTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDraftCountdownTicking)
            return;

        _isDraftCountdownTicking = true;
        try
        {
            var result = _state.TickChampionSelectTimer();
            if (!result.StateChanged)
            {
                await StopDraftPlaybackAsync("Draft playback stopped. Restored edit state.");
                return;
            }

            foreach (string appliedAction in result.AppliedActions)
                AddLog(appliedAction);

            if (result.AppliedActions.Count > 0)
                await EmitChampSelectSessionIfRunningAsync(logWhenStopped: false);

            if (result.TimeLeftSeconds <= 0)
            {
                await StopDraftPlaybackAsync("Draft countdown reached 0s. Restored edit state.");
            }
        }
        finally
        {
            _isDraftCountdownTicking = false;
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _logs.Clear();
    }

    private async void LocalPlayerRoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingUi || !IsLoaded)
            return;

        string role = GetRoleBoxValue(LocalPlayerRoleBox);
        _state.UpdateLocalPlayerRole(role);
        AddLog($"Local player role applied: {MockLeagueClientRoles.ToDisplayName(role)}.");
        await EmitSnapshotIfRunningAsync();
    }

    private async Task ApplyQueueAsync()
    {
        if (!int.TryParse(QueueIdBox.Text.Trim(), out int queueId))
            queueId = 0;

        _state.UpdateQueue(queueId, QueueNameBox.Text);
        AddLog($"Queue applied: {queueId} {QueueNameBox.Text.Trim()}.");
        await EmitSnapshotIfRunningAsync();
    }

    private async Task ApplyChampionSelectStateAsync(string logMessage)
    {
        CommitChampionSelectEditorsToState();
        AddLog(logMessage);
        await EmitChampSelectSessionIfRunningAsync();
    }

    private void CommitChampionSelectEditorsToState()
    {
        CommitGridEdits(DraftMyTeamGrid);
        CommitGridEdits(DraftTheirTeamGrid);
        CommitGridEdits(ActionsGrid);
        ApplyLocalPlayerRoleToGrid();
        ApplyTimedActionPlansToActions();

        if (!int.TryParse(TimeLeftBox.Text.Trim(), out int timeLeftSeconds))
            timeLeftSeconds = 0;

        var myTeamBanIds = _myTeamSlots.Select(slot => slot.BanChampionId);
        var theirTeamBanIds = _theirTeamSlots.Select(slot => slot.BanChampionId);

        _state.UpdateChampionSelect(
            GetComboText(TimerPhaseBox),
            timeLeftSeconds,
            _myTeamSlots,
            _theirTeamSlots,
            myTeamBanIds,
            theirTeamBanIds,
            _actions);
    }

    private static void CommitGridEdits(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private async Task ApplyDraftStepAsync(DraftPickStep step)
    {
        CommitChampionSelectEditorsToState();
        _state.UpdateDraftStep(step);
        AddLog($"Draft step applied: {DraftPickSteps.GetDisplayName(step)}.");
        await EmitSnapshotIfRunningAsync();
    }

    private async Task EmitSnapshotIfRunningAsync(bool logWhenStopped = true)
    {
        if (_server is null)
        {
            if (logWhenStopped)
                AddLog("State updated. Start the server to emit websocket events.");

            return;
        }

        await _server.EmitSnapshotAsync();
    }

    private async Task EmitChampSelectSessionIfRunningAsync(bool logWhenStopped = true)
    {
        if (_server is null)
        {
            if (logWhenStopped)
                AddLog("State updated. Start the server to emit websocket events.");

            return;
        }

        await _server.EmitChampSelectSessionAsync();
    }

    private void State_Changed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshUiFromState);
    }

    private void RefreshUiFromState()
    {
        _isRefreshingUi = true;
        try
        {
            var snapshot = _state.GetSnapshot();
            CurrentPhaseText.Text = snapshot.Phase.ToString();
            CurrentTimerText.Text = $"{snapshot.TimerPhase} / {snapshot.TimeLeftSeconds}s";
            DraftTimerStatusText.Text = _draftCountdownTimer.IsEnabled
                ? "Countdown running"
                : _draftPlaybackEditSnapshot is not null
                    ? "Playback paused"
                : snapshot.TimeLeftSeconds == 0
                    ? "Timer at 0s"
                    : "Countdown paused";
            CurrentQueueText.Text = $"{snapshot.QueueName} ({snapshot.QueueId})";
            QueueIdBox.Text = snapshot.QueueId.ToString();
            QueueNameBox.Text = snapshot.QueueName;
            SetQueueModeBoxFromValue(QueueModeBox, snapshot.QueueMode);
            SetLocalPlayerCellBoxFromValue(LocalPlayerCellBox, snapshot.LocalPlayerCellId);
            SetSelectedDraftStep(snapshot.DraftStep);
            SetComboText(ReadyStateBox, snapshot.ReadyCheckState);
            SetComboText(ReadyResponseBox, snapshot.ReadyCheckResponse);
            SetComboText(TimerPhaseBox, snapshot.TimerPhase);
            TimeLeftBox.Text = snapshot.TimeLeftSeconds.ToString();
            SetRoleBoxFromValue(
                LocalPlayerRoleBox,
                snapshot.MyTeam.FirstOrDefault(slot => slot.CellId == snapshot.LocalPlayerCellId)?.AssignedPosition
                ?? MockLeagueClientRoles.DefaultRole);
            DraftStepStatusText.Text = $"{DraftPickSteps.GetDisplayName(snapshot.DraftStep)} / Local Blue {snapshot.LocalPlayerCellId}";
            ReplaceCollection(_myTeamSlots, snapshot.MyTeam);
            ReplaceCollection(_theirTeamSlots, snapshot.TheirTeam);
            ReplaceCollection(_actions, snapshot.Actions);
            UpdateTimedActionColumnVisibility(snapshot);
            ReplaceCollection(_timedActionPlans, CreateTimedActionPlans(snapshot));
        }
        finally
        {
            _isRefreshingUi = false;
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private static string GetComboText(ComboBox comboBox)
    {
        if (!string.IsNullOrWhiteSpace(comboBox.Text))
            return comboBox.Text.Trim();

        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? string.Empty
            : string.Empty;
    }

    private static void SetComboText(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                comboBox.Text = value;
                return;
            }
        }

        comboBox.SelectedItem = null;
        comboBox.Text = value;
    }

    private void ConfigureChampionColumn(DataGridComboBoxColumn column)
    {
        column.ItemsSource = _champions;

        if (FindResource("ChampionComboBoxStyle") is Style championComboBoxStyle)
        {
            column.ElementStyle = CreateChampionColumnComboBoxStyle(championComboBoxStyle, isReadOnly: true);
            column.EditingElementStyle = CreateChampionColumnComboBoxStyle(championComboBoxStyle, isReadOnly: false);
        }
    }

    private static IReadOnlyList<TimedActionPlan> CreateTimedActionPlans(MockLeagueClientSnapshot snapshot)
    {
        var actionList = snapshot.Actions.ToList();
        var mode = GetTimedActionMode(snapshot);
        var visibleCellIds = GetTimedActionCellIds(snapshot, mode);

        return visibleCellIds
            .Order()
            .Select(cellId =>
            {
                var pickAction = mode is TimedActionMode.Pick or TimedActionMode.PlanningPickHover
                    ? FindAction(actionList, cellId, "pick")
                    : null;
                var banAction = mode == TimedActionMode.Ban
                    ? FindAction(actionList, cellId, "ban")
                    : null;

                return new TimedActionPlan
                {
                    CellId = cellId,
                    PickActionId = pickAction?.Id,
                    PickChampionId = pickAction?.TargetChampionId ?? 0,
                    PickHoverAtSeconds = pickAction?.HoverAtSeconds,
                    PickLockAtSeconds = pickAction?.LockAtSeconds,
                    BanActionId = banAction?.Id,
                    BanChampionId = banAction?.TargetChampionId ?? 0,
                    BanHoverAtSeconds = banAction?.HoverAtSeconds,
                    BanLockAtSeconds = banAction?.LockAtSeconds
                };
            })
            .ToList();
    }

    private void UpdateTimedActionColumnVisibility(MockLeagueClientSnapshot snapshot)
    {
        var mode = GetTimedActionMode(snapshot);
        bool showPick = mode is TimedActionMode.Pick or TimedActionMode.PlanningPickHover;
        bool showPickLock = mode == TimedActionMode.Pick;
        bool showBan = mode == TimedActionMode.Ban;

        ActionPickChampionColumn.Visibility = showPick ? Visibility.Visible : Visibility.Collapsed;
        ActionPickHoverColumn.Visibility = showPick ? Visibility.Visible : Visibility.Collapsed;
        ActionPickLockColumn.Visibility = showPickLock ? Visibility.Visible : Visibility.Collapsed;
        ActionBanChampionColumn.Visibility = showBan ? Visibility.Visible : Visibility.Collapsed;
        ActionBanHoverColumn.Visibility = showBan ? Visibility.Visible : Visibility.Collapsed;
        ActionBanLockColumn.Visibility = showBan ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TimedActionMode GetTimedActionMode(MockLeagueClientSnapshot snapshot)
    {
        if (snapshot.Actions.Any(action => action.IsInProgress && IsActionType(action, "ban")))
            return TimedActionMode.Ban;

        if (snapshot.Actions.Any(action => action.IsInProgress && IsActionType(action, "pick")))
            return TimedActionMode.Pick;

        if (string.Equals(snapshot.TimerPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
            return TimedActionMode.PlanningPickHover;

        if (string.Equals(snapshot.TimerPhase, "FINALIZATION", StringComparison.OrdinalIgnoreCase))
            return TimedActionMode.Finalization;

        return TimedActionMode.None;
    }

    private static HashSet<int> GetTimedActionCellIds(MockLeagueClientSnapshot snapshot, TimedActionMode mode)
    {
        return mode switch
        {
            TimedActionMode.Ban => snapshot.Actions
                .Where(action => action.IsInProgress && IsActionType(action, "ban"))
                .Select(action => action.ActorCellId)
                .ToHashSet(),
            TimedActionMode.Pick => snapshot.Actions
                .Where(action => action.IsInProgress && IsActionType(action, "pick"))
                .Select(action => action.ActorCellId)
                .ToHashSet(),
            TimedActionMode.PlanningPickHover or TimedActionMode.Finalization => GetAllTeamCellIds(snapshot),
            _ => []
        };
    }

    private static HashSet<int> GetAllTeamCellIds(MockLeagueClientSnapshot snapshot)
    {
        return snapshot.MyTeam
            .Concat(snapshot.TheirTeam)
            .Select(slot => slot.CellId)
            .Where(cellId => cellId > 0)
            .ToHashSet();
    }

    private void ApplyTimedActionPlansToActions()
    {
        foreach (var plan in _timedActionPlans)
        {
            if (plan.PickActionId.HasValue)
                ApplyTimedActionPlanToAction(plan.PickActionId, plan.CellId, "pick", plan.PickChampionId, plan.PickHoverAtSeconds, plan.PickLockAtSeconds);

            if (plan.BanActionId.HasValue)
                ApplyTimedActionPlanToAction(plan.BanActionId, plan.CellId, "ban", plan.BanChampionId, plan.BanHoverAtSeconds, plan.BanLockAtSeconds);
        }
    }

    private void ApplyTimedActionPlanToAction(
        int? actionId,
        int cellId,
        string type,
        int championId,
        int? hoverAtSeconds,
        int? lockAtSeconds)
    {
        var action = actionId is int id
            ? _actions.FirstOrDefault(candidate => candidate.Id == id)
            : null;

        action ??= FindAction(_actions, cellId, type);
        if (action is null)
            return;

        action.TargetChampionId = Math.Max(0, championId);
        action.HoverAtSeconds = hoverAtSeconds is int hoverSeconds ? Math.Max(0, hoverSeconds) : null;
        action.LockAtSeconds = lockAtSeconds is int lockSeconds ? Math.Max(0, lockSeconds) : null;
    }

    private static ChampSelectAction? FindAction(IEnumerable<ChampSelectAction> actions, int cellId, string type)
    {
        return actions.FirstOrDefault(action =>
            action.ActorCellId == cellId
            && IsActionType(action, type));
    }

    private static bool IsActionType(ChampSelectAction action, string type)
    {
        return string.Equals(action.Type, type, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyLocalPlayerRoleToGrid()
    {
        int localPlayerCellId = GetLocalPlayerCellBoxValue(LocalPlayerCellBox);
        var localPlayer = _myTeamSlots.FirstOrDefault(slot => slot.CellId == localPlayerCellId);
        if (localPlayer is not null)
            localPlayer.AssignedPosition = GetRoleBoxValue(LocalPlayerRoleBox);
    }

    private static MockQueueMode GetQueueModeBoxValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string mode }
               && Enum.TryParse(mode, out MockQueueMode queueMode)
            ? queueMode
            : MockQueueMode.DraftPick;
    }

    private static void SetQueueModeBoxFromValue(ComboBox comboBox, MockQueueMode queueMode)
    {
        string queueModeText = queueMode.ToString();
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string itemMode
                && string.Equals(itemMode, queueModeText, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void SetQueueModeBoxSilently(MockQueueMode queueMode)
    {
        bool wasRefreshing = _isRefreshingUi;
        _isRefreshingUi = true;
        try
        {
            SetQueueModeBoxFromValue(QueueModeBox, queueMode);
        }
        finally
        {
            _isRefreshingUi = wasRefreshing;
        }
    }

    private static int GetLocalPlayerCellBoxValue(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem { Tag: string cellText }
            && int.TryParse(cellText, out int cellId))
        {
            return Math.Clamp(cellId, 1, 5);
        }

        return 1;
    }

    private static void SetLocalPlayerCellBoxFromValue(ComboBox comboBox, int localPlayerCellId)
    {
        string cellText = Math.Clamp(localPlayerCellId, 1, 5).ToString();
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string itemCellText
                && string.Equals(itemCellText, cellText, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void SetSelectedDraftStep(DraftPickStep step)
    {
        foreach (var item in DraftStepList.Items.OfType<DraftPickStepOption>())
        {
            if (item.Step == step)
            {
                DraftStepList.SelectedItem = item;
                DraftStepList.ScrollIntoView(item);
                return;
            }
        }

        DraftStepList.SelectedIndex = 0;
    }

    private static string FormatQueueMode(MockQueueMode queueMode)
    {
        return queueMode switch
        {
            MockQueueMode.BlindPick => "Blind Pick",
            _ => "Draft Pick"
        };
    }

    private static string GetRoleBoxValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string role }
            ? MockLeagueClientRoles.NormalizeDisplayRole(role)
            : MockLeagueClientRoles.DefaultRole;
    }

    private static void SetRoleBoxFromValue(ComboBox comboBox, string role)
    {
        string normalizedRole = MockLeagueClientRoles.NormalizeDisplayRole(role);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string itemRole
                && string.Equals(
                    MockLeagueClientRoles.NormalizeDisplayRole(itemRole),
                    normalizedRole,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static Style CreateChampionColumnComboBoxStyle(Style baseStyle, bool isReadOnly)
    {
        var style = new Style(typeof(ComboBox), baseStyle);

        if (isReadOnly)
        {
            style.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
            style.Setters.Add(new Setter(Control.FocusableProperty, false));
        }

        return style;
    }

    private void AddLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddLog(message));
            return;
        }

        _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_logs.Count > 500)
            _logs.RemoveAt(0);

        if (_logs.Count > 0)
            LogList.ScrollIntoView(_logs[^1]);
    }

    private void StopDraftCountdown(string? logMessage = null)
    {
        if (_draftCountdownTimer.IsEnabled)
            _draftCountdownTimer.Stop();

        DraftTimerStatusText.Text = "Countdown paused";

        if (!string.IsNullOrWhiteSpace(logMessage))
            AddLog(logMessage);
    }

    private async Task StopDraftPlaybackAsync(string logMessage)
    {
        if (_draftCountdownTimer.IsEnabled)
            _draftCountdownTimer.Stop();

        if (_draftPlaybackEditSnapshot is { } editSnapshot)
        {
            _draftPlaybackEditSnapshot = null;
            _state.RestoreSnapshot(editSnapshot);
            DraftTimerStatusText.Text = "Playback stopped";
            AddLog(logMessage);
            await EmitChampSelectSessionIfRunningAsync(logWhenStopped: false);
            return;
        }

        DraftTimerStatusText.Text = "Playback stopped";
        AddLog("Draft playback stopped.");
    }

    private async Task StopServerAsync()
    {
        if (_server is null)
            return;

        await _server.DisposeAsync();
        _server = null;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        PortBox.IsEnabled = true;
        TokenBox.IsEnabled = true;
        ServerStatusText.Text = "Stopped";
    }

    protected override async void OnClosed(EventArgs e)
    {
        _draftCountdownTimer.Stop();
        _draftCountdownTimer.Tick -= DraftCountdownTimer_Tick;
        _state.Changed -= State_Changed;
        await StopServerAsync();
        base.OnClosed(e);
    }

    private static string FormatStartupError(Exception ex)
    {
        return "Failed to start HTTPS mock server. "
            + "If this is a development certificate issue, run `dotnet dev-certs https --trust`, then start the tool again. "
            + ex.Message;
    }

    private static string GetDebugAppPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "JoinGameAfk",
            "bin",
            "Debug",
            "net10.0-windows",
            "JoinGameAfk.exe"));
    }
}

internal sealed class TimedActionPlan
{
    public int CellId { get; set; }
    public int? PickActionId { get; set; }
    public int PickChampionId { get; set; }
    public int? PickHoverAtSeconds { get; set; }
    public int? PickLockAtSeconds { get; set; }
    public int? BanActionId { get; set; }
    public int BanChampionId { get; set; }
    public int? BanHoverAtSeconds { get; set; }
    public int? BanLockAtSeconds { get; set; }
}

internal enum TimedActionMode
{
    None,
    PlanningPickHover,
    Ban,
    Pick,
    Finalization
}
