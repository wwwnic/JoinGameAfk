using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JoinGameAfk.Tools.MockLeagueClient;

public partial class MainWindow : Window
{
    private readonly MockLeagueClientState _state = new();
    private readonly ObservableCollection<string> _logs = [];
    private readonly ObservableCollection<TeamSlot> _myTeamSlots = [];
    private readonly ObservableCollection<TeamSlot> _theirTeamSlots = [];
    private readonly ObservableCollection<ChampSelectAction> _actions = [];
    private readonly IReadOnlyList<ChampionOption> _champions = ChampionOption.LoadAll();
    private readonly IReadOnlyList<ChampionOption> _selectableChampions;
    private readonly Dictionary<int, ChampionOption> _championsById;
    private readonly ObservableCollection<ChampionChipItem> _myTeamBanChips = [];
    private readonly ObservableCollection<ChampionChipItem> _theirTeamBanChips = [];
    private bool _isRefreshingUi;
    private MockLeagueClientServer? _server;

    public MainWindow()
    {
        _selectableChampions = _champions.Where(champion => champion.ChampionId > 0).ToList();
        _championsById = _champions.ToDictionary(champion => champion.ChampionId);

        InitializeComponent();
        LogList.ItemsSource = _logs;
        DraftStepList.ItemsSource = DraftPickSteps.All;
        DraftMyTeamGrid.ItemsSource = _myTeamSlots;
        DraftTheirTeamGrid.ItemsSource = _theirTeamSlots;
        MyTeamGrid.ItemsSource = _myTeamSlots;
        TheirTeamGrid.ItemsSource = _theirTeamSlots;
        ActionsGrid.ItemsSource = _actions;
        MyTeamBanChipList.ItemsSource = _myTeamBanChips;
        TheirTeamBanChipList.ItemsSource = _theirTeamBanChips;
        ConfigureChampionColumn(DraftMyTeamChampionColumn);
        ConfigureChampionColumn(DraftMyTeamIntentColumn);
        ConfigureChampionColumn(DraftMyTeamBanColumn);
        ConfigureChampionColumn(DraftTheirTeamChampionColumn);
        ConfigureChampionColumn(DraftTheirTeamIntentColumn);
        ConfigureChampionColumn(DraftTheirTeamBanColumn);
        ConfigureChampionColumn(MyTeamChampionColumn);
        ConfigureChampionColumn(MyTeamIntentColumn);
        ConfigureChampionColumn(MyTeamBanColumn);
        ConfigureChampionColumn(TheirTeamChampionColumn);
        ConfigureChampionColumn(TheirTeamIntentColumn);
        ConfigureChampionColumn(TheirTeamBanColumn);
        ConfigureChampionColumn(ActionChampionColumn);
        ConfigureChampionPicker(MyTeamBanPicker);
        ConfigureChampionPicker(TheirTeamBanPicker);
        SetRoleBoxFromValue(LocalPlayerRoleBox, MockLeagueClientRoles.DefaultRole);
        SetQueueModeBoxFromValue(QueueModeBox, MockQueueMode.DraftPick);
        SetLocalPlayerCellBoxFromValue(LocalPlayerCellBox, 1);
        ReadyStateBox.SelectedIndex = 0;
        ReadyResponseBox.SelectedIndex = 0;
        TimerPhaseBox.SelectedIndex = 1;
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
        _state.UpdateQueueMode(MockQueueMode.DraftPick);
        _state.ResetGuidedChampSelect();
        AddLog("Draft Pick state reset.");
        await EmitSnapshotIfRunningAsync();
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

    private async void AddMyTeamBanButton_Click(object sender, RoutedEventArgs e)
    {
        await AddBanChampionAsync(MyTeamBanPicker, _myTeamBanChips, "your team");
    }

    private async void AddTheirTeamBanButton_Click(object sender, RoutedEventArgs e)
    {
        await AddBanChampionAsync(TheirTeamBanPicker, _theirTeamBanChips, "enemy team");
    }

    private async void RemoveBanChampionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ChampionChipItem item)
            return;

        bool removed = _myTeamBanChips.Remove(item) || _theirTeamBanChips.Remove(item);
        if (!removed)
            return;

        await ApplyChampionSelectStateAsync($"Removed ban {item.DisplayName}.");
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
        await EmitSnapshotIfRunningAsync();
    }

    private void CommitChampionSelectEditorsToState()
    {
        CommitGridEdits(DraftMyTeamGrid);
        CommitGridEdits(DraftTheirTeamGrid);
        CommitGridEdits(MyTeamGrid);
        CommitGridEdits(TheirTeamGrid);
        CommitGridEdits(ActionsGrid);
        ApplyLocalPlayerRoleToGrid();

        if (!int.TryParse(TimeLeftBox.Text.Trim(), out int timeLeftSeconds))
            timeLeftSeconds = 0;

        var myTeamBanIds = _myTeamBanChips
            .Select(champion => champion.ChampionId)
            .Concat(_myTeamSlots.Select(slot => slot.BanChampionId));
        var theirTeamBanIds = _theirTeamBanChips
            .Select(champion => champion.ChampionId)
            .Concat(_theirTeamSlots.Select(slot => slot.BanChampionId));

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

    private async Task AddBanChampionAsync(
        ComboBox picker,
        ObservableCollection<ChampionChipItem> target,
        string teamLabel)
    {
        if (picker.SelectedItem is not ChampionOption { ChampionId: > 0 } champion)
        {
            AddLog($"Select a champion before adding a {teamLabel} ban.");
            return;
        }

        if (target.Any(item => item.ChampionId == champion.ChampionId))
        {
            AddLog($"{champion.DisplayName} is already in {teamLabel} bans.");
            return;
        }

        target.Add(new ChampionChipItem(champion));
        await ApplyChampionSelectStateAsync($"Added {champion.DisplayName} to {teamLabel} bans.");
    }

    private async Task EmitSnapshotIfRunningAsync()
    {
        if (_server is null)
        {
            AddLog("State updated. Start the server to emit websocket events.");
            return;
        }

        await _server.EmitSnapshotAsync();
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
            EnemyBanRevealText.Text = snapshot.EnemyBansRevealed
                ? "Red bans are revealed in emitted LCU payloads."
                : "Red bans are staged locally and hidden from emitted LCU payloads.";
            ReplaceCollection(_myTeamBanChips, CreateChampionChips(snapshot.MyTeamBans));
            ReplaceCollection(_theirTeamBanChips, CreateChampionChips(snapshot.TheirTeamBans));
            ReplaceCollection(_myTeamSlots, snapshot.MyTeam);
            ReplaceCollection(_theirTeamSlots, snapshot.TheirTeam);
            ReplaceCollection(_actions, snapshot.Actions);
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

    private void ConfigureChampionPicker(ComboBox picker)
    {
        picker.ItemsSource = _selectableChampions;
        picker.SelectedValuePath = nameof(ChampionOption.ChampionId);
        picker.SelectedIndex = 0;
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

    private IReadOnlyList<ChampionChipItem> CreateChampionChips(IEnumerable<int> championIds)
    {
        return championIds
            .Where(championId => championId > 0)
            .Distinct()
            .Select(championId => new ChampionChipItem(GetChampionOption(championId)))
            .ToList();
    }

    private ChampionOption GetChampionOption(int championId)
    {
        return _championsById.TryGetValue(championId, out var champion)
            ? champion
            : new ChampionOption(championId, $"Champion {championId}");
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
