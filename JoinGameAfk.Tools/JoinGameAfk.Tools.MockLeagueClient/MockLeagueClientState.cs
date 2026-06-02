namespace JoinGameAfk.Tools.MockLeagueClient;

internal enum MockClientPhase
{
    Unknown,
    Lobby,
    Matchmaking,
    ReadyCheck,
    Planning,
    ChampSelect,
    InGame
}

internal enum MockLeagueClientScenario
{
    ClientOffline,
    Lobby,
    Matchmaking,
    ReadyCheck,
    Planning,
    Ban,
    Pick,
    Finalization,
    InGame,
    UnsupportedQuickplay
}

internal sealed class MockLeagueClientState
{
    private static readonly int[] OwnedChampionIds =
    [
        1, 12, 22, 24, 25, 40, 51, 53, 64, 81, 86, 89, 99, 103, 122, 157, 202, 222, 412, 517
    ];

    private static readonly IReadOnlyList<IReadOnlyList<int>> DraftPickGroups =
    [
        [1],
        [6, 7],
        [2, 3],
        [8, 9],
        [4, 5],
        [10]
    ];

    private readonly object _lock = new();
    private MockClientPhase _phase = MockClientPhase.Unknown;
    private MockQueueMode _queueMode = MockQueueMode.DraftPick;
    private DraftPickStep _draftStep = DraftPickStep.Planning;
    private int _queueId = 400;
    private string _queueName = "Normal Draft";
    private string _readyCheckState = "InProgress";
    private string _readyCheckResponse = "None";
    private string _timerPhase = "PLANNING";
    private int _timeLeftSeconds = 30;
    private string _sessionId = "mock-champ-select-1";
    private int _localPlayerCellId = 1;
    private string _localPlayerAssignedPosition = MockLeagueClientRoles.DefaultRole;
    private readonly List<TeamSlot> _myTeam = [];
    private readonly List<TeamSlot> _theirTeam = [];
    private readonly List<int> _myTeamBans = [];
    private readonly List<int> _theirTeamBans = [];
    private readonly List<ChampSelectAction> _actions = [];
    private readonly Dictionary<DraftPickStep, DraftStepState> _draftStepStates = [];

    public event EventHandler? Changed;

    public MockLeagueClientState()
    {
        ResetDraftPickCore();
        _phase = MockClientPhase.Unknown;
    }

