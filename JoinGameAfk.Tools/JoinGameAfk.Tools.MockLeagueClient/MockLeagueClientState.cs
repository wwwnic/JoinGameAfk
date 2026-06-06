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

internal sealed partial class MockLeagueClientState
{
    private const int DefaultChampSelectTimeLeftSeconds = 30;
    private const int FirstPlayerCellId = 1;
    private const int LastBluePlayerCellId = 5;
    private const int LastPlayerCellId = 10;

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
    private int _totalTimeInPhaseSeconds = 30;
    private string _sessionId = "mock-champ-select-1";
    private int _localPlayerCellId = 1;
    private string _localPlayerAssignedPosition = MockLeagueClientRoles.DefaultRole;
    private readonly List<TeamSlot> _myTeam = [];
    private readonly List<TeamSlot> _theirTeam = [];
    private readonly List<int> _myTeamBans = [];
    private readonly List<int> _theirTeamBans = [];
    private readonly List<ChampSelectAction> _actions = [];
    private readonly List<TimedCustomAction> _customTimedActions = [];
    private readonly Dictionary<DraftPickStep, DraftStepState> _draftStepStates = [];
    private readonly Dictionary<int, int> _sharedDraftPickHoverChampionIds = [];
    private readonly Dictionary<int, int> _sharedDraftBanHoverChampionIds = [];

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
                _actions.Select(action => action.Clone()).ToList(),
                _customTimedActions.Select(action => action.Clone()).ToList());
        }
    }

    public void RestoreSnapshot(MockLeagueClientSnapshot snapshot)
    {
        lock (_lock)
        {
            RestoreSnapshotCore(snapshot);
        }

        OnChanged();
    }

    public void RestoreSnapshotAsNewChampSelectSession(MockLeagueClientSnapshot snapshot)
    {
        lock (_lock)
        {
            RestoreSnapshotCore(snapshot);
            _sessionId = CreateSessionId();
        }

        OnChanged();
    }

    public void EnterChampSelectRestartExitPhase()
    {
        lock (_lock)
        {
            _phase = MockClientPhase.Lobby;
        }

        OnChanged();
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
                    _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
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
                    _timeLeftSeconds = DefaultChampSelectTimeLeftSeconds;
                    _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
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
            _localPlayerCellId = NormalizeLocalPlayerCellId(localPlayerCellId);
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
        int timeLeftSeconds,
        IEnumerable<TeamSlot> myTeam,
        IEnumerable<TeamSlot> theirTeam,
        IEnumerable<int> myTeamBans,
        IEnumerable<int> theirTeamBans,
        IEnumerable<ChampSelectAction> actions,
        IEnumerable<TimedCustomAction> customTimedActions)
    {
        lock (_lock)
        {
            _timerPhase = GetDefaultTimerPhase(_draftStep);
            _timeLeftSeconds = Math.Max(0, timeLeftSeconds);
            _totalTimeInPhaseSeconds = Math.Max(_timeLeftSeconds, 1);
            ReplaceList(_myTeam, myTeam.Select(CloneWithNormalizedPosition));
            ReplaceList(_theirTeam, theirTeam.Select(CloneWithNormalizedPosition));
            _localPlayerAssignedPosition = FindTeamSlot(_localPlayerCellId)?.AssignedPosition
                                           ?? _localPlayerAssignedPosition;
            ReplaceList(_myTeamBans, myTeamBans.Where(id => id > 0).Distinct());
            ReplaceList(_theirTeamBans, theirTeamBans.Where(id => id > 0).Distinct());
            ApplyBanListToSlots(_myTeam, _myTeamBans);
            ApplyBanListToSlots(_theirTeam, _theirTeamBans);
            ReplaceList(_actions, actions.Select(CloneWithNormalizedSchedule));
            ReplaceList(_customTimedActions, customTimedActions.Select(CloneWithNormalizedCustomAction));
            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();
        }

        OnChanged();
    }

    public DraftTimerTickResult TickChampionSelectTimer()
    {
        DraftTimerTickResult result;
        lock (_lock)
        {
            if (_phase is not (MockClientPhase.Planning or MockClientPhase.ChampSelect)
                || _timeLeftSeconds <= 0)
            {
                return new DraftTimerTickResult(false, _timeLeftSeconds, []);
            }

            _timeLeftSeconds--;
            var appliedActions = ApplyScheduledActionsCore();

            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();

            result = new DraftTimerTickResult(true, _timeLeftSeconds, appliedActions);
        }

        OnChanged();
        return result;
    }

    public DraftTimerTickResult ApplyDueScheduledActions()
    {
        DraftTimerTickResult result;
        lock (_lock)
        {
            if (_phase is not (MockClientPhase.Planning or MockClientPhase.ChampSelect))
                return new DraftTimerTickResult(false, _timeLeftSeconds, []);

            var appliedActions = ApplyScheduledActionsCore();
            if (appliedActions.Count == 0)
                return new DraftTimerTickResult(false, _timeLeftSeconds, []);

            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();

            result = new DraftTimerTickResult(true, _timeLeftSeconds, appliedActions);
        }

        OnChanged();
        return result;
    }

    public bool BeginChampionSelectPlayback()
    {
        bool changed;
        lock (_lock)
        {
            if (_timeLeftSeconds <= 0
                || (_queueMode == MockQueueMode.DraftPick && _draftStep == DraftPickStep.InGame))
            {
                return false;
            }

            var playbackPhase = GetPlaybackPhaseCore();
            changed = _phase != playbackPhase;
            _phase = playbackPhase;
        }

        if (changed)
            OnChanged();

        return true;
    }

    public DraftPlaybackAdvanceResult AdvanceDraftStepPlayback()
    {
        DraftPlaybackAdvanceResult result;
        lock (_lock)
        {
            if (_queueMode != MockQueueMode.DraftPick || _draftStep == DraftPickStep.InGame)
            {
                return new DraftPlaybackAdvanceResult(
                    Advanced: false,
                    Step: _draftStep,
                    TimeLeftSeconds: _timeLeftSeconds,
                    ContinuesPlayback: false);
            }

            PrepareCurrentDraftStepStateForCaptureCore();
            var currentState = CaptureCurrentDraftStepStateCore();
            _draftStepStates[_draftStep] = currentState;

            DraftPickStep nextStep = DraftPickSteps.Next(_draftStep);
            _draftStepStates[nextStep] = CreatePlaybackAdvanceState(nextStep, currentState);
            LoadDraftStepStateCore(nextStep);

            result = new DraftPlaybackAdvanceResult(
                Advanced: true,
                Step: _draftStep,
                TimeLeftSeconds: _timeLeftSeconds,
                ContinuesPlayback: _phase is MockClientPhase.Planning or MockClientPhase.ChampSelect
                                   && _timeLeftSeconds > 0);
        }

        OnChanged();
        return result;
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
            else if (championId.HasValue)
            {
                TryApplyHoveredChampionToSlotCore(action, action.ChampionId);
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
            bool localPlayerOnBlueSide = IsBlueTeamCellId(_localPlayerCellId);
            var localTeam = localPlayerOnBlueSide ? _myTeam : _theirTeam;
            var enemyTeam = localPlayerOnBlueSide ? _theirTeam : _myTeam;
            var localTeamBans = localPlayerOnBlueSide ? _myTeamBans : _theirTeamBans;
            var enemyTeamBans = localPlayerOnBlueSide ? _theirTeamBans : _myTeamBans;

            return new
            {
                localPlayerCellId = _localPlayerCellId,
                multiUserChatId = _sessionId,
                timer = CreateTimerPayload(nowMs),
                myTeam = localTeam.Select(CreateTeamMemberPayload).ToArray(),
                theirTeam = enemyTeam.Select(CreateTeamMemberPayload).ToArray(),
                bans = new
                {
                    myTeamBans = localTeamBans.ToArray(),
                    theirTeamBans = revealEnemyBans ? enemyTeamBans.ToArray() : Array.Empty<int>()
                },
                actions = new[]
                {
                    _actions.Select(action => CreateActionPayload(action, revealEnemyBans, _localPlayerCellId)).ToArray()
                }
            };
        }
    }

    public object GetChampSelectTimerPayload()
    {
        lock (_lock)
        {
            return CreateTimerPayload(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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
        _timeLeftSeconds = DefaultChampSelectTimeLeftSeconds;
        _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
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
        _timeLeftSeconds = DefaultChampSelectTimeLeftSeconds;
        _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
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
        _timeLeftSeconds = DefaultChampSelectTimeLeftSeconds;
        _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
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
        _timeLeftSeconds = DefaultChampSelectTimeLeftSeconds;
        _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
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
        _sessionId = CreateSessionId();
        _draftStepStates.Clear();
        _sharedDraftPickHoverChampionIds.Clear();
        _sharedDraftBanHoverChampionIds.Clear();
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
            new TeamSlot(1, "top", 0, 0, 0, 0),
            new TeamSlot(2, "jungle", 64, 0, 0, 0),
            new TeamSlot(3, "mid", 99, 0, 0, 0),
            new TeamSlot(4, "adc", 222, 0, 0, 0),
            new TeamSlot(5, "support", 412, 0, 0, 0)
        ]);

        _theirTeam.AddRange(
        [
            new TeamSlot(6, "top", 86, 0, 0, 0),
            new TeamSlot(7, "jungle", 0, 0, 0, 0),
            new TeamSlot(8, "mid", 0, 0, 0, 0),
            new TeamSlot(9, "adc", 0, 0, 0, 0),
            new TeamSlot(10, "support", 0, 0, 0, 0)
        ]);

        var localPlayer = FindTeamSlot(_localPlayerCellId);
        if (localPlayer is not null)
            localPlayer.AssignedPosition = _localPlayerAssignedPosition;

        _actions.Add(new ChampSelectAction(1, _localPlayerCellId, "ban", 0, IsInProgress: false, Completed: false));
        _actions.Add(new ChampSelectAction(2, _localPlayerCellId, "pick", 0, IsInProgress: false, Completed: false));
    }

    private void ResetSessionCore()
    {
        _sessionId = CreateSessionId();
        _timerPhase = "PLANNING";
        _timeLeftSeconds = DefaultChampSelectTimeLeftSeconds;
        _totalTimeInPhaseSeconds = GetDefaultTotalTimeInPhaseSeconds(_timerPhase);
        _myTeamBans.Clear();
        _theirTeamBans.Clear();
        _myTeam.Clear();
        _theirTeam.Clear();
        _actions.Clear();
        _customTimedActions.Clear();
        _sharedDraftPickHoverChampionIds.Clear();
        _sharedDraftBanHoverChampionIds.Clear();
    }

    private void RestoreSnapshotCore(MockLeagueClientSnapshot snapshot)
    {
        _phase = snapshot.Phase;
        _queueMode = snapshot.QueueMode;
        _draftStep = snapshot.DraftStep;
        _localPlayerCellId = NormalizeLocalPlayerCellId(snapshot.LocalPlayerCellId);
        SetQueueCore(snapshot.QueueId, snapshot.QueueName);
        _readyCheckState = NormalizeText(snapshot.ReadyCheckState, "InProgress");
        _readyCheckResponse = NormalizeText(snapshot.ReadyCheckResponse, "None");
        _timerPhase = NormalizeText(snapshot.TimerPhase, "BAN_PICK");
        _timeLeftSeconds = Math.Max(0, snapshot.TimeLeftSeconds);
        _totalTimeInPhaseSeconds = Math.Max(_timeLeftSeconds, 1);
        _sharedDraftPickHoverChampionIds.Clear();
        _sharedDraftBanHoverChampionIds.Clear();
        ReplaceList(_myTeam, snapshot.MyTeam.Select(CloneWithNormalizedPosition));
        ReplaceList(_theirTeam, snapshot.TheirTeam.Select(CloneWithNormalizedPosition));
        _localPlayerAssignedPosition = FindTeamSlot(_localPlayerCellId)?.AssignedPosition
                                       ?? _localPlayerAssignedPosition;
        ReplaceList(_myTeamBans, snapshot.MyTeamBans.Where(id => id > 0).Distinct());
        ReplaceList(_theirTeamBans, snapshot.TheirTeamBans.Where(id => id > 0).Distinct());
        ApplyBanListToSlots(_myTeam, _myTeamBans);
        ApplyBanListToSlots(_theirTeam, _theirTeamBans);
        ReplaceList(_actions, snapshot.Actions.Select(CloneWithNormalizedSchedule));
        ReplaceList(_customTimedActions, snapshot.CustomTimedActions.Select(CloneWithNormalizedCustomAction));

        if (_queueMode == MockQueueMode.DraftPick)
            SaveCurrentDraftStepStateCore();
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

        PrepareCurrentDraftStepStateForCaptureCore();
        _draftStepStates[_draftStep] = CaptureCurrentDraftStepStateCore();
    }

    private void PrepareCurrentDraftStepStateForCaptureCore()
    {
        if (_queueMode != MockQueueMode.DraftPick)
            return;

        SyncSharedDraftHoverIntentsFromCurrentSlotsCore();
        ApplySharedDraftHoverIntentsToCurrentStateCore();
        RebuildBanListsFromSlots();
    }

    private void SyncSharedDraftHoverIntentsFromCurrentSlotsCore()
    {
        SyncSharedDraftHoverIntentsFromSlots(_myTeam);
        SyncSharedDraftHoverIntentsFromSlots(_theirTeam);
    }

    private void SyncSharedDraftHoverIntentsFromSlots(IEnumerable<TeamSlot> slots)
    {
        foreach (var slot in slots)
        {
            if (!IsValidCellId(slot.CellId))
                continue;

            SyncSharedDraftHoverIntent(_sharedDraftPickHoverChampionIds, slot.CellId, slot.ChampionPickIntent, slot.ChampionId);
            SyncSharedDraftHoverIntent(_sharedDraftBanHoverChampionIds, slot.CellId, slot.BanChampionIntent, slot.BanChampionId);
        }
    }

    private static void SyncSharedDraftHoverIntent(
        Dictionary<int, int> hoverChampionIds,
        int cellId,
        int hoverChampionId,
        int lockedChampionId)
    {
        if (lockedChampionId > 0 || hoverChampionId <= 0)
        {
            hoverChampionIds.Remove(cellId);
            return;
        }

        hoverChampionIds[cellId] = hoverChampionId;
    }

    private void ApplySharedDraftHoverIntentsToCurrentStateCore()
    {
        ApplySharedDraftHoverIntentsToSlots(_myTeam);
        ApplySharedDraftHoverIntentsToSlots(_theirTeam);
        ApplySharedDraftHoverIntentsToActions();
    }

    private void ApplySharedDraftHoverIntentsToSlots(IEnumerable<TeamSlot> slots)
    {
        foreach (var slot in slots)
        {
            if (!IsValidCellId(slot.CellId))
                continue;

            slot.ChampionPickIntent = slot.ChampionId > 0
                ? 0
                : GetSharedDraftHoverChampionId(_sharedDraftPickHoverChampionIds, slot.CellId);

            slot.BanChampionIntent = slot.BanChampionId > 0
                ? 0
                : GetSharedDraftHoverChampionId(_sharedDraftBanHoverChampionIds, slot.CellId);
        }
    }

    private void ApplySharedDraftHoverIntentsToActions()
    {
        foreach (var action in _actions)
        {
            if (action.Completed)
                continue;

            if (IsPickAction(action))
            {
                action.ChampionId = GetSharedDraftHoverChampionId(_sharedDraftPickHoverChampionIds, action.ActorCellId);
                continue;
            }

            if (IsBanAction(action))
                action.ChampionId = GetSharedDraftHoverChampionId(_sharedDraftBanHoverChampionIds, action.ActorCellId);
        }
    }

    private static int GetSharedDraftHoverChampionId(IReadOnlyDictionary<int, int> hoverChampionIds, int cellId)
    {
        return hoverChampionIds.TryGetValue(cellId, out int championId)
            ? championId
            : 0;
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
            _actions.Select(action => action.Clone()).ToList(),
            _customTimedActions.Select(action => action.Clone()).ToList());
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
        _totalTimeInPhaseSeconds = Math.Max(_timeLeftSeconds, 1);
        ReplaceList(_myTeam, state.MyTeam.Select(slot => slot.Clone()));
        ReplaceList(_theirTeam, state.TheirTeam.Select(slot => slot.Clone()));
        ReplaceList(_myTeamBans, state.MyTeamBans);
        ReplaceList(_theirTeamBans, state.TheirTeamBans);
        ReplaceList(_actions, state.Actions.Select(action => action.Clone()));
        ReplaceList(_customTimedActions, state.CustomTimedActions.Select(action => action.Clone()));
        ApplySharedDraftHoverIntentsToCurrentStateCore();

        var localPlayer = FindTeamSlot(_localPlayerCellId);
        if (localPlayer is not null)
            _localPlayerAssignedPosition = localPlayer.AssignedPosition;
    }

    private DraftStepState CreateDefaultDraftStepState(DraftPickStep step)
    {
        var myTeam = CreateDefaultMyTeamSlots();
        var theirTeam = CreateDefaultTheirTeamSlots();
        ApplyLocalPlayerAssignedPosition(myTeam, theirTeam);

        return new DraftStepState(
            GetDefaultTimerPhase(step),
            GetDefaultTimeLeftSeconds(step),
            myTeam,
            theirTeam,
            [],
            [],
            CreateDefaultDraftActions(step),
            []);
    }

    private DraftStepState CreatePlaybackAdvanceState(DraftPickStep nextStep, DraftStepState currentState)
    {
        var defaultNextState = CreateDefaultDraftStepState(nextStep);
        var configuredNextState = _draftStepStates.TryGetValue(nextStep, out var existingNextState)
            ? existingNextState
            : defaultNextState;
        var myTeam = MergePlaybackSlots(defaultNextState.MyTeam, currentState.MyTeam);
        var theirTeam = MergePlaybackSlots(defaultNextState.TheirTeam, currentState.TheirTeam);
        var actions = MergePlaybackActions(defaultNextState.Actions, currentState.Actions);
        ApplyConfiguredTimedActionSchedules(actions, configuredNextState.Actions);
        var myTeamBans = MergePlaybackBans(currentState.MyTeamBans, myTeam);
        var theirTeamBans = MergePlaybackBans(currentState.TheirTeamBans, theirTeam);

        ApplyBanListToSlots(myTeam, myTeamBans);
        ApplyBanListToSlots(theirTeam, theirTeamBans);

        return new DraftStepState(
            configuredNextState.TimerPhase,
            configuredNextState.TimeLeftSeconds,
            myTeam,
            theirTeam,
            myTeamBans,
            theirTeamBans,
            actions,
            configuredNextState.CustomTimedActions.Select(action => action.Clone()).ToList());
    }

    private static void ApplyConfiguredTimedActionSchedules(
        List<ChampSelectAction> actions,
        IReadOnlyList<ChampSelectAction> configuredActions)
    {
        foreach (var action in actions)
        {
            if (action.Completed)
                continue;

            var configuredAction = configuredActions.FirstOrDefault(candidate =>
                candidate.ActorCellId == action.ActorCellId
                && string.Equals(candidate.Type, action.Type, StringComparison.OrdinalIgnoreCase));

            if (configuredAction is null)
                continue;

            action.TargetChampionId = configuredAction.TargetChampionId;
            action.HoverAtSeconds = configuredAction.HoverAtSeconds;
            action.LockAtSeconds = configuredAction.LockAtSeconds;
        }
    }

    private static List<TeamSlot> MergePlaybackSlots(
        IReadOnlyList<TeamSlot> defaultSlots,
        IReadOnlyList<TeamSlot> currentSlots)
    {
        return defaultSlots
            .Select(defaultSlot =>
            {
                var slot = defaultSlot.Clone();
                var currentSlot = currentSlots.FirstOrDefault(candidate => candidate.CellId == slot.CellId);
                if (currentSlot is null)
                    return slot;

                slot.AssignedPosition = currentSlot.AssignedPosition;
                slot.ChampionId = currentSlot.ChampionId;
                slot.ChampionPickIntent = currentSlot.ChampionId > 0 ? 0 : currentSlot.ChampionPickIntent;
                slot.BanChampionIntent = currentSlot.BanChampionId > 0 ? 0 : currentSlot.BanChampionIntent;
                slot.BanChampionId = currentSlot.BanChampionId;
                return slot;
            })
            .ToList();
    }

    private static List<int> MergePlaybackBans(
        IReadOnlyList<int> currentBans,
        IReadOnlyList<TeamSlot> slots)
    {
        return currentBans
            .Concat(slots.Select(slot => slot.BanChampionId))
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private static List<ChampSelectAction> MergePlaybackActions(
        IReadOnlyList<ChampSelectAction> defaultActions,
        IReadOnlyList<ChampSelectAction> currentActions)
    {
        return defaultActions
            .Select(defaultAction =>
            {
                var action = defaultAction.Clone();
                var currentAction = currentActions.FirstOrDefault(candidate => candidate.Id == action.Id);
                if (currentAction is null)
                    return action;

                action.ActorCellId = currentAction.ActorCellId;
                action.ChampionId = currentAction.ChampionId;
                action.TargetChampionId = currentAction.TargetChampionId;
                action.HoverAtSeconds = currentAction.HoverAtSeconds;
                action.LockAtSeconds = currentAction.LockAtSeconds;

                if (currentAction.Completed)
                {
                    action.Completed = true;
                    action.IsInProgress = false;
                }

                return action;
            })
            .ToList();
    }

    private static List<TeamSlot> CreateDefaultMyTeamSlots()
    {
        return
        [
            new TeamSlot(1, "top", 0, 0, 0, 0),
            new TeamSlot(2, "jungle", 0, 0, 0, 0),
            new TeamSlot(3, "mid", 0, 0, 0, 0),
            new TeamSlot(4, "adc", 0, 0, 0, 0),
            new TeamSlot(5, "support", 0, 0, 0, 0)
        ];
    }

    private static List<TeamSlot> CreateDefaultTheirTeamSlots()
    {
        return
        [
            new TeamSlot(6, "top", 0, 0, 0, 0),
            new TeamSlot(7, "jungle", 0, 0, 0, 0),
            new TeamSlot(8, "mid", 0, 0, 0, 0),
            new TeamSlot(9, "adc", 0, 0, 0, 0),
            new TeamSlot(10, "support", 0, 0, 0, 0)
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

    private MockClientPhase GetPlaybackPhaseCore()
    {
        if (_queueMode == MockQueueMode.DraftPick)
            return GetDefaultPhase(_draftStep);

        return string.Equals(_timerPhase, "PLANNING", StringComparison.OrdinalIgnoreCase)
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
        return step == DraftPickStep.InGame ? 0 : DefaultChampSelectTimeLeftSeconds;
    }

    private static int GetDefaultTotalTimeInPhaseSeconds(string _)
    {
        return DefaultChampSelectTimeLeftSeconds;
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
            action.ActorCellId = _localPlayerCellId;
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
            {
                slot.BanChampionId = championId;
                slot.BanChampionIntent = 0;
            }

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

    private object CreateTimerPayload(long internalNowInEpochMs)
    {
        long adjustedTimeLeftMs = Math.Max(0, _timeLeftSeconds) * 1000L;
        long totalTimeMs = Math.Max(Math.Max(_totalTimeInPhaseSeconds, _timeLeftSeconds), 1) * 1000L;

        return new
        {
            phase = _timerPhase,
            adjustedTimeLeftInPhase = adjustedTimeLeftMs,
            timeLeftInPhase = adjustedTimeLeftMs,
            adjustedTimeLeftInPhaseInSec = Math.Max(0, _timeLeftSeconds),
            timeLeftInPhaseInSec = Math.Max(0, _timeLeftSeconds),
            totalTimeInPhase = totalTimeMs,
            internalNowInEpochMs,
            isInfinite = false
        };
    }

    private static object CreateActionPayload(ChampSelectAction action, bool revealEnemyBans, int localPlayerCellId)
    {
        int championId = action.ChampionId;
        if (!revealEnemyBans
            && string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase)
            && IsEnemyTeamCellId(action.ActorCellId, localPlayerCellId))
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

    private List<string> ApplyScheduledActionsCore()
    {
        var appliedActions = ApplyCustomTimedActionsCore();
        foreach (var action in _actions)
        {
            if (IsTimerTriggerDue(action.HoverAtSeconds)
                && TryHoverActionCore(action))
            {
                appliedActions.Add(FormatScheduledAction(action, "hovered"));
            }

            if (IsTimerTriggerDue(action.LockAtSeconds)
                && TryLockActionCore(action))
            {
                appliedActions.Add(FormatScheduledAction(action, "locked"));
            }
        }

        return appliedActions;
    }

    private List<string> ApplyCustomTimedActionsCore()
    {
        var appliedActions = new List<string>();
        foreach (var action in _customTimedActions)
        {
            if (action.Applied || action.Skipped || !IsTimerTriggerDue(action.TriggerAtSeconds))
                continue;

            appliedActions.Add(ApplyCustomTimedActionCore(action));
        }

        return appliedActions;
    }

    private string ApplyCustomTimedActionCore(TimedCustomAction action)
    {
        if (!IsValidCellId(action.SourceCellId) || !IsValidCellId(action.TargetCellId))
            return MarkCustomTimedActionSkipped(action, "source and target cells must be between 1 and 10");

        if (action.SourceCellId == action.TargetCellId)
            return MarkCustomTimedActionSkipped(action, "source and target cells must be different");

        return action.Type switch
        {
            TimedCustomActionType.RoleSwap => ApplyRoleSwapCore(action),
            TimedCustomActionType.ChampionSwap => ApplyChampionSwapCore(action),
            TimedCustomActionType.PickOrderSwap => ApplyPickOrderSwapCore(action),
            _ => MarkCustomTimedActionSkipped(action, "unsupported optional action type")
        };
    }

    private string ApplyRoleSwapCore(TimedCustomAction action)
    {
        var sourceSlot = FindTeamSlot(action.SourceCellId);
        var targetSlot = FindTeamSlot(action.TargetCellId);
        if (sourceSlot is null || targetSlot is null)
            return MarkCustomTimedActionSkipped(action, "source or target cell was not found");

        (sourceSlot.AssignedPosition, targetSlot.AssignedPosition) = (targetSlot.AssignedPosition, sourceSlot.AssignedPosition);
        RefreshLocalPlayerAssignedPositionCore();
        return MarkCustomTimedActionApplied(action, "swapped assigned roles");
    }

    private string ApplyChampionSwapCore(TimedCustomAction action)
    {
        var sourceSlot = FindTeamSlot(action.SourceCellId);
        var targetSlot = FindTeamSlot(action.TargetCellId);
        if (sourceSlot is null || targetSlot is null)
            return MarkCustomTimedActionSkipped(action, "source or target cell was not found");

        (sourceSlot.ChampionId, targetSlot.ChampionId) = (targetSlot.ChampionId, sourceSlot.ChampionId);
        if (sourceSlot.ChampionId > 0)
            sourceSlot.ChampionPickIntent = 0;

        if (targetSlot.ChampionId > 0)
            targetSlot.ChampionPickIntent = 0;

        SyncCompletedPickActionChampion(action.SourceCellId, sourceSlot.ChampionId);
        SyncCompletedPickActionChampion(action.TargetCellId, targetSlot.ChampionId);
        return MarkCustomTimedActionApplied(action, "swapped locked champions");
    }

    private string ApplyPickOrderSwapCore(TimedCustomAction action)
    {
        var sourceAction = FindPickActionByCellId(action.SourceCellId);
        var targetAction = FindPickActionByCellId(action.TargetCellId);
        if (sourceAction is null || targetAction is null)
            return MarkCustomTimedActionSkipped(action, "source or target pick action was not found");

        var sourceSchedule = TimedPickSchedule.FromAction(sourceAction);
        var targetSchedule = TimedPickSchedule.FromAction(targetAction);

        sourceAction.ActorCellId = action.TargetCellId;
        targetAction.ActorCellId = action.SourceCellId;
        sourceSchedule.ApplyTo(targetAction);
        targetSchedule.ApplyTo(sourceAction);

        return MarkCustomTimedActionApplied(action, "swapped pick action ownership");
    }

    private void SyncCompletedPickActionChampion(int cellId, int championId)
    {
        foreach (var action in _actions.Where(action =>
                     action.Completed
                     && action.ActorCellId == cellId
                     && IsPickAction(action)))
        {
            action.ChampionId = championId;
        }
    }

    private ChampSelectAction? FindPickActionByCellId(int cellId)
    {
        return _actions.FirstOrDefault(action => action.ActorCellId == cellId && IsPickAction(action));
    }

    private string MarkCustomTimedActionApplied(TimedCustomAction action, string detail)
    {
        action.Applied = true;
        action.Skipped = false;
        return FormatCustomTimedAction(action, detail);
    }

    private string MarkCustomTimedActionSkipped(TimedCustomAction action, string reason)
    {
        action.Applied = false;
        action.Skipped = true;
        return FormatCustomTimedAction(action, $"skipped: {reason}");
    }

    private string FormatCustomTimedAction(TimedCustomAction action, string detail)
    {
        return $"Optional action {action.Id} {GetCustomTimedActionTypeDisplayName(action.Type)} cells {action.SourceCellId}<->{action.TargetCellId} {detail} at {_timeLeftSeconds}s.";
    }

    private bool TryHoverActionCore(ChampSelectAction action)
    {
        if (!CanApplyScheduledHoverCore(action))
            return false;

        int championId = GetScheduledChampionId(action);
        if (championId <= 0)
            return false;

        bool actionAlreadySelected = action.ChampionId == championId;
        bool slotChanged = TryApplyHoveredChampionToSlotCore(action, championId);
        if (actionAlreadySelected && !slotChanged)
            return false;

        action.ChampionId = championId;
        return true;
    }

    private bool TryLockActionCore(ChampSelectAction action)
    {
        if (!CanApplyScheduledLockCore(action))
            return false;

        int championId = GetScheduledChampionId(action);
        if (championId <= 0)
            return false;

        CompleteActionCore(action.Id, championId);
        return true;
    }

    private bool CanApplyScheduledHoverCore(ChampSelectAction action)
    {
        if (action.Completed)
            return false;

        if (action.IsInProgress)
            return true;

        return IsPlanningTimerPhase()
               && IsPickAction(action);
    }

    private static bool CanApplyScheduledLockCore(ChampSelectAction action)
    {
        return !action.Completed && action.IsInProgress;
    }

    private bool TryApplyHoveredChampionToSlotCore(ChampSelectAction action, int championId)
    {
        var slot = FindTeamSlot(action.ActorCellId);
        if (slot is null)
            return false;

        if (IsPickAction(action))
        {
            if (slot.ChampionPickIntent == championId)
                return false;

            slot.ChampionPickIntent = championId;
            return true;
        }

        if (IsBanAction(action))
        {
            if (slot.BanChampionIntent == championId)
                return false;

            slot.BanChampionIntent = championId;
            return true;
        }

        return false;
    }

    private int GetScheduledChampionId(ChampSelectAction action)
    {
        if (action.TargetChampionId > 0)
            return action.TargetChampionId;

        if (action.ChampionId > 0)
            return action.ChampionId;

        var slot = FindTeamSlot(action.ActorCellId);
        if (slot is null)
            return 0;

        if (IsPickAction(action))
        {
            if (slot.ChampionPickIntent > 0)
                return slot.ChampionPickIntent;

            return slot.ChampionId;
        }

        if (IsBanAction(action))
        {
            if (slot.BanChampionIntent > 0)
                return slot.BanChampionIntent;

            return slot.BanChampionId;
        }

        return 0;
    }

    private bool IsPlanningTimerPhase()
    {
        return string.Equals(_timerPhase, "PLANNING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPickAction(ChampSelectAction action)
    {
        return string.Equals(action.Type, "pick", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBanAction(ChampSelectAction action)
    {
        return string.Equals(action.Type, "ban", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTimerTriggerDue(int? triggerSeconds)
    {
        return triggerSeconds is int seconds
               && _timeLeftSeconds <= Math.Max(0, seconds);
    }

    private string FormatScheduledAction(ChampSelectAction action, string verb)
    {
        return $"Action {action.Id} {action.Type} for cell {action.ActorCellId} {verb} at {_timeLeftSeconds}s.";
    }

    private static TeamSlot CloneWithNormalizedPosition(TeamSlot slot)
    {
        var clone = slot.Clone();
        clone.AssignedPosition = MockLeagueClientRoles.NormalizeDisplayRole(clone.AssignedPosition);
        return clone;
    }

    private static ChampSelectAction CloneWithNormalizedSchedule(ChampSelectAction action)
    {
        var clone = action.Clone();
        clone.TargetChampionId = Math.Max(0, clone.TargetChampionId);
        clone.HoverAtSeconds = NormalizeTriggerSeconds(clone.HoverAtSeconds);
        clone.LockAtSeconds = NormalizeTriggerSeconds(clone.LockAtSeconds);
        return clone;
    }

    private static TimedCustomAction CloneWithNormalizedCustomAction(TimedCustomAction action)
    {
        var clone = action.Clone();
        clone.Id = Math.Max(0, clone.Id);
        clone.SourceCellId = Math.Clamp(clone.SourceCellId, FirstPlayerCellId, LastPlayerCellId);
        clone.TargetCellId = Math.Clamp(clone.TargetCellId, FirstPlayerCellId, LastPlayerCellId);
        clone.TriggerAtSeconds = Math.Max(0, clone.TriggerAtSeconds);
        return clone;
    }

    private static int? NormalizeTriggerSeconds(int? seconds)
    {
        return seconds is int value ? Math.Max(0, value) : null;
    }

    private static void ApplyBanListToSlots(IReadOnlyList<TeamSlot> slots, IReadOnlyList<int> banChampionIds)
    {
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].BanChampionId <= 0 && index < banChampionIds.Count)
                slots[index].BanChampionId = banChampionIds[index];

            if (slots[index].BanChampionId > 0)
                slots[index].BanChampionIntent = 0;
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

    private static string CreateSessionId()
    {
        return $"mock-champ-select-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private void ApplyLocalPlayerAssignedPosition(IReadOnlyList<TeamSlot> myTeam, IReadOnlyList<TeamSlot> theirTeam)
    {
        var localPlayer = myTeam
            .Concat(theirTeam)
            .FirstOrDefault(slot => slot.CellId == _localPlayerCellId);

        if (localPlayer is not null)
            localPlayer.AssignedPosition = _localPlayerAssignedPosition;
    }

    private void RefreshLocalPlayerAssignedPositionCore()
    {
        var localPlayer = FindTeamSlot(_localPlayerCellId);
        if (localPlayer is not null)
            _localPlayerAssignedPosition = localPlayer.AssignedPosition;
    }

    internal static string GetCustomTimedActionTypeDisplayName(TimedCustomActionType type)
    {
        return type switch
        {
            TimedCustomActionType.RoleSwap => "Role swap",
            TimedCustomActionType.ChampionSwap => "Champion swap",
            TimedCustomActionType.PickOrderSwap => "Pick order swap",
            _ => "Optional action"
        };
    }

    private static int NormalizeLocalPlayerCellId(int cellId)
    {
        return Math.Clamp(cellId, FirstPlayerCellId, LastPlayerCellId);
    }

    private static bool IsValidCellId(int cellId)
    {
        return cellId is >= FirstPlayerCellId and <= LastPlayerCellId;
    }

    private static bool IsBlueTeamCellId(int cellId)
    {
        return cellId is >= FirstPlayerCellId and <= LastBluePlayerCellId;
    }

    private static bool IsEnemyTeamCellId(int cellId, int localPlayerCellId)
    {
        return IsBlueTeamCellId(cellId) != IsBlueTeamCellId(localPlayerCellId);
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

    private readonly record struct TimedPickSchedule(
        int TargetChampionId,
        int? HoverAtSeconds,
        int? LockAtSeconds)
    {
        public static TimedPickSchedule FromAction(ChampSelectAction action)
        {
            return new TimedPickSchedule(
                action.TargetChampionId,
                action.HoverAtSeconds,
                action.LockAtSeconds);
        }

        public void ApplyTo(ChampSelectAction action)
        {
            action.TargetChampionId = TargetChampionId;
            action.HoverAtSeconds = HoverAtSeconds;
            action.LockAtSeconds = LockAtSeconds;
        }
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
    IReadOnlyList<ChampSelectAction> Actions,
    IReadOnlyList<TimedCustomAction> CustomTimedActions);

internal sealed record DraftTimerTickResult(
    bool StateChanged,
    int TimeLeftSeconds,
    IReadOnlyList<string> AppliedActions);

internal sealed record DraftPlaybackAdvanceResult(
    bool Advanced,
    DraftPickStep Step,
    int TimeLeftSeconds,
    bool ContinuesPlayback);

internal sealed record DraftStepState(
    string TimerPhase,
    int TimeLeftSeconds,
    IReadOnlyList<TeamSlot> MyTeam,
    IReadOnlyList<TeamSlot> TheirTeam,
    IReadOnlyList<int> MyTeamBans,
    IReadOnlyList<int> TheirTeamBans,
    IReadOnlyList<ChampSelectAction> Actions,
    IReadOnlyList<TimedCustomAction> CustomTimedActions);

internal sealed class TeamSlot
{
    public TeamSlot()
    {
    }

    public TeamSlot(
        int cellId,
        string assignedPosition,
        int championId,
        int championPickIntent,
        int banChampionIntent,
        int banChampionId)
    {
        CellId = cellId;
        AssignedPosition = assignedPosition;
        ChampionId = championId;
        ChampionPickIntent = championPickIntent;
        BanChampionIntent = banChampionIntent;
        BanChampionId = banChampionId;
    }

    public int CellId { get; set; }
    public string AssignedPosition { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public int ChampionPickIntent { get; set; }
    public int BanChampionIntent { get; set; }
    public int BanChampionId { get; set; }

    public TeamSlot Clone()
    {
        return new TeamSlot(CellId, AssignedPosition, ChampionId, ChampionPickIntent, BanChampionIntent, BanChampionId);
    }
}

internal sealed class ChampSelectAction
{
    public ChampSelectAction()
    {
    }

    public ChampSelectAction(
        int id,
        int actorCellId,
        string type,
        int championId,
        bool IsInProgress,
        bool Completed,
        int targetChampionId = 0,
        int? hoverAtSeconds = null,
        int? lockAtSeconds = null)
    {
        Id = id;
        ActorCellId = actorCellId;
        Type = type;
        ChampionId = championId;
        this.IsInProgress = IsInProgress;
        this.Completed = Completed;
        TargetChampionId = targetChampionId;
        HoverAtSeconds = hoverAtSeconds;
        LockAtSeconds = lockAtSeconds;
    }

    public int Id { get; set; }
    public int ActorCellId { get; set; }
    public string Type { get; set; } = "pick";
    public int ChampionId { get; set; }
    public bool IsInProgress { get; set; }
    public bool Completed { get; set; }
    public int TargetChampionId { get; set; }
    public int? HoverAtSeconds { get; set; }
    public int? LockAtSeconds { get; set; }

    public ChampSelectAction Clone()
    {
        return new ChampSelectAction(
            Id,
            ActorCellId,
            Type,
            ChampionId,
            IsInProgress,
            Completed,
            TargetChampionId,
            HoverAtSeconds,
            LockAtSeconds);
    }
}

internal enum TimedCustomActionType
{
    RoleSwap,
    ChampionSwap,
    PickOrderSwap
}

internal sealed class TimedCustomAction
{
    public TimedCustomAction()
    {
    }

    public TimedCustomAction(
        int id,
        TimedCustomActionType type,
        int sourceCellId,
        int targetCellId,
        int triggerAtSeconds,
        bool applied,
        bool skipped)
    {
        Id = id;
        Type = type;
        SourceCellId = sourceCellId;
        TargetCellId = targetCellId;
        TriggerAtSeconds = triggerAtSeconds;
        Applied = applied;
        Skipped = skipped;
    }

    public int Id { get; set; }
    public TimedCustomActionType Type { get; set; } = TimedCustomActionType.RoleSwap;
    public int SourceCellId { get; set; } = 1;
    public int TargetCellId { get; set; } = 2;
    public int TriggerAtSeconds { get; set; }
    public bool Applied { get; set; }
    public bool Skipped { get; set; }
    public string StatusText => Skipped ? "Skipped" : Applied ? "Applied" : "Pending";

    public TimedCustomAction Clone()
    {
        return new TimedCustomAction(
            Id,
            Type,
            SourceCellId,
            TargetCellId,
            TriggerAtSeconds,
            Applied,
            Skipped);
    }
}