    public MockLeagueClientSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new MockLeagueClientSnapshot(
                _phase,
                _queueMode,
                _draftStep,
                _localPlayerCellId,
                AreEnemyBansRevealedCore(),
                _queueId,
                _queueName,
                _readyCheckState,
                _readyCheckResponse,
                _timerPhase,
                _timeLeftSeconds,
                _myTeam.Select(slot => slot.Clone()).ToList(),
                _theirTeam.Select(slot => slot.Clone()).ToList(),
                _myTeamBans.ToList(),
                _theirTeamBans.ToList(),
                _actions.Select(action => action.Clone()).ToList());
        }
    }

    public void ApplyScenario(MockLeagueClientScenario scenario, string? localPlayerAssignedPosition = null)
    {
        lock (_lock)
        {
            _localPlayerAssignedPosition = MockLeagueClientRoles.NormalizeDisplayRole(
                localPlayerAssignedPosition ?? _localPlayerAssignedPosition);
            _readyCheckState = "InProgress";
            _readyCheckResponse = "None";

            switch (scenario)
            {
                case MockLeagueClientScenario.ClientOffline:
                    _phase = MockClientPhase.Unknown;
                    _timerPhase = "BAN_PICK";
                    _timeLeftSeconds = 0;
                    break;

                case MockLeagueClientScenario.Lobby:
                    ResetQueueModeCore(resetSession: false);
                    _phase = MockClientPhase.Lobby;
                    break;

                case MockLeagueClientScenario.Matchmaking:
                    ResetQueueModeCore(resetSession: false);
                    _phase = MockClientPhase.Matchmaking;
                    break;

                case MockLeagueClientScenario.ReadyCheck:
                    ResetQueueModeCore(resetSession: false);
                    _phase = MockClientPhase.ReadyCheck;
                    break;

                case MockLeagueClientScenario.Planning:
                    ApplyPlanningScenarioCore();
                    break;

                case MockLeagueClientScenario.Ban:
                    ApplyBanScenarioCore();
                    break;

                case MockLeagueClientScenario.Pick:
                    ApplyPickScenarioCore();
                    break;

                case MockLeagueClientScenario.Finalization:
                    ApplyFinalizationScenarioCore();
                    break;

                case MockLeagueClientScenario.InGame:
                    ApplyInGameScenarioCore();
                    break;

                case MockLeagueClientScenario.UnsupportedQuickplay:
                    ResetBlindPickCore();
                    _phase = MockClientPhase.ChampSelect;
                    SetQueueCore(490, "Quickplay");
                    _timerPhase = "BAN_PICK";
                    _timeLeftSeconds = 25;
                    SetActionProgress("pick", isInProgress: true);
                    break;
            }
        }

        OnChanged();
    }

    public void UpdateQueueMode(MockQueueMode queueMode)
    {
        lock (_lock)
        {
            if (_queueMode == queueMode)
                return;

            _queueMode = queueMode;
            ResetQueueModeCore(resetSession: true);
        }

        OnChanged();
    }

    public void ResetGuidedChampSelect()
    {
        lock (_lock)
        {
            ResetQueueModeCore(resetSession: true);
        }

        OnChanged();
    }

    public void UpdateDraftStep(DraftPickStep step)
    {
        lock (_lock)
        {
            EnsureDraftModeCore();
            SwitchDraftStepCore(step);
        }

        OnChanged();
    }

    public void UpdateLocalPlayerCellId(int localPlayerCellId)
    {
        lock (_lock)
        {
            _localPlayerCellId = Math.Clamp(localPlayerCellId, 1, 5);
            var localPlayer = FindTeamSlot(_localPlayerCellId);
            _localPlayerAssignedPosition = localPlayer?.AssignedPosition ?? _localPlayerAssignedPosition;

            if (_queueMode == MockQueueMode.BlindPick)
                EnsureBlindPickActionActorsCore();
        }

        OnChanged();
    }

    public void UpdateQueue(int queueId, string queueName)
    {
        lock (_lock)
        {
            var previousQueueMode = _queueMode;
            if (queueId == 430)
                _queueMode = MockQueueMode.BlindPick;
            else if (queueId is 400 or 420 or 440 or 3110)
                _queueMode = MockQueueMode.DraftPick;

            if (previousQueueMode != _queueMode)
                ResetQueueModeCore(resetSession: true);

            SetQueueCore(queueId, queueName);
        }

        OnChanged();
    }

    public void UpdateReadyCheck(string state, string playerResponse)
    {
        lock (_lock)
        {
            _readyCheckState = NormalizeText(state, "InProgress");
            _readyCheckResponse = NormalizeText(playerResponse, "None");
        }

        OnChanged();
    }

    public void UpdateChampionSelect(
        string timerPhase,
        int timeLeftSeconds,
        IEnumerable<TeamSlot> myTeam,
        IEnumerable<TeamSlot> theirTeam,
        IEnumerable<int> myTeamBans,
        IEnumerable<int> theirTeamBans,
        IEnumerable<ChampSelectAction> actions)
    {
        lock (_lock)
        {
            _timerPhase = NormalizeText(timerPhase, "BAN_PICK");
            _timeLeftSeconds = Math.Max(0, timeLeftSeconds);
            ReplaceList(_myTeam, myTeam.Select(CloneWithNormalizedPosition));
            ReplaceList(_theirTeam, theirTeam.Select(CloneWithNormalizedPosition));
            _localPlayerAssignedPosition = FindTeamSlot(_localPlayerCellId)?.AssignedPosition
                                           ?? _localPlayerAssignedPosition;
            ReplaceList(_myTeamBans, myTeamBans.Where(id => id > 0).Distinct());
            ReplaceList(_theirTeamBans, theirTeamBans.Where(id => id > 0).Distinct());
            ApplyBanListToSlots(_myTeam, _myTeamBans);
            ApplyBanListToSlots(_theirTeam, _theirTeamBans);
            ReplaceList(_actions, actions.Select(action => action.Clone()));
            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();
        }

        OnChanged();
    }

    public void UpdateLocalPlayerRole(string assignedPosition)
    {
        lock (_lock)
        {
            _localPlayerAssignedPosition = MockLeagueClientRoles.NormalizeDisplayRole(assignedPosition);
            var localPlayer = FindTeamSlot(_localPlayerCellId);
            if (localPlayer is not null)
                localPlayer.AssignedPosition = _localPlayerAssignedPosition;

            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();
        }

        OnChanged();
    }

    public void AcceptReadyCheck()
    {
        lock (_lock)
        {
            _readyCheckState = "InProgress";
            _readyCheckResponse = "Accepted";
        }

        OnChanged();
    }

    public bool PatchAction(int actionId, int? championId, bool? completed)
    {
        bool changed;
        lock (_lock)
        {
            var action = _actions.FirstOrDefault(candidate => candidate.Id == actionId);
            if (action is null)
                return false;

            if (championId is int selectedChampionId)
                action.ChampionId = Math.Max(0, selectedChampionId);

            if (completed == true)
            {
                CompleteActionCore(actionId, action.ChampionId);
            }
            else if (string.Equals(action.Type, "pick", StringComparison.OrdinalIgnoreCase)
                     && action.ActorCellId == _localPlayerCellId)
            {
                var localPlayer = FindTeamSlot(_localPlayerCellId);
                if (localPlayer is not null)
                    localPlayer.ChampionPickIntent = action.ChampionId;
            }

            changed = true;
            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();
        }

        if (changed)
            OnChanged();

        return true;
    }

    public string GetGameflowPhase()
    {
        lock (_lock)
        {
            return ToGameflowPhase(_phase);
        }
    }

    public object GetGameflowSessionPayload()
    {
        lock (_lock)
        {
            return new
            {
                phase = ToGameflowPhase(_phase),
                gameData = new
                {
                    queueId = _queueId,
                    queue = new
                    {
                        id = _queueId,
                        queueId = _queueId,
                        name = _queueName,
                        description = _queueName
                    }
                }
            };
        }
    }

    public object GetLobbyPayload()
    {
        lock (_lock)
        {
            return new
            {
                gameConfig = new
                {
                    queueId = _queueId,
                    name = _queueName,
                    queueName = _queueName,
                    description = _queueName
                }
            };
        }
    }

    public bool HasReadyCheck()
    {
        lock (_lock)
        {
            return _phase == MockClientPhase.ReadyCheck;
        }
    }

    public object GetReadyCheckPayload()
    {
        lock (_lock)
        {
            return new
            {
                state = _readyCheckState,
                playerResponse = _readyCheckResponse
            };
        }
    }

    public bool HasChampSelectSession()
    {
        lock (_lock)
        {
            return _phase is MockClientPhase.Planning or MockClientPhase.ChampSelect;
        }
    }

    public object GetChampSelectSessionPayload()
    {
        lock (_lock)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool revealEnemyBans = AreEnemyBansRevealedCore();
            return new
            {
                localPlayerCellId = _localPlayerCellId,
                multiUserChatId = _sessionId,
                timer = new
                {
                    phase = _timerPhase,
                    adjustedTimeLeftInPhase = _timeLeftSeconds * 1000L,
                    totalTimeInPhase = Math.Max(_timeLeftSeconds, 1) * 1000L,
                    internalNowInEpochMs = nowMs,
                    isInfinite = false
                },
                myTeam = _myTeam.Select(CreateTeamMemberPayload).ToArray(),
                theirTeam = _theirTeam.Select(CreateTeamMemberPayload).ToArray(),
                bans = new
                {
                    myTeamBans = _myTeamBans.ToArray(),
                    theirTeamBans = revealEnemyBans ? _theirTeamBans.ToArray() : Array.Empty<int>()
                },
                actions = new[]
                {
                    _actions.Select(action => CreateActionPayload(action, revealEnemyBans)).ToArray()
                }
            };
        }
    }

    public object GetCurrentSummonerPayload()
    {
        return new
        {
            summonerId = 1001,
            displayName = "Mock Player"
        };
    }

    public object GetChampionInventoryPayload()
    {
        return OwnedChampionIds
            .Select(championId => new
            {
                id = championId,
                owned = true,
                ownership = new { owned = true }
            })
            .ToArray();
    }

    private void ApplyPlanningScenarioCore()
    {
        if (_queueMode == MockQueueMode.DraftPick)
        {
            EnsureDraftModeCore();
            SwitchDraftStepCore(DraftPickStep.Planning);
            return;
        }

        ResetBlindPickCore();
        _phase = MockClientPhase.Planning;
        _timerPhase = "PLANNING";
        _timeLeftSeconds = 30;
        SetActionProgress("pick", isInProgress: true);
    }

    private void ApplyBanScenarioCore()
    {
        if (_queueMode == MockQueueMode.DraftPick)
        {
            EnsureDraftModeCore();
            SwitchDraftStepCore(DraftPickStep.Ban);
            return;
        }

        ResetBlindPickCore();
        _phase = MockClientPhase.ChampSelect;
        _timerPhase = "BAN_PICK";
        _timeLeftSeconds = 25;
        SetActionProgress("ban", isInProgress: true);
    }

    private void ApplyPickScenarioCore()
    {
        if (_queueMode == MockQueueMode.DraftPick)
        {
            EnsureDraftModeCore();
            SwitchDraftStepCore(DraftPickStep.BlueFirstPick);
            return;
        }

        ResetBlindPickCore();
        _phase = MockClientPhase.ChampSelect;
        _timerPhase = "BAN_PICK";
        _timeLeftSeconds = 25;
        CompleteActionCore(1, 122);
        SetActionProgress("pick", isInProgress: true);
    }

    private void ApplyFinalizationScenarioCore()
    {
        if (_queueMode == MockQueueMode.DraftPick)
        {
            EnsureDraftModeCore();
            SwitchDraftStepCore(DraftPickStep.Finalization);
            return;
        }

        ResetBlindPickCore();
        _phase = MockClientPhase.ChampSelect;
        _timerPhase = "FINALIZATION";
        _timeLeftSeconds = 10;
        CompleteActionCore(1, 122);
        CompleteActionCore(2, 103);
    }

    private void ApplyInGameScenarioCore()
    {
        if (_queueMode == MockQueueMode.DraftPick)
        {
            EnsureDraftModeCore();
            SwitchDraftStepCore(DraftPickStep.InGame);
            return;
        }

        ResetBlindPickCore();
        _phase = MockClientPhase.InGame;
    }

    private void ResetQueueModeCore(bool resetSession)
    {
        if (_queueMode == MockQueueMode.DraftPick)
        {
            if (resetSession)
                ResetDraftPickCore();
            else
                SetQueueCore(400, "Normal Draft");

            return;
        }

        if (resetSession)
            ResetBlindPickCore();
        else
            SetQueueCore(430, "Blind Pick");
    }

    private void EnsureDraftModeCore()
    {
        if (_queueMode == MockQueueMode.DraftPick && _draftStepStates.Count > 0)
            return;

        _queueMode = MockQueueMode.DraftPick;
        ResetDraftPickCore();
    }

    private void ResetDraftPickCore()
    {
        _queueMode = MockQueueMode.DraftPick;
        _draftStep = DraftPickStep.Planning;
        SetQueueCore(400, "Normal Draft");
        _sessionId = $"mock-champ-select-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _draftStepStates.Clear();
        foreach (var step in DraftPickSteps.All.Select(option => option.Step))
            _draftStepStates[step] = CreateDefaultDraftStepState(step);

        LoadDraftStepStateCore(DraftPickStep.Planning);
    }

    private void ResetBlindPickCore()
    {
        _queueMode = MockQueueMode.BlindPick;
        _draftStep = DraftPickStep.Planning;
        _phase = MockClientPhase.Planning;
        SetQueueCore(430, "Blind Pick");
        ResetSessionCore();

        _myTeam.AddRange(
        [
            new TeamSlot(1, "top", 0, 0, 0),
            new TeamSlot(2, "jungle", 64, 0, 0),
            new TeamSlot(3, "mid", 99, 0, 0),
            new TeamSlot(4, "adc", 222, 0, 0),
            new TeamSlot(5, "support", 412, 0, 0)
        ]);

        _theirTeam.AddRange(
        [
            new TeamSlot(6, "top", 86, 0, 0),
            new TeamSlot(7, "jungle", 0, 0, 0),
            new TeamSlot(8, "mid", 0, 0, 0),
            new TeamSlot(9, "adc", 0, 0, 0),
            new TeamSlot(10, "support", 0, 0, 0)
        ]);

        var localPlayer = FindTeamSlot(_localPlayerCellId);
        if (localPlayer is not null)
            localPlayer.AssignedPosition = _localPlayerAssignedPosition;

        _actions.Add(new ChampSelectAction(1, _localPlayerCellId, "ban", 0, IsInProgress: false, Completed: false));
        _actions.Add(new ChampSelectAction(2, _localPlayerCellId, "pick", 0, IsInProgress: false, Completed: false));
    }

    private void ResetSessionCore()
    {
        _sessionId = $"mock-champ-select-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _timerPhase = "PLANNING";
        _timeLeftSeconds = 30;
        _myTeamBans.Clear();
        _theirTeamBans.Clear();
        _myTeam.Clear();
        _theirTeam.Clear();
        _actions.Clear();
    }

    private void SwitchDraftStepCore(DraftPickStep step)
    {
        SaveCurrentDraftStepStateCore();
        LoadDraftStepStateCore(step);
    }

    private void SaveCurrentDraftStepStateCore()
    {
        if (_queueMode != MockQueueMode.DraftPick)
            return;

        RebuildBanListsFromSlots();
        _draftStepStates[_draftStep] = CaptureCurrentDraftStepStateCore();
    }

    private DraftStepState CaptureCurrentDraftStepStateCore()
    {
        return new DraftStepState(
            _timerPhase,
            _timeLeftSeconds,
            _myTeam.Select(slot => slot.Clone()).ToList(),
            _theirTeam.Select(slot => slot.Clone()).ToList(),
            _myTeamBans.ToList(),
            _theirTeamBans.ToList(),
            _actions.Select(action => action.Clone()).ToList());
    }

    private void LoadDraftStepStateCore(DraftPickStep step)
    {
        if (!_draftStepStates.TryGetValue(step, out var state))
        {
            state = CreateDefaultDraftStepState(step);
            _draftStepStates[step] = state;
        }

        _draftStep = step;
        _phase = GetDefaultPhase(step);
        _timerPhase = state.TimerPhase;
        _timeLeftSeconds = state.TimeLeftSeconds;
        ReplaceList(_myTeam, state.MyTeam.Select(slot => slot.Clone()));
        ReplaceList(_theirTeam, state.TheirTeam.Select(slot => slot.Clone()));
        ReplaceList(_myTeamBans, state.MyTeamBans);
        ReplaceList(_theirTeamBans, state.TheirTeamBans);
        ReplaceList(_actions, state.Actions.Select(action => action.Clone()));

        var localPlayer = FindTeamSlot(_localPlayerCellId);
        if (localPlayer is not null)
            _localPlayerAssignedPosition = localPlayer.AssignedPosition;
    }

    private DraftStepState CreateDefaultDraftStepState(DraftPickStep step)
    {
        var myTeam = CreateDefaultMyTeamSlots();
        var localPlayer = myTeam.FirstOrDefault(slot => slot.CellId == _localPlayerCellId);
        if (localPlayer is not null)
            localPlayer.AssignedPosition = _localPlayerAssignedPosition;

        return new DraftStepState(
            GetDefaultTimerPhase(step),
            GetDefaultTimeLeftSeconds(step),
            myTeam,
            CreateDefaultTheirTeamSlots(),
            [],
            [],
            CreateDefaultDraftActions(step));
    }

    private static List<TeamSlot> CreateDefaultMyTeamSlots()
    {
        return
        [
            new TeamSlot(1, "top", 0, 0, 0),
            new TeamSlot(2, "jungle", 0, 0, 0),
            new TeamSlot(3, "mid", 0, 0, 0),
            new TeamSlot(4, "adc", 0, 0, 0),
            new TeamSlot(5, "support", 0, 0, 0)
        ];
    }

    private static List<TeamSlot> CreateDefaultTheirTeamSlots()
    {
        return
        [
            new TeamSlot(6, "top", 0, 0, 0),
            new TeamSlot(7, "jungle", 0, 0, 0),
            new TeamSlot(8, "mid", 0, 0, 0),
            new TeamSlot(9, "adc", 0, 0, 0),
            new TeamSlot(10, "support", 0, 0, 0)
        ];
    }

    private static List<ChampSelectAction> CreateDefaultDraftActions(DraftPickStep step)
    {
        var actions = new List<ChampSelectAction>();
        for (int cellId = 1; cellId <= 10; cellId++)
        {
            actions.Add(new ChampSelectAction(
                cellId,
                cellId,
                "ban",
                0,
                IsInProgress: step == DraftPickStep.Ban,
                Completed: step > DraftPickStep.Ban));
        }

        int activePickGroupIndex = GetActivePickGroupIndex(step);
        int actionId = 11;
        foreach (int actorCellId in DraftPickGroups.SelectMany(group => group))
        {
            int groupIndex = GetDraftPickGroupIndex(actorCellId);
            actions.Add(new ChampSelectAction(
                actionId++,
                actorCellId,
                "pick",
                0,
                IsInProgress: activePickGroupIndex == groupIndex,
                Completed: IsPickGroupCompleted(groupIndex, activePickGroupIndex, step)));
        }

        return actions;
    }

    private void RebuildBanListsFromSlots()
    {
        ReplaceList(_myTeamBans, _myTeam.Select(slot => slot.BanChampionId).Where(id => id > 0).Distinct());
        ReplaceList(_theirTeamBans, _theirTeam.Select(slot => slot.BanChampionId).Where(id => id > 0).Distinct());
    }

    private static MockClientPhase GetDefaultPhase(DraftPickStep step)
    {
        return step == DraftPickStep.InGame
            ? MockClientPhase.InGame
            : step == DraftPickStep.Planning
                ? MockClientPhase.Planning
                : MockClientPhase.ChampSelect;
    }

    private static string GetDefaultTimerPhase(DraftPickStep step)
    {
        return step switch
        {
            DraftPickStep.Planning => "PLANNING",
            DraftPickStep.Finalization or DraftPickStep.InGame => "FINALIZATION",
            _ => "BAN_PICK"
        };
    }

    private static int GetDefaultTimeLeftSeconds(DraftPickStep step)
    {
        return step switch
        {
            DraftPickStep.Planning => 30,
            DraftPickStep.Finalization => 10,
            DraftPickStep.InGame => 0,
            _ => 25
        };
    }

    private static int GetActivePickGroupIndex(DraftPickStep step)
    {
        return step switch
        {
            DraftPickStep.BlueFirstPick => 0,
            DraftPickStep.RedFirstRotation => 1,
            DraftPickStep.BlueSecondRotation => 2,
            DraftPickStep.RedSecondRotation => 3,
            DraftPickStep.BlueFinalRotation => 4,
            DraftPickStep.RedLastPick => 5,
            _ => -1
        };
    }

    private static int GetDraftPickGroupIndex(int actorCellId)
    {
        for (int index = 0; index < DraftPickGroups.Count; index++)
        {
            if (DraftPickGroups[index].Contains(actorCellId))
                return index;
        }

        return -1;
    }

    private static bool IsPickGroupCompleted(int groupIndex, int activePickGroupIndex, DraftPickStep step)
    {
        if (groupIndex < 0)
            return false;

        if (step is DraftPickStep.Finalization or DraftPickStep.InGame)
            return true;

        return activePickGroupIndex >= 0 && groupIndex < activePickGroupIndex;
    }

    private bool AreEnemyBansRevealedCore()
    {
        return _queueMode != MockQueueMode.DraftPick || _draftStep > DraftPickStep.Ban;
    }

    private void EnsureBlindPickActionActorsCore()
    {
        foreach (var action in _actions)
        {
            if (action.ActorCellId is >= 1 and <= 5)
                action.ActorCellId = _localPlayerCellId;
        }
    }

    private void SetActionProgress(string type, bool isInProgress)
    {
        foreach (var action in _actions)
        {
            action.IsInProgress = string.Equals(action.Type, type, StringComparison.OrdinalIgnoreCase) && isInProgress;
            if (action.IsInProgress)
                action.Completed = false;
        }
    }

    private void CompleteActionCore(int actionId, int championId)
    {
        var action = _actions.FirstOrDefault(candidate => candidate.Id == actionId);
        if (action is null)
            return;

        championId = Math.Max(0, championId);
        action.ChampionId = championId;
        action.Completed = true;
        action.IsInProgress = false;

        var slot = FindTeamSlot(action.ActorCellId);
        if (string.Equals(action.Type, "pick", StringComparison.OrdinalIgnoreCase))
        {
            if (slot is not null)
            {
                slot.ChampionId = championId;
                slot.ChampionPickIntent = 0;
            }
        }
        else if (string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase) && championId > 0)
        {
            if (slot is not null)
                slot.BanChampionId = championId;

            var bans = IsMyTeamCellId(action.ActorCellId) ? _myTeamBans : _theirTeamBans;
            if (!bans.Contains(championId))
                bans.Add(championId);
        }
    }

    private TeamSlot? FindTeamSlot(int cellId)
    {
        return _myTeam.Concat(_theirTeam).FirstOrDefault(slot => slot.CellId == cellId);
    }

    private bool IsMyTeamCellId(int cellId)
    {
        return _myTeam.Any(slot => slot.CellId == cellId);
    }

    private void SetQueueCore(int queueId, string queueName)
    {
        _queueId = Math.Max(0, queueId);
        _queueName = NormalizeText(queueName, $"Queue {_queueId}");
    }

    private static object CreateTeamMemberPayload(TeamSlot slot)
    {
        return new
        {
            cellId = slot.CellId,
            assignedPosition = MockLeagueClientRoles.ToLeagueAssignedPosition(slot.AssignedPosition),
            championId = slot.ChampionId,
            championPickIntent = slot.ChampionPickIntent
        };
    }

    private static object CreateActionPayload(ChampSelectAction action, bool revealEnemyBans)
    {
        int championId = action.ChampionId;
        if (!revealEnemyBans
            && string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase)
            && action.ActorCellId >= 6)
        {
            championId = 0;
        }

        return new
        {
            id = action.Id,
            actorCellId = action.ActorCellId,
            championId,
            completed = action.Completed,
            isInProgress = action.IsInProgress,
            type = action.Type
        };
    }

    private static TeamSlot CloneWithNormalizedPosition(TeamSlot slot)
    {
        var clone = slot.Clone();
        clone.AssignedPosition = MockLeagueClientRoles.NormalizeDisplayRole(clone.AssignedPosition);
        return clone;
    }

    private static void ApplyBanListToSlots(IReadOnlyList<TeamSlot> slots, IReadOnlyList<int> banChampionIds)
    {
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].BanChampionId <= 0 && index < banChampionIds.Count)
                slots[index].BanChampionId = banChampionIds[index];
        }
    }

    private static string ToGameflowPhase(MockClientPhase phase)
    {
        return phase switch
        {
            MockClientPhase.Unknown => "None",
            MockClientPhase.InGame => "InProgress",
            _ => phase.ToString()
        };
    }

    private static string NormalizeText(string? text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text)
            ? fallback
            : text.Trim();
    }

    private static void ReplaceList<T>(List<T> target, IEnumerable<T> values)
    {
        target.Clear();
        target.AddRange(values);
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed record MockLeagueClientSnapshot(
    MockClientPhase Phase,
    MockQueueMode QueueMode,
    DraftPickStep DraftStep,
    int LocalPlayerCellId,
    bool EnemyBansRevealed,
    int QueueId,
    string QueueName,
    string ReadyCheckState,
    string ReadyCheckResponse,
    string TimerPhase,
    int TimeLeftSeconds,
    IReadOnlyList<TeamSlot> MyTeam,
    IReadOnlyList<TeamSlot> TheirTeam,
    IReadOnlyList<int> MyTeamBans,
    IReadOnlyList<int> TheirTeamBans,
    IReadOnlyList<ChampSelectAction> Actions);

internal sealed record DraftStepState(
    string TimerPhase,
    int TimeLeftSeconds,
    IReadOnlyList<TeamSlot> MyTeam,
    IReadOnlyList<TeamSlot> TheirTeam,
    IReadOnlyList<int> MyTeamBans,
    IReadOnlyList<int> TheirTeamBans,
    IReadOnlyList<ChampSelectAction> Actions);

internal sealed class TeamSlot
{
    public TeamSlot()
    {
    }

    public TeamSlot(int cellId, string assignedPosition, int championId, int championPickIntent, int banChampionId)
    {
        CellId = cellId;
        AssignedPosition = assignedPosition;
        ChampionId = championId;
        ChampionPickIntent = championPickIntent;
        BanChampionId = banChampionId;
    }

    public int CellId { get; set; }
    public string AssignedPosition { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public int ChampionPickIntent { get; set; }
    public int BanChampionId { get; set; }

    public TeamSlot Clone()
    {
        return new TeamSlot(CellId, AssignedPosition, ChampionId, ChampionPickIntent, BanChampionId);
    }
}

internal sealed class ChampSelectAction
{
    public ChampSelectAction()
    {
    }

    public ChampSelectAction(int id, int actorCellId, string type, int championId, bool IsInProgress, bool Completed)
    {
        Id = id;
        ActorCellId = actorCellId;
        Type = type;
        ChampionId = championId;
        this.IsInProgress = IsInProgress;
        this.Completed = Completed;
    }

    public int Id { get; set; }
    public int ActorCellId { get; set; }
    public string Type { get; set; } = "pick";
    public int ChampionId { get; set; }
    public bool IsInProgress { get; set; }
    public bool Completed { get; set; }

    public ChampSelectAction Clone()
    {
        return new ChampSelectAction(Id, ActorCellId, Type, ChampionId, IsInProgress, Completed);
    }
}
