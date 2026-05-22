using System.Net;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using LcuClient;
using static LcuClient.Lcu;

public class ChampSelect : IPhaseHandler
{
    private sealed record ChampionPlanChoice(int ChampionId, Position SourcePosition);

    private sealed record TimerSnapshot(
        string Phase,
        long TotalTimeInPhaseMs,
        long TimeLeftMs,
        long? InternalNowInEpochMs,
        bool IsInfinite,
        DateTime ObservedAtUtc);

    private sealed record ScheduledLockState(
        string SessionId,
        int ActionId,
        int ChampionId,
        int LockDelaySeconds,
        DateTime LockAtUtc,
        CancellationTokenSource CancellationTokenSource);

    private sealed record LockSoundAlertSchedule(string AlertId, int ThresholdSeconds, int Urgency);

    private sealed record ActiveLockCountdownStatus(
        string ActionType,
        long TimeLeftMilliseconds,
        DateTime ObservedAtUtc);

    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly LeagueChampionOwnershipService _ownershipService;
    private readonly Action<string>? _log;
    private readonly Action? _requestRefresh;
    private readonly Action<SoundAlertPlaybackRequest>? _playSoundAlert;
    private string? _lastSessionId;
    private bool _hasPicked;
    private bool _hasBanned;
    private bool _hasHoveredPick;
    private bool _hasHoveredBan;
    private bool _manualPickSelectionOverride;
    private bool _manualBanSelectionOverride;
    private int _hoveredPickChampionId;
    private int _hoveredBanChampionId;
    private bool _hasLoggedSessionSummary;
    private string? _lastPickStatusMessage;
    private string? _lastBanStatusMessage;
    private string? _lastTimerSessionId;
    private string? _lastTimerPhase;
    private long _lastReportedTimeLeftMs;
    private long _lastEffectiveTimeLeftMs;
    private DateTime _lastTimerObservedAtUtc;
    private Position _lastAssignedPosition = Position.None;
    private int _pendingPickHoverActionId;
    private int _pendingBanHoverActionId;
    private string _pendingPickHoverPhase = string.Empty;
    private string _pendingBanHoverPhase = string.Empty;
    private DateTime _pickHoverReadyAtUtc;
    private DateTime _banHoverReadyAtUtc;
    private int _pickRetryStateActionId;
    private int _banRetryStateActionId;
    private readonly HashSet<int> _failedPickChampionIds = [];
    private readonly HashSet<int> _failedBanChampionIds = [];
    private readonly HashSet<string> _playedSoundAlertKeys = [];
    private ScheduledLockState? _scheduledPickLock;
    private ScheduledLockState? _scheduledBanLock;
    private CancellationTokenSource? _scheduledHoverWake;
    private int _scheduledHoverWakeActionId;
    private string _scheduledHoverWakePhase = string.Empty;
    private DateTime _scheduledHoverWakeAtUtc;

    public ClientPhase ClientPhase => ClientPhase.ChampSelect;

    public DashboardStatus LastDashboardStatus { get; private set; } = new();

    public ChampSelect(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null, Action? requestRefresh = null, Action<SoundAlertPlaybackRequest>? playSoundAlert = null)
    {
        _http = http;
        _settings = settings;
        _ownershipService = new LeagueChampionOwnershipService(http, log);
        _log = log;
        _requestRefresh = requestRefresh;
        _playSoundAlert = playSoundAlert;
    }

    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await _http.GetChampSelectSessionAsync(cancellationToken);
            DateTime sessionObservedAtUtc = DateTime.UtcNow;
            await HandleSessionJsonCoreAsync(json, sessionObservedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"HandleAsync Token cancellation requested.");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"Champ Select handler error: {ex.Message}");
        }
    }

    public async Task HandleSessionJsonAsync(string json, DateTime sessionObservedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await HandleSessionJsonCoreAsync(json, sessionObservedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"HandleSessionJsonAsync Token cancellation requested.");
        }
        catch (Exception ex)
        {
            Log($"Champ Select event handler error: {ex.Message}");
        }
    }

    private async Task HandleSessionJsonCoreAsync(string json, DateTime sessionObservedAtUtc, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? sessionId = GetSessionId(root);
        string soundAlertSessionId = sessionId ?? "champ-select-session";
        if (!string.Equals(_lastSessionId, sessionId, StringComparison.Ordinal))
        {
            Reset();
            _lastSessionId = sessionId;
        }

        if (!TryGetInt32(root, "localPlayerCellId", out int localPlayerCellId))
            return;

        Position assignedPosition = GetAssignedPosition(root, localPlayerCellId);
        RefreshAssignedPosition(assignedPosition);

        var pickChoices = GetMergedPickChampionChoices(assignedPosition);
        var banChoices = GetMergedBanChampionChoices(assignedPosition);
        var mergedPickIds = pickChoices.Select(choice => choice.ChampionId).ToList();
        var mergedBanIds = banChoices.Select(choice => choice.ChampionId).ToList();
        ChampionOwnershipSnapshot ownershipSnapshot = await _ownershipService.GetSnapshotAsync(cancellationToken);
        TimerSnapshot timerSnapshot = GetTimerSnapshot(root, sessionObservedAtUtc);
        string champSelectPhase = timerSnapshot.Phase;
        long timeLeftMs = GetEffectiveTimeLeftMs(sessionId, timerSnapshot, out DateTime timeLeftObservedAtUtc);
        bool championSelectAutomationEnabled = _settings.IsChampionSelectAutomationActive();

        if (!_hasLoggedSessionSummary)
        {
            Log($"Champ Select session ready. Position={assignedPosition}, picks=[{FormatChampionIds(mergedPickIds)}], bans=[{FormatChampionIds(mergedBanIds)}], automation={championSelectAutomationEnabled}, autoHover={_settings.AutoHoverChampionEnabled}, autoLock={_settings.AutoLockSelectionEnabled}, hoverDelay={_settings.ChampionHoverDelaySeconds}s, planningHoverDelay={GetHoverDelaySeconds("PLANNING")}s, pickLockDelay={_settings.PickLockDelaySeconds}s, banLockDelay={_settings.BanLockDelaySeconds}s.");
            _hasLoggedSessionSummary = true;
        }

        string? localPlayerActiveActionType = null;
        int localPickActionId = 0;
        int localBanActionId = 0;

        if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionGroup in actions.EnumerateArray())
            {
                if (actionGroup.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var action in actionGroup.EnumerateArray())
                {
                    if (!TryGetInt32(action, "actorCellId", out int actorCellId) || actorCellId != localPlayerCellId)
                        continue;

                    if (!TryGetBool(action, "completed", out bool completed) || completed)
                        continue;

                    string type = action.TryGetProperty("type", out var typeProperty)
                        ? typeProperty.GetString() ?? string.Empty
                        : string.Empty;

                    if (!TryGetInt32(action, "id", out int actionId))
                        continue;

                    bool isInProgress = TryGetBool(action, "isInProgress", out bool inProgress) && inProgress;
                    int currentChampionId = TryGetInt32(action, "championId", out int championId)
                        ? championId
                        : 0;

                    if (isInProgress && localPlayerActiveActionType is null)
                        localPlayerActiveActionType = type;

                    if (isInProgress)
                        TryPlayActionStartedSoundAlert(soundAlertSessionId, actionId, type);

                    if (type == "pick" && localPickActionId == 0)
                        localPickActionId = actionId;

                    if (type == "ban" && localBanActionId == 0)
                        localBanActionId = actionId;

                    if (championSelectAutomationEnabled && type == "pick" && mergedPickIds.Count > 0 && !_hasPicked)
                    {
                        await HandlePickActionAsync(soundAlertSessionId, root, localPlayerCellId, actionId, currentChampionId, isInProgress, champSelectPhase, timeLeftMs, timeLeftObservedAtUtc, mergedPickIds, ownershipSnapshot, cancellationToken);
                    }
                    else if (championSelectAutomationEnabled && type == "ban" && mergedBanIds.Count > 0 && !_hasBanned)
                    {
                        await HandleBanActionAsync(soundAlertSessionId, root, localPlayerCellId, actionId, currentChampionId, isInProgress, champSelectPhase, timeLeftMs, timeLeftObservedAtUtc, mergedBanIds, cancellationToken);
                    }
                }
            }
        }

        LastDashboardStatus = BuildDashboardStatus(root, localPlayerCellId, localPickActionId, localBanActionId, pickChoices, banChoices, ownershipSnapshot, assignedPosition, champSelectPhase, timeLeftMs, timeLeftObservedAtUtc, localPlayerActiveActionType);
    }

    private DashboardStatus BuildDashboardStatus(JsonElement root, int localPlayerCellId, int pickActionId, int banActionId, IReadOnlyList<ChampionPlanChoice> pickChoices, IReadOnlyList<ChampionPlanChoice> banChoices, ChampionOwnershipSnapshot ownershipSnapshot, Position assignedPosition, string champSelectPhase, long timeLeftMs, DateTime timeLeftObservedAtUtc, string? localPlayerActiveActionType)
    {
        var activeLockCountdown = GetActiveLockCountdownStatus(champSelectPhase, localPlayerActiveActionType, timeLeftMs, timeLeftObservedAtUtc);

        return new DashboardStatus
        {
            CurrentPosition = assignedPosition,
            MyTeamSlots = BuildTeamSlotItems(root, "myTeam", localPlayerCellId),
            TheirTeamSlots = BuildTeamSlotItems(root, "theirTeam", localPlayerCellId),
            MyTeamBans = BuildTeamBanItems(root, "myTeamBans", "myTeam"),
            TheirTeamBans = BuildTeamBanItems(root, "theirTeamBans", "theirTeam"),
            PickChampionPriority = BuildChampionPlanItems(root, localPlayerCellId, pickActionId, pickChoices, _failedPickChampionIds, ownershipSnapshot, requiresOwnedChampion: true, availableStatusText: "Pick"),
            BanChampionPriority = BuildChampionPlanItems(root, localPlayerCellId, banActionId, banChoices, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, requiresOwnedChampion: false, availableStatusText: "Ban"),
            PickChampionText = "No picks configured",
            BanChampionText = "No bans configured",
            PickLockText = BuildLockText(_settings.PickLockDelaySeconds, _manualPickSelectionOverride),
            BanLockText = BuildLockText(_settings.BanLockDelaySeconds, _manualBanSelectionOverride),
            ChampSelectSubPhase = GetSubPhaseLabel(champSelectPhase, localPlayerActiveActionType),
            TimeLeftSeconds = GetDisplayTimeLeftSeconds(timeLeftMs),
            TimeLeftMilliseconds = Math.Max(0, timeLeftMs),
            TimeLeftObservedAtUtc = timeLeftObservedAtUtc,
            ActiveLockActionType = activeLockCountdown.ActionType,
            ActiveLockTimeLeftMilliseconds = activeLockCountdown.TimeLeftMilliseconds,
            ActiveLockTimeLeftObservedAtUtc = activeLockCountdown.ObservedAtUtc,
        };
    }

    private ActiveLockCountdownStatus GetActiveLockCountdownStatus(string champSelectPhase, string? localPlayerActiveActionType, long timeLeftMs, DateTime timeLeftObservedAtUtc)
    {
        if (IsPlanningPhase(champSelectPhase))
            return GetPlanningHoverCountdownStatus();

        if (!ShouldShowActiveLockCountdown())
            return new ActiveLockCountdownStatus(string.Empty, -1, DateTime.MinValue);

        return localPlayerActiveActionType switch
        {
            "pick" => new ActiveLockCountdownStatus(
                "Pick",
                GetMillisecondsUntilLock(timeLeftMs, GetLockDelaySeconds(_settings.PickLockDelaySeconds, _manualPickSelectionOverride)),
                timeLeftObservedAtUtc),
            "ban" => new ActiveLockCountdownStatus(
                "Ban",
                GetMillisecondsUntilLock(timeLeftMs, GetLockDelaySeconds(_settings.BanLockDelaySeconds, _manualBanSelectionOverride)),
                timeLeftObservedAtUtc),
            _ => new ActiveLockCountdownStatus(string.Empty, -1, DateTime.MinValue)
        };
    }

    private ActiveLockCountdownStatus GetPlanningHoverCountdownStatus()
    {
        if (!_settings.ChampionSelectAutomationEnabled
            || !_settings.AutoHoverChampionEnabled
            || _hasHoveredPick
            || _manualPickSelectionOverride
            || _pendingPickHoverActionId == 0
            || _pickHoverReadyAtUtc == DateTime.MinValue)
        {
            return new ActiveLockCountdownStatus(string.Empty, -1, DateTime.MinValue);
        }

        DateTime observedAtUtc = DateTime.UtcNow;
        long millisecondsUntilHover = Math.Max(0, (long)Math.Ceiling((_pickHoverReadyAtUtc - observedAtUtc).TotalMilliseconds));
        return new ActiveLockCountdownStatus("Hover", millisecondsUntilHover, observedAtUtc);
    }

    private bool ShouldShowActiveLockCountdown()
    {
        return _settings.ChampionSelectAutomationEnabled
            && _settings.AutoLockSelectionEnabled;
    }

    private static IReadOnlyList<DashboardChampionPlanItem> BuildChampionPlanItems(JsonElement root, int localPlayerCellId, int actionId, IReadOnlyList<ChampionPlanChoice> championChoices, IReadOnlySet<int> failedChampionIds, ChampionOwnershipSnapshot ownershipSnapshot, bool requiresOwnedChampion, string availableStatusText)
    {
        return championChoices
            .Select(choice =>
            {
                string? unavailableStatus;
                string unavailableReasonKind;

                if (failedChampionIds.Contains(choice.ChampionId))
                {
                    unavailableStatus = "Failed";
                    unavailableReasonKind = DashboardChampionAvailabilityReason.Failed;
                }
                else
                {
                    unavailableStatus = GetChampionUnavailableStatus(root, localPlayerCellId, actionId, choice.ChampionId);
                    unavailableReasonKind = GetUnavailableReasonKind(unavailableStatus);
                }

                if (unavailableStatus is null && requiresOwnedChampion)
                {
                    unavailableStatus = GetChampionOwnershipUnavailableStatus(ownershipSnapshot, choice.ChampionId);
                    unavailableReasonKind = GetUnavailableReasonKind(unavailableStatus);
                }

                return new DashboardChampionPlanItem
                {
                    ChampionId = choice.ChampionId,
                    Name = FormatChampion(choice.ChampionId),
                    SourcePosition = choice.SourcePosition,
                    IsAvailable = unavailableStatus is null,
                    StatusText = unavailableStatus ?? availableStatusText,
                    UnavailableReasonKind = unavailableReasonKind
                };
            })
            .ToList();
    }

    private IReadOnlyList<ChampionPlanChoice> GetMergedPickChampionChoices(Position position)
    {
        return GetMergedChampionChoices(position, preference => preference.PickChampionIds);
    }

    private IReadOnlyList<ChampionPlanChoice> GetMergedBanChampionChoices(Position position)
    {
        return GetMergedChampionChoices(position, preference => preference.BanChampionIds);
    }

    private IReadOnlyList<ChampionPlanChoice> GetMergedChampionChoices(Position position, Func<PositionPreference, List<int>> selector)
    {
        position = NormalizePreferencePosition(position);

        var roleIds = position != Position.Default
            && _settings.Preferences.TryGetValue(position, out var rolePreference)
            ? selector(rolePreference)
            : [];

        var defaultIds = _settings.Preferences.TryGetValue(Position.Default, out var defaultPreference)
            ? selector(defaultPreference)
            : [];

        if (roleIds.Count == 0)
        {
            return defaultIds
                .Select(championId => new ChampionPlanChoice(championId, Position.Default))
                .ToList();
        }

        var seen = new HashSet<int>();
        var choices = new List<ChampionPlanChoice>();

        foreach (int championId in roleIds)
        {
            if (seen.Add(championId))
                choices.Add(new ChampionPlanChoice(championId, position));
        }

        foreach (int championId in defaultIds)
        {
            if (seen.Add(championId))
                choices.Add(new ChampionPlanChoice(championId, Position.Default));
        }

        return choices;
    }

    private static Position NormalizePreferencePosition(Position position)
    {
        return position == Position.None
            ? Position.Default
            : position;
    }

    private string BuildLockText(int configuredDelaySeconds, bool useLastSecondFallback)
    {
        if (!_settings.IsChampionSelectAutomationActive())
            return "Automation disabled";

        if (!_settings.AutoLockSelectionEnabled)
            return "Auto-lock disabled";

        int lockDelaySeconds = GetLockDelaySeconds(configuredDelaySeconds, useLastSecondFallback);
        if (lockDelaySeconds <= 0)
            return "Locks immediately";

        string suffix = useLastSecondFallback ? " after manual selection" : string.Empty;
        return $"Locks at <= {lockDelaySeconds}s{suffix}";
    }

    private static IReadOnlyList<DashboardTeamSlotItem> BuildTeamSlotItems(JsonElement root, string teamPropertyName, int localPlayerCellId)
    {
        if (!root.TryGetProperty(teamPropertyName, out var teamMembers)
            || teamMembers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var slots = new List<DashboardTeamSlotItem>();
        foreach (var member in teamMembers.EnumerateArray())
        {
            Position position = GetAssignedPosition(member);
            int championId = GetCurrentChampionId(member);
            bool isLocalPlayer = TryGetInt32(member, "cellId", out int cellId) && cellId == localPlayerCellId;
            string championName = championId > 0 ? FormatChampion(championId) : "No champion";

            slots.Add(new DashboardTeamSlotItem
            {
                ChampionId = championId,
                ChampionName = championName,
                RoleName = GetPositionDisplayName(position),
                IsLocalPlayer = isLocalPlayer
            });
        }

        return slots;
    }

    private static int GetCurrentChampionId(JsonElement member)
    {
        if (TryGetInt32(member, "championId", out int championId) && championId > 0)
            return championId;

        return TryGetInt32(member, "championPickIntent", out int championPickIntent) && championPickIntent > 0
            ? championPickIntent
            : 0;
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
            _ => "None",
        };
    }

    private static IReadOnlyList<DashboardChampionPlanItem> BuildTeamBanItems(JsonElement root, string bansPropertyName, string teamPropertyName)
    {
        var teamCellIds = GetTeamCellIds(root, teamPropertyName);
        var activeOrCompletedBanChampionIds = GetBanChampionIdsFromActions(root, teamCellIds, includeInProgress: true);
        var championIds = GetBanChampionIdsFromBans(root, bansPropertyName);
        if (championIds.Count == 0)
            championIds = GetBanChampionIdsFromActions(root, teamCellIds, includeInProgress: false);

        var actionBanChampionIds = activeOrCompletedBanChampionIds.ToHashSet();

        return championIds
            .Where(championId => ChampionCatalog.TryGetById(championId, out _) || actionBanChampionIds.Contains(championId))
            .Select(championId => new DashboardChampionPlanItem
            {
                ChampionId = championId,
                Name = FormatChampion(championId)
            })
            .ToList();
    }

    private static List<int> GetBanChampionIdsFromBans(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty("bans", out var bans)
            && bans.ValueKind == JsonValueKind.Object
            && bans.TryGetProperty(propertyName, out var banIds))
        {
            return GetChampionIdsFromArray(banIds);
        }

        return root.TryGetProperty(propertyName, out var rootBanIds)
            ? GetChampionIdsFromArray(rootBanIds)
            : [];
    }

    private static List<int> GetBanChampionIdsFromActions(JsonElement root, IReadOnlySet<int> teamCellIds, bool includeInProgress)
    {
        if (teamCellIds.Count == 0
            || !root.TryGetProperty("actions", out var actions)
            || actions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var championIds = new List<int>();
        var seenChampionIds = new HashSet<int>();

        foreach (var actionGroup in actions.EnumerateArray())
        {
            if (actionGroup.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var action in actionGroup.EnumerateArray())
            {
                if (!TryGetInt32(action, "actorCellId", out int actorCellId)
                    || !teamCellIds.Contains(actorCellId)
                    || !TryGetInt32(action, "championId", out int championId)
                    || championId <= 0)
                {
                    continue;
                }

                bool completed = TryGetBool(action, "completed", out bool completedValue) && completedValue;
                bool inProgress = includeInProgress
                    && TryGetBool(action, "isInProgress", out bool inProgressValue)
                    && inProgressValue;

                if (!completed && !inProgress)
                    continue;

                string type = action.TryGetProperty("type", out var typeProperty)
                    ? typeProperty.GetString() ?? string.Empty
                    : string.Empty;

                if (!string.Equals(type, "ban", StringComparison.OrdinalIgnoreCase)
                    || !seenChampionIds.Add(championId))
                {
                    continue;
                }

                championIds.Add(championId);
            }
        }

        return championIds;
    }

    private static HashSet<int> GetTeamCellIds(JsonElement root, string teamPropertyName)
    {
        var cellIds = new HashSet<int>();
        if (!root.TryGetProperty(teamPropertyName, out var teamMembers)
            || teamMembers.ValueKind != JsonValueKind.Array)
        {
            return cellIds;
        }

        foreach (var member in teamMembers.EnumerateArray())
        {
            if (TryGetInt32(member, "cellId", out int cellId))
                cellIds.Add(cellId);
        }

        return cellIds;
    }

    private static List<int> GetChampionIdsFromArray(JsonElement championIdsElement)
    {
        if (championIdsElement.ValueKind != JsonValueKind.Array)
            return [];

        var championIds = new List<int>();
        var seenChampionIds = new HashSet<int>();
        foreach (var championIdElement in championIdsElement.EnumerateArray())
        {
            if (championIdElement.ValueKind != JsonValueKind.Number
                || !championIdElement.TryGetInt32(out int championId)
                || championId <= 0
                || !seenChampionIds.Add(championId))
            {
                continue;
            }

            championIds.Add(championId);
        }

        return championIds;
    }

    private static string GetSubPhaseLabel(string champSelectPhase, string? localPlayerActiveActionType)
    {
        if (IsPlanningPhase(champSelectPhase))
            return "Hover";

        if (localPlayerActiveActionType is not null)
        {
            return localPlayerActiveActionType switch
            {
                "ban" => "Ban",
                "pick" => "Pick",
                _ => "Waiting",
            };
        }

        return champSelectPhase.ToUpperInvariant() switch
        {
            "BAN_PICK" => "Waiting",
            "FINALIZATION" => "Finalization",
            _ => "Waiting",
        };
    }

    private static bool CanHoverPickNow(string champSelectPhase, bool isPickInProgress)
    {
        return isPickInProgress || IsPlanningPhase(champSelectPhase);
    }

    private static bool IsPlanningPhase(string champSelectPhase)
    {
        return string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatChampSelectPhase(string champSelectPhase)
    {
        return string.IsNullOrWhiteSpace(champSelectPhase)
            ? "unknown"
            : champSelectPhase;
    }

    public void Reset()
    {
        _lastSessionId = null;
        _hasPicked = false;
        _hasBanned = false;
        _hasHoveredPick = false;
        _hasHoveredBan = false;
        _manualPickSelectionOverride = false;
        _manualBanSelectionOverride = false;
        _hoveredPickChampionId = 0;
        _hoveredBanChampionId = 0;
        _hasLoggedSessionSummary = false;
        _lastPickStatusMessage = null;
        _lastBanStatusMessage = null;
        _lastTimerSessionId = null;
        _lastTimerPhase = null;
        _lastReportedTimeLeftMs = 0;
        _lastEffectiveTimeLeftMs = 0;
        _lastTimerObservedAtUtc = DateTime.MinValue;
        _lastAssignedPosition = Position.None;
        _pendingPickHoverActionId = 0;
        _pendingBanHoverActionId = 0;
        _pendingPickHoverPhase = string.Empty;
        _pendingBanHoverPhase = string.Empty;
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _banHoverReadyAtUtc = DateTime.MinValue;
        _pickRetryStateActionId = 0;
        _banRetryStateActionId = 0;
        _failedPickChampionIds.Clear();
        _failedBanChampionIds.Clear();
        _playedSoundAlertKeys.Clear();
        CancelScheduledPickLock();
        CancelScheduledBanLock();
        CancelScheduledHoverWake();
    }

    private void RefreshAssignedPosition(Position assignedPosition)
    {
        if (_lastAssignedPosition == assignedPosition)
            return;

        Position previousPosition = _lastAssignedPosition;
        _lastAssignedPosition = assignedPosition;

        ResetPickHover();
        ResetBanHover();
        _failedPickChampionIds.Clear();
        _failedBanChampionIds.Clear();

        if (previousPosition != Position.None)
            Log($"Position changed: {previousPosition} -> {assignedPosition}. Refreshing champion plan.");
    }

    private async Task HandlePickActionAsync(string sessionId, JsonElement root, int localPlayerCellId, int actionId, int currentChampionId, bool isInProgress, string champSelectPhase, long timeLeftMs, DateTime timeLeftObservedAtUtc, IReadOnlyCollection<int> preferredChampionIds, ChampionOwnershipSnapshot ownershipSnapshot, CancellationToken cancellationToken)
    {
        EnsureRetryStateForAction(actionId, isPickAction: true);
        bool canHoverPickNow = CanHoverPickNow(champSelectPhase, isInProgress);

        string? hoveredPickUnavailableStatus = _hoveredPickChampionId == 0
            ? null
            : GetPickChampionUnavailableStatus(root, localPlayerCellId, actionId, _hoveredPickChampionId, ownershipSnapshot);

        if (_hasHoveredPick
            && !_manualPickSelectionOverride
            && _hoveredPickChampionId != 0
            && hoveredPickUnavailableStatus is not null)
        {
            if (!string.Equals(hoveredPickUnavailableStatus, "Not owned", StringComparison.Ordinal))
                _failedPickChampionIds.Add(_hoveredPickChampionId);

            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(_hoveredPickChampionId)} is no longer available ({hoveredPickUnavailableStatus}). Trying next configured champion.");
            ResetPickHover();
            _pendingPickHoverActionId = actionId;
            _pendingPickHoverPhase = champSelectPhase;
            _pickHoverReadyAtUtc = DateTime.UtcNow;
        }

        if (currentChampionId == 0 && _hasHoveredPick && !_manualPickSelectionOverride)
        {
            ResetPickHover();
        }
        else if (currentChampionId != 0)
        {
            if (IsManualSelectionOverride(currentChampionId, _hasHoveredPick, _hoveredPickChampionId, _manualPickSelectionOverride))
            {
                _manualPickSelectionOverride = true;
                LogStatus(ref _lastPickStatusMessage, $"Pick selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredPick = true;
            _hoveredPickChampionId = currentChampionId;
        }

        if (!_hasHoveredPick && !_manualPickSelectionOverride && !_settings.AutoHoverChampionEnabled)
        {
            _pendingPickHoverActionId = 0;
            _pendingPickHoverPhase = string.Empty;
            _pickHoverReadyAtUtc = DateTime.MinValue;
            LogStatus(ref _lastPickStatusMessage, $"Pick action detected. Auto-hover is disabled, so the app is waiting for your manual champion selection.");
        }
        else if (!_hasHoveredPick && !_manualPickSelectionOverride)
        {
            if (!canHoverPickNow)
            {
                _pendingPickHoverActionId = 0;
                _pendingPickHoverPhase = string.Empty;
                _pickHoverReadyAtUtc = DateTime.MinValue;
                LogStatus(ref _lastPickStatusMessage, $"Pick action is not active yet. Phase={FormatChampSelectPhase(champSelectPhase)}, timeLeft={FormatTimeLeft(timeLeftMs)}. Waiting before hovering.");
                return;
            }

            string hoverActionLabel = IsPlanningPhase(champSelectPhase) ? "Hover" : "Pick";
            if (ShouldAttemptHover(actionId, champSelectPhase, isPickAction: true, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastPickStatusMessage, $"{hoverActionLabel} delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                await TryHoverChampionAsync(root, localPlayerCellId, actionId, preferredChampionIds, _failedPickChampionIds, ownershipSnapshot, isPickAction: true, actionLabel: hoverActionLabel, cancellationToken);
            }
            else
            {
                ScheduleHoverWake(actionId, champSelectPhase, _pickHoverReadyAtUtc, cancellationToken);
                LogStatus(ref _lastPickStatusMessage, $"{hoverActionLabel} action detected. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Waiting {hoverDelaySeconds}s before hovering.");
            }
        }

        int championIdToLock = currentChampionId != 0 ? currentChampionId : (_manualPickSelectionOverride ? 0 : _hoveredPickChampionId);

        if (championIdToLock == 0)
        {
            if (_manualPickSelectionOverride)
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick selection was changed manually. Waiting for your current champion selection before auto-locking.");
                return;
            }

            if (!_settings.AutoHoverChampionEnabled)
            {
                LogStatus(ref _lastPickStatusMessage, $"No pick selected yet. Auto-hover is disabled, so auto-lock will wait for your manual selection.");
                return;
            }

            LogStatus(ref _lastPickStatusMessage, $"Pick hover not set yet. ActionId={actionId}. Will retry with remaining configured champions.");
            return;
        }

        string? pickOwnershipUnavailableStatus = GetChampionOwnershipUnavailableStatus(ownershipSnapshot, championIdToLock);
        if (pickOwnershipUnavailableStatus is not null)
        {
            CancelScheduledPickLock();
            LogStatus(ref _lastPickStatusMessage, $"Pick target {FormatChampion(championIdToLock)} is blocked ({pickOwnershipUnavailableStatus}). The app will not lock this pick.");
            if (!_manualPickSelectionOverride)
            {
                ResetPickHover();
                _pendingPickHoverActionId = actionId;
                _pendingPickHoverPhase = champSelectPhase;
                _pickHoverReadyAtUtc = DateTime.UtcNow;
            }

            return;
        }

        if (!isInProgress)
        {
            CancelScheduledPickLock();
            Log($"Pick hovered {FormatChampion(championIdToLock)}. Waiting for pick action to become active. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        if (!_settings.AutoLockSelectionEnabled)
        {
            CancelScheduledPickLock();
            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(championIdToLock)}. Auto-lock is disabled, so the app will not lock before timer 0.");
            return;
        }

        int pickLockDelaySeconds = GetLockDelaySeconds(_settings.PickLockDelaySeconds, _manualPickSelectionOverride);
        long millisecondsUntilPickLock = GetMillisecondsUntilLock(timeLeftMs, pickLockDelaySeconds);
        DateTime pickLockAtUtc = timeLeftObservedAtUtc.AddMilliseconds(millisecondsUntilPickLock);
        if (millisecondsUntilPickLock > 0 && pickLockAtUtc > DateTime.UtcNow)
        {
            ScheduleLock(sessionId, actionId, championIdToLock, pickLockDelaySeconds, pickLockAtUtc, isPickAction: true, cancellationToken);
            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(championIdToLock)}. Scheduled lock at <= {pickLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        try
        {
            CancelScheduledPickLock();
            LogStatus(ref _lastPickStatusMessage, $"Pick lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            await _http.CompleteActionAsync(actionId, championIdToLock, cancellationToken);
            _hasPicked = true;
            TryPlayLockCompleteSoundAlert(sessionId, actionId, championIdToLock, isPickAction: true);
            Log($"Pick locked successfully. Champion={FormatChampion(championIdToLock)}, actionId={actionId}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"Pick lock failed for {FormatChampion(championIdToLock)} on actionId={actionId}: {ex.Message}");
            if (ShouldExcludeChampionAfterRequestFailure(ex))
                _failedPickChampionIds.Add(championIdToLock);

            ResetPickHover();
            _pendingPickHoverActionId = actionId;
            _pendingPickHoverPhase = champSelectPhase;
            _pickHoverReadyAtUtc = DateTime.UtcNow;
        }
    }

    private async Task HandleBanActionAsync(string sessionId, JsonElement root, int localPlayerCellId, int actionId, int currentChampionId, bool isInProgress, string champSelectPhase, long timeLeftMs, DateTime timeLeftObservedAtUtc, IReadOnlyCollection<int> preferredChampionIds, CancellationToken cancellationToken)
    {
        EnsureRetryStateForAction(actionId, isPickAction: false);

        if (_hasHoveredBan
            && !_manualBanSelectionOverride
            && _hoveredBanChampionId != 0
            && IsChampionUnavailable(root, localPlayerCellId, actionId, _hoveredBanChampionId, includeLocalPlayerTeamSelection: true))
        {
            _failedBanChampionIds.Add(_hoveredBanChampionId);
            LogStatus(ref _lastBanStatusMessage, $"Ban hovered {FormatChampion(_hoveredBanChampionId)} is no longer available. Trying next configured champion.");
            ResetBanHover();
            _pendingBanHoverActionId = actionId;
            _pendingBanHoverPhase = champSelectPhase;
            _banHoverReadyAtUtc = DateTime.UtcNow;
        }

        if (currentChampionId == 0 && _hasHoveredBan && !_manualBanSelectionOverride)
        {
            ResetBanHover();
        }
        else if (currentChampionId != 0)
        {
            if (IsManualSelectionOverride(currentChampionId, _hasHoveredBan, _hoveredBanChampionId, _manualBanSelectionOverride))
            {
                _manualBanSelectionOverride = true;
                LogStatus(ref _lastBanStatusMessage, $"Ban selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredBan = true;
            _hoveredBanChampionId = currentChampionId;
        }

        if (!_hasHoveredBan && !_manualBanSelectionOverride && !_settings.AutoHoverChampionEnabled && !IsPlanningPhase(champSelectPhase))
        {
            _pendingBanHoverActionId = 0;
            _pendingBanHoverPhase = string.Empty;
            _banHoverReadyAtUtc = DateTime.MinValue;
            LogStatus(ref _lastBanStatusMessage, $"Ban action detected. Auto-hover is disabled, so the app is waiting for your manual champion selection.");
        }
        else if (!_hasHoveredBan && !_manualBanSelectionOverride && !IsPlanningPhase(champSelectPhase))
        {
            if (ShouldAttemptHover(actionId, champSelectPhase, isPickAction: false, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                await TryHoverChampionAsync(root, localPlayerCellId, actionId, preferredChampionIds, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, isPickAction: false, actionLabel: "Ban", cancellationToken);
            }
            else
            {
                ScheduleHoverWake(actionId, champSelectPhase, _banHoverReadyAtUtc, cancellationToken);
                LogStatus(ref _lastBanStatusMessage, $"Ban action detected. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Waiting {hoverDelaySeconds}s before hovering.");
            }
        }

        int championIdToLock = currentChampionId != 0 ? currentChampionId : (_manualBanSelectionOverride ? 0 : _hoveredBanChampionId);

        if (championIdToLock == 0)
        {
            if (_manualBanSelectionOverride)
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban selection was changed manually. Waiting for your current champion selection before auto-locking.");
                return;
            }

            if (!IsPlanningPhase(champSelectPhase))
            {
                if (!_settings.AutoHoverChampionEnabled)
                {
                    LogStatus(ref _lastBanStatusMessage, $"No ban selected yet. Auto-hover is disabled, so auto-lock will wait for your manual selection.");
                    return;
                }

                LogStatus(ref _lastBanStatusMessage, $"Ban hover not set yet. ActionId={actionId}. Will retry with remaining configured champions.");
            }

            return;
        }

        if (!isInProgress)
        {
            CancelScheduledBanLock();
            Log($"Ban hovered {FormatChampion(championIdToLock)}. Waiting for ban action to become active. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        if (!_settings.AutoLockSelectionEnabled)
        {
            CancelScheduledBanLock();
            LogStatus(ref _lastBanStatusMessage, $"Ban hovered {FormatChampion(championIdToLock)}. Auto-lock is disabled, so the app will not lock before timer 0.");
            return;
        }

        int banLockDelaySeconds = GetLockDelaySeconds(_settings.BanLockDelaySeconds, _manualBanSelectionOverride);
        long millisecondsUntilBanLock = GetMillisecondsUntilLock(timeLeftMs, banLockDelaySeconds);
        DateTime banLockAtUtc = timeLeftObservedAtUtc.AddMilliseconds(millisecondsUntilBanLock);
        if (millisecondsUntilBanLock > 0 && banLockAtUtc > DateTime.UtcNow)
        {
            ScheduleLock(sessionId, actionId, championIdToLock, banLockDelaySeconds, banLockAtUtc, isPickAction: false, cancellationToken);
            LogStatus(ref _lastBanStatusMessage, $"Ban hovered {FormatChampion(championIdToLock)}. Scheduled lock at <= {banLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        string? unavailableStatus = GetChampionUnavailableStatus(root, localPlayerCellId, actionId, championIdToLock, includeLocalPlayerTeamSelection: true);
        if (unavailableStatus is not null)
        {
            LogStatus(ref _lastBanStatusMessage, $"Ban target {FormatChampion(championIdToLock)} is blocked ({unavailableStatus}). The app will not lock this ban.");
            if (!_manualBanSelectionOverride)
            {
                _failedBanChampionIds.Add(championIdToLock);
                ResetBanHover();
                _pendingBanHoverActionId = actionId;
                _pendingBanHoverPhase = champSelectPhase;
                _banHoverReadyAtUtc = DateTime.UtcNow;
            }

            return;
        }

        try
        {
            CancelScheduledBanLock();
            LogStatus(ref _lastBanStatusMessage, $"Ban lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            await _http.CompleteActionAsync(actionId, championIdToLock, cancellationToken);
            _hasBanned = true;
            TryPlayLockCompleteSoundAlert(sessionId, actionId, championIdToLock, isPickAction: false);
            Log($"Ban locked successfully. Champion={FormatChampion(championIdToLock)}, actionId={actionId}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"HandleBanActionAsync Token cancellation requested.");
        }
        catch (Exception ex)
        {
            Log($"Ban lock failed for {FormatChampion(championIdToLock)} on actionId={actionId}: {ex.Message}");
            if (ShouldExcludeChampionAfterRequestFailure(ex))
                _failedBanChampionIds.Add(championIdToLock);

            ResetBanHover();
            _pendingBanHoverActionId = actionId;
            _pendingBanHoverPhase = champSelectPhase;
            _banHoverReadyAtUtc = DateTime.UtcNow;
        }
    }

    private static bool IsManualSelectionOverride(int currentChampionId, bool hasAppHoveredChampion, int appHoveredChampionId, bool manualSelectionOverride)
    {
        return currentChampionId != 0
            && !manualSelectionOverride
            && (!hasAppHoveredChampion || currentChampionId != appHoveredChampionId);
    }

    private void ScheduleLock(string sessionId, int actionId, int championId, int lockDelaySeconds, DateTime lockAtUtc, bool isPickAction, CancellationToken cancellationToken)
    {
        ScheduledLockState? currentSchedule = isPickAction ? _scheduledPickLock : _scheduledBanLock;
        if (currentSchedule is not null
            && string.Equals(currentSchedule.SessionId, sessionId, StringComparison.Ordinal)
            && currentSchedule.ActionId == actionId
            && currentSchedule.ChampionId == championId
            && currentSchedule.LockDelaySeconds == lockDelaySeconds
            && Math.Abs((currentSchedule.LockAtUtc - lockAtUtc).TotalMilliseconds) < 250
            && !currentSchedule.CancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        if (isPickAction)
            CancelScheduledPickLock();
        else
            CancelScheduledBanLock();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var scheduledLock = new ScheduledLockState(sessionId, actionId, championId, lockDelaySeconds, lockAtUtc, linkedCts);
        if (isPickAction)
            _scheduledPickLock = scheduledLock;
        else
            _scheduledBanLock = scheduledLock;

        ScheduleLockSoundAlerts(scheduledLock, isPickAction);
        _ = RunScheduledLockAsync(scheduledLock, isPickAction);
    }

    private async Task RunScheduledLockAsync(ScheduledLockState scheduledLock, bool isPickAction)
    {
        string actionLabel = isPickAction ? "Pick" : "Ban";
        CancellationToken cancellationToken = scheduledLock.CancellationTokenSource.Token;

        try
        {
            TimeSpan delay = scheduledLock.LockAtUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            await CompleteScheduledLockAsync(scheduledLock, isPickAction, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log($"{actionLabel} scheduled lock failed for {FormatChampion(scheduledLock.ChampionId)} on actionId={scheduledLock.ActionId}: {ex.Message}");
        }
        finally
        {
            ClearScheduledLockIfCurrent(scheduledLock, isPickAction);
            scheduledLock.CancellationTokenSource.Dispose();
        }
    }

    private async Task CompleteScheduledLockAsync(ScheduledLockState scheduledLock, bool isPickAction, CancellationToken cancellationToken)
    {
        string actionLabel = GetActionLabel(isPickAction);
        StopLockSoundChannel(scheduledLock, isPickAction);
        Log($"{actionLabel} scheduled lock window reached. Locking {FormatChampion(scheduledLock.ChampionId)} on actionId={scheduledLock.ActionId}.");
        await _http.CompleteActionAsync(scheduledLock.ActionId, scheduledLock.ChampionId, cancellationToken);

        if (isPickAction)
            _hasPicked = true;
        else
            _hasBanned = true;

        TryPlayLockCompleteSoundAlert(scheduledLock.SessionId, scheduledLock.ActionId, scheduledLock.ChampionId, isPickAction);
        Log($"{actionLabel} locked successfully. Champion={FormatChampion(scheduledLock.ChampionId)}, actionId={scheduledLock.ActionId}.");
    }

    private void TryPlayActionStartedSoundAlert(string sessionId, int actionId, string actionType)
    {
        string? alertId = actionType switch
        {
            "pick" => SoundAlertIds.PickActionStart,
            "ban" => SoundAlertIds.BanActionStart,
            _ => null
        };

        if (alertId is null)
            return;

        TryPlaySoundAlertOnce(alertId, $"{sessionId}:{alertId}:{actionId}", $"{FormatActionType(actionType)} action sound alert");
    }

    private static string FormatActionType(string actionType)
    {
        return actionType switch
        {
            "pick" => "Pick",
            "ban" => "Ban",
            _ => "Champion select"
        };
    }

    private void ScheduleLockSoundAlerts(ScheduledLockState scheduledLock, bool isPickAction)
    {
        if (_playSoundAlert is null)
            return;

        var alertSchedules = GetActiveLockSoundAlertSchedules(isPickAction);
        if (alertSchedules.Count == 0)
            return;

        TryPreloadSoundAlert(
            isPickAction ? SoundAlertIds.PickLockComplete : SoundAlertIds.BanLockComplete,
            $"{GetActionLabel(isPickAction)} auto-lock complete sound preload");

        DateTime now = DateTime.UtcNow;
        for (int index = 0; index < alertSchedules.Count; index++)
        {
            var schedule = alertSchedules[index];
            TryPreloadSoundAlert(schedule.AlertId, $"{GetActionLabel(isPickAction)} auto-lock countdown sound preload");

            DateTime alertAtUtc = scheduledLock.LockAtUtc.AddSeconds(-schedule.ThresholdSeconds);
            DateTime alertUntilUtc = index + 1 < alertSchedules.Count
                ? scheduledLock.LockAtUtc.AddSeconds(-alertSchedules[index + 1].ThresholdSeconds)
                : scheduledLock.LockAtUtc;

            if (alertUntilUtc <= now)
                continue;

            int? playbackDurationSeconds = index + 1 < alertSchedules.Count
                ? null
                : GetLockSoundPlaybackDurationSeconds(alertAtUtc > now ? alertAtUtc : now, alertUntilUtc);
            if (playbackDurationSeconds is <= 0)
                continue;

            if (alertAtUtc <= now)
                TryPlayLockCountdownSoundAlert(scheduledLock, isPickAction, schedule.AlertId, playbackDurationSeconds);
            else
                _ = RunScheduledLockSoundAlertAsync(scheduledLock, isPickAction, schedule.AlertId, alertAtUtc, playbackDurationSeconds);
        }
    }

    private List<LockSoundAlertSchedule> GetActiveLockSoundAlertSchedules(bool isPickAction)
    {
        var countdownAlert = CreateLockSoundAlertSchedule(
            isPickAction ? SoundAlertIds.PickLockCountdown : SoundAlertIds.BanLockCountdown,
            SoundAlertDefaults.DefaultLockCountdownThresholdSeconds,
            urgency: 0);
        var closeAlert = CreateLockSoundAlertSchedule(
            isPickAction ? SoundAlertIds.PickLockSoon : SoundAlertIds.BanLockSoon,
            SoundAlertDefaults.DefaultLockSoonThresholdSeconds,
            urgency: 1);

        var schedules = new List<LockSoundAlertSchedule>();
        bool countdownAlertActive = _settings.IsSoundAlertActive(countdownAlert.AlertId);
        bool closeAlertActive = _settings.IsSoundAlertActive(closeAlert.AlertId);
        if (countdownAlertActive && (!closeAlertActive || countdownAlert.ThresholdSeconds > closeAlert.ThresholdSeconds))
            schedules.Add(countdownAlert);

        if (closeAlertActive)
            schedules.Add(closeAlert);

        return schedules
            .OrderByDescending(schedule => schedule.ThresholdSeconds)
            .ThenBy(schedule => schedule.Urgency)
            .ToList();
    }

    private LockSoundAlertSchedule CreateLockSoundAlertSchedule(string alertId, int fallbackThresholdSeconds, int urgency)
    {
        return new LockSoundAlertSchedule(
            alertId,
            _settings.GetSoundAlertThresholdSeconds(alertId) ?? fallbackThresholdSeconds,
            urgency);
    }

    private static int GetLockSoundPlaybackDurationSeconds(DateTime playbackStartsAtUtc, DateTime playbackEndsAtUtc)
    {
        return Math.Clamp(
            (int)Math.Ceiling((playbackEndsAtUtc - playbackStartsAtUtc).TotalSeconds),
            0,
            ChampSelectSettings.MaxSoundAlertPlaybackDurationSeconds);
    }

    private async Task RunScheduledLockSoundAlertAsync(ScheduledLockState scheduledLock, bool isPickAction, string alertId, DateTime alertAtUtc, int? playbackDurationSeconds)
    {
        CancellationToken cancellationToken = scheduledLock.CancellationTokenSource.Token;
        try
        {
            TimeSpan delay = alertAtUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            TryPlayLockCountdownSoundAlert(scheduledLock, isPickAction, alertId, playbackDurationSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            string actionLabel = isPickAction ? "Pick" : "Ban";
            Log($"{actionLabel} lock sound alert failed for {FormatChampion(scheduledLock.ChampionId)} on actionId={scheduledLock.ActionId}: {ex.Message}");
        }
    }

    private void TryPlayLockCompleteSoundAlert(string sessionId, int actionId, int championId, bool isPickAction)
    {
        string alertId = isPickAction ? SoundAlertIds.PickLockComplete : SoundAlertIds.BanLockComplete;
        TryPlaySoundAlertOnce(
            alertId,
            $"{sessionId}:{alertId}:{actionId}:{championId}",
            $"{GetActionLabel(isPickAction)} auto-lock sound alert");
    }

    private void TryPlayLockCountdownSoundAlert(ScheduledLockState scheduledLock, bool isPickAction, string alertId, int? playbackDurationSeconds)
    {
        if (!_settings.IsSoundAlertActive(alertId) || _playSoundAlert is null)
            return;

        _playSoundAlert(SoundAlertPlaybackRequest.PlayAlert(
            alertId,
            $"{GetActionLabel(isPickAction)} auto-lock countdown sound alert",
            GetLockSoundChannelKey(scheduledLock, isPickAction),
            playbackDurationSeconds));
    }

    private void TryPreloadSoundAlert(string alertId, string context)
    {
        if (!_settings.IsSoundAlertActive(alertId) || _playSoundAlert is null)
            return;

        _playSoundAlert(SoundAlertPlaybackRequest.PreloadAlert(alertId, context));
    }

    private void StopLockSoundChannel(ScheduledLockState scheduledLock, bool isPickAction)
    {
        _playSoundAlert?.Invoke(SoundAlertPlaybackRequest.StopChannel(GetLockSoundChannelKey(scheduledLock, isPickAction)));
    }

    private static string GetLockSoundChannelKey(ScheduledLockState scheduledLock, bool isPickAction)
    {
        string actionType = isPickAction ? "pick" : "ban";
        return $"champ-select-auto-lock:{scheduledLock.SessionId}:{scheduledLock.ActionId}:{actionType}";
    }

    private static string GetActionLabel(bool isPickAction)
    {
        return isPickAction ? "Pick" : "Ban";
    }

    private void TryPlaySoundAlertOnce(string alertId, string dedupeKey, string context, int? playbackDurationSeconds = null)
    {
        if (!_settings.IsSoundAlertActive(alertId) || _playSoundAlert is null)
            return;

        if (!_playedSoundAlertKeys.Add(dedupeKey))
            return;

        _playSoundAlert(SoundAlertPlaybackRequest.PlayAlert(alertId, context, playbackDurationSeconds: playbackDurationSeconds));
    }

    private void CancelScheduledPickLock()
    {
        if (_scheduledPickLock is not { } scheduledLock)
            return;

        StopLockSoundChannel(scheduledLock, isPickAction: true);
        scheduledLock.CancellationTokenSource.Cancel();
        _scheduledPickLock = null;
    }

    private void CancelScheduledBanLock()
    {
        if (_scheduledBanLock is not { } scheduledLock)
            return;

        StopLockSoundChannel(scheduledLock, isPickAction: false);
        scheduledLock.CancellationTokenSource.Cancel();
        _scheduledBanLock = null;
    }

    private void ScheduleHoverWake(int actionId, string champSelectPhase, DateTime wakeAtUtc, CancellationToken cancellationToken)
    {
        if (_requestRefresh is null || wakeAtUtc <= DateTime.UtcNow)
            return;

        string phaseKey = string.IsNullOrWhiteSpace(champSelectPhase)
            ? string.Empty
            : champSelectPhase;

        if (_scheduledHoverWake is not null
            && _scheduledHoverWakeActionId == actionId
            && string.Equals(_scheduledHoverWakePhase, phaseKey, StringComparison.OrdinalIgnoreCase)
            && Math.Abs((_scheduledHoverWakeAtUtc - wakeAtUtc).TotalMilliseconds) < 250
            && !_scheduledHoverWake.IsCancellationRequested)
        {
            return;
        }

        CancelScheduledHoverWake();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scheduledHoverWake = linkedCts;
        _scheduledHoverWakeActionId = actionId;
        _scheduledHoverWakePhase = phaseKey;
        _scheduledHoverWakeAtUtc = wakeAtUtc;
        _ = RunScheduledHoverWakeAsync(wakeAtUtc, linkedCts);
    }

    private async Task RunScheduledHoverWakeAsync(DateTime wakeAtUtc, CancellationTokenSource wakeCancellationTokenSource)
    {
        try
        {
            TimeSpan delay = wakeAtUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, wakeCancellationTokenSource.Token);

            _requestRefresh?.Invoke();
        }
        catch (OperationCanceledException) when (wakeCancellationTokenSource.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_scheduledHoverWake, wakeCancellationTokenSource))
            {
                _scheduledHoverWake = null;
                _scheduledHoverWakeActionId = 0;
                _scheduledHoverWakePhase = string.Empty;
                _scheduledHoverWakeAtUtc = DateTime.MinValue;
            }

            wakeCancellationTokenSource.Dispose();
        }
    }

    private void CancelScheduledHoverWake()
    {
        _scheduledHoverWake?.Cancel();
        _scheduledHoverWake = null;
        _scheduledHoverWakeActionId = 0;
        _scheduledHoverWakePhase = string.Empty;
        _scheduledHoverWakeAtUtc = DateTime.MinValue;
    }

    private void ClearScheduledLockIfCurrent(ScheduledLockState scheduledLock, bool isPickAction)
    {
        if (isPickAction)
        {
            if (ReferenceEquals(_scheduledPickLock, scheduledLock))
            {
                StopLockSoundChannel(scheduledLock, isPickAction);
                _scheduledPickLock = null;
            }

            return;
        }

        if (ReferenceEquals(_scheduledBanLock, scheduledLock))
        {
            StopLockSoundChannel(scheduledLock, isPickAction);
            _scheduledBanLock = null;
        }
    }

    private async Task TryHoverChampionAsync(JsonElement root, int localPlayerCellId, int actionId, IReadOnlyCollection<int> championIds, HashSet<int> excludedChampionIds, ChampionOwnershipSnapshot ownershipSnapshot, bool isPickAction, string actionLabel, CancellationToken cancellationToken)
    {
        foreach (var championId in championIds)
        {
            if (excludedChampionIds.Contains(championId))
                continue;

            string? unavailableStatus = isPickAction
                ? GetPickChampionUnavailableStatus(root, localPlayerCellId, actionId, championId, ownershipSnapshot)
                : GetChampionUnavailableStatus(root, localPlayerCellId, actionId, championId, includeLocalPlayerTeamSelection: true);
            if (unavailableStatus is not null)
            {
                Log($"{actionLabel}: skipping {FormatChampion(championId)} because it is unavailable ({unavailableStatus}).");
                continue;
            }

            try
            {
                Log($"{actionLabel}: trying {FormatChampion(championId)} on actionId={actionId}.");
                await _http.HoverChampionAsync(actionId, championId, cancellationToken);

                if (isPickAction)
                {
                    _hasHoveredPick = true;
                    _hoveredPickChampionId = championId;
                }
                else
                {
                    _hasHoveredBan = true;
                    _hoveredBanChampionId = championId;
                }

                CancelScheduledHoverWake();
                Log($"{actionLabel}: hover succeeded with {FormatChampion(championId)} on actionId={actionId}.");
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log($"TryHoverChampionAsync Token cancellation requested.");
            }
            catch (Exception ex)
            {
                if (ShouldExcludeChampionAfterRequestFailure(ex))
                    excludedChampionIds.Add(championId);

                Log($"{actionLabel}: hover failed for {FormatChampion(championId)} on actionId={actionId}: {ex.Message}");
            }
        }
    }

    private void EnsureRetryStateForAction(int actionId, bool isPickAction)
    {
        if (isPickAction)
        {
            if (_pickRetryStateActionId == actionId)
                return;

            _pickRetryStateActionId = actionId;
            _failedPickChampionIds.Clear();
            return;
        }

        if (_banRetryStateActionId == actionId)
            return;

        _banRetryStateActionId = actionId;
        _failedBanChampionIds.Clear();
    }

    private static bool IsChampionUnavailable(JsonElement root, int localPlayerCellId, int localActionId, int championId, bool includeLocalPlayerTeamSelection = false)
    {
        return GetChampionUnavailableStatus(root, localPlayerCellId, localActionId, championId, includeLocalPlayerTeamSelection) is not null;
    }

    private static string? GetPickChampionUnavailableStatus(JsonElement root, int localPlayerCellId, int localActionId, int championId, ChampionOwnershipSnapshot ownershipSnapshot)
    {
        return GetChampionUnavailableStatus(root, localPlayerCellId, localActionId, championId)
            ?? GetChampionOwnershipUnavailableStatus(ownershipSnapshot, championId);
    }

    private static string? GetChampionOwnershipUnavailableStatus(ChampionOwnershipSnapshot ownershipSnapshot, int championId)
    {
        return ownershipSnapshot.IsKnownUnowned(championId)
            ? "Not owned"
            : null;
    }

    private static string GetUnavailableReasonKind(string? unavailableStatus)
    {
        return unavailableStatus switch
        {
            "Not owned" => DashboardChampionAvailabilityReason.NotOwned,
            "Banned" => DashboardChampionAvailabilityReason.Banned,
            "Picked" or "Locked" => DashboardChampionAvailabilityReason.Selected,
            "Failed" => DashboardChampionAvailabilityReason.Failed,
            null => DashboardChampionAvailabilityReason.None,
            _ => DashboardChampionAvailabilityReason.Blocked
        };
    }

    private static string? GetChampionUnavailableStatus(JsonElement root, int localPlayerCellId, int localActionId, int championId, bool includeLocalPlayerTeamSelection = false)
    {
        if (championId == 0)
            return null;

        if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionGroup in actions.EnumerateArray())
            {
                if (actionGroup.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var action in actionGroup.EnumerateArray())
                {
                    if (!TryGetInt32(action, "championId", out int actionChampionId) || actionChampionId != championId)
                        continue;

                    if (TryGetInt32(action, "actorCellId", out int actorCellId)
                        && actorCellId == localPlayerCellId
                        && TryGetInt32(action, "id", out int actionId)
                        && actionId == localActionId)
                    {
                        continue;
                    }

                    string type = action.TryGetProperty("type", out var typeProperty)
                        ? typeProperty.GetString() ?? string.Empty
                        : string.Empty;

                    if (string.Equals(type, "pick", StringComparison.OrdinalIgnoreCase))
                    {
                        return TryGetBool(action, "completed", out bool pickCompleted) && pickCompleted
                            ? "Locked"
                            : "Picked";
                    }

                    if (string.Equals(type, "ban", StringComparison.OrdinalIgnoreCase)
                        && TryGetBool(action, "completed", out bool banCompleted)
                        && banCompleted)
                    {
                        return "Banned";
                    }
                }
            }
        }

        return GetChampionSelectedByAnotherPlayerStatus(root, "myTeam", championId, localPlayerCellId, includeLocalPlayerTeamSelection)
            ?? GetChampionSelectedByAnotherPlayerStatus(root, "theirTeam", championId, localPlayerCellId, includeLocalPlayerTeamSelection);
    }

    private static string? GetChampionSelectedByAnotherPlayerStatus(JsonElement root, string teamPropertyName, int championId, int localPlayerCellId, bool includeLocalPlayer)
    {
        if (!root.TryGetProperty(teamPropertyName, out var teamMembers) || teamMembers.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var member in teamMembers.EnumerateArray())
        {
            if (!includeLocalPlayer && TryGetInt32(member, "cellId", out int cellId) && cellId == localPlayerCellId)
                continue;

            if (TryGetInt32(member, "championId", out int memberChampionId) && memberChampionId == championId)
                return "Locked";

            if (TryGetInt32(member, "championPickIntent", out int championPickIntent) && championPickIntent == championId)
                return "Picked";
        }

        return null;
    }

    private static bool ShouldExcludeChampionAfterRequestFailure(Exception ex)
    {
        return ex is HttpRequestException
        {
            StatusCode: HttpStatusCode.BadRequest
                or HttpStatusCode.Conflict
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Gone
                or HttpStatusCode.NotFound
                or HttpStatusCode.UnprocessableEntity
        };
    }

    private void ResetPickHover()
    {
        CancelScheduledPickLock();
        CancelScheduledHoverWake();
        _hasHoveredPick = false;
        _manualPickSelectionOverride = false;
        _hoveredPickChampionId = 0;
        _pendingPickHoverActionId = 0;
        _pendingPickHoverPhase = string.Empty;
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _lastPickStatusMessage = null;
    }

    private void ResetBanHover()
    {
        CancelScheduledBanLock();
        CancelScheduledHoverWake();
        _hasHoveredBan = false;
        _manualBanSelectionOverride = false;
        _hoveredBanChampionId = 0;
        _pendingBanHoverActionId = 0;
        _pendingBanHoverPhase = string.Empty;
        _banHoverReadyAtUtc = DateTime.MinValue;
        _lastBanStatusMessage = null;
    }

    private bool ShouldAttemptHover(int actionId, string champSelectPhase, bool isPickAction, out int hoverDelaySeconds)
    {
        string phaseKey = string.IsNullOrWhiteSpace(champSelectPhase)
            ? string.Empty
            : champSelectPhase;
        hoverDelaySeconds = GetHoverDelaySeconds(phaseKey);
        DateTime now = DateTime.UtcNow;

        if (isPickAction)
        {
            if (_pendingPickHoverActionId != actionId || !string.Equals(_pendingPickHoverPhase, phaseKey, StringComparison.OrdinalIgnoreCase))
            {
                _pendingPickHoverActionId = actionId;
                _pendingPickHoverPhase = phaseKey;
                _pickHoverReadyAtUtc = now.AddSeconds(hoverDelaySeconds);
            }

            return now >= _pickHoverReadyAtUtc;
        }

        if (_pendingBanHoverActionId != actionId || !string.Equals(_pendingBanHoverPhase, phaseKey, StringComparison.OrdinalIgnoreCase))
        {
            _pendingBanHoverActionId = actionId;
            _pendingBanHoverPhase = phaseKey;
            _banHoverReadyAtUtc = now.AddSeconds(hoverDelaySeconds);
        }

        return now >= _banHoverReadyAtUtc;
    }

    private int GetHoverDelaySeconds(string champSelectPhase)
    {
        int configuredDelaySeconds = Math.Max(0, _settings.ChampionHoverDelaySeconds);

        return IsPlanningPhase(champSelectPhase)
            ? Math.Max(configuredDelaySeconds, Math.Max(0, _settings.PlanningHoverDelaySeconds))
            : configuredDelaySeconds;
    }

    private static Position GetAssignedPosition(JsonElement root, int localPlayerCellId)
    {
        if (root.TryGetProperty("myTeam", out var myTeam))
        {
            foreach (var member in myTeam.EnumerateArray())
            {
                if (!TryGetInt32(member, "cellId", out int cellId))
                    continue;

                if (cellId == localPlayerCellId)
                {
                    return GetAssignedPosition(member);
                }
            }
        }

        return Position.None;
    }

    private static Position GetAssignedPosition(JsonElement member)
    {
        string position = member.TryGetProperty("assignedPosition", out var assignedPositionProperty)
            ? assignedPositionProperty.GetString() ?? ""
            : "";

        return position.ToLowerInvariant() switch
        {
            "top" => Position.Top,
            "jungle" => Position.Jungle,
            "middle" => Position.Mid,
            "bottom" => Position.Adc,
            "utility" => Position.Support,
            _ => Position.None
        };
    }

    private static TimerSnapshot GetTimerSnapshot(JsonElement sessionRoot, DateTime sessionObservedAtUtc)
    {
        return TryCreateTimerSnapshot(sessionRoot, sessionObservedAtUtc, out var timerSnapshot)
            ? timerSnapshot
            : new TimerSnapshot(string.Empty, 0, 0, null, IsInfinite: false, DateTime.UtcNow);
    }

    private static bool TryCreateTimerSnapshot(JsonElement source, DateTime fallbackObservedAtUtc, out TimerSnapshot timerSnapshot)
    {
        timerSnapshot = new TimerSnapshot(string.Empty, 0, 0, null, IsInfinite: false, fallbackObservedAtUtc);

        if (!TryGetTimerElement(source, out var timer))
            return false;

        string phase = timer.TryGetProperty("phase", out var phaseProperty)
            ? phaseProperty.GetString() ?? string.Empty
            : string.Empty;

        long timeLeftMs = GetTimerTimeLeftMs(timer);
        long totalTimeInPhaseMs = TryGetNumberAsInt64(timer, "totalTimeInPhase", out long totalTime)
            ? totalTime
            : 0;
        long? internalNowInEpochMs = TryGetNumberAsInt64(timer, "internalNowInEpochMs", out long internalNow)
            ? internalNow
            : null;
        bool isInfinite = TryGetBool(timer, "isInfinite", out bool infinite) && infinite;
        DateTime observedAtUtc = GetTimerObservedAtUtc(internalNowInEpochMs, totalTimeInPhaseMs, fallbackObservedAtUtc);

        timerSnapshot = new TimerSnapshot(
            phase,
            Math.Max(0, totalTimeInPhaseMs),
            Math.Max(0, timeLeftMs),
            internalNowInEpochMs,
            isInfinite,
            observedAtUtc);
        return true;
    }

    private static bool TryGetTimerElement(JsonElement source, out JsonElement timer)
    {
        timer = default;
        if (source.ValueKind != JsonValueKind.Object)
            return false;

        if (source.TryGetProperty("timer", out var nestedTimer))
        {
            timer = nestedTimer;
            return timer.ValueKind == JsonValueKind.Object;
        }

        timer = source;
        return true;
    }

    private static long GetTimerTimeLeftMs(JsonElement timer)
    {
        if (TryGetNumberAsInt64(timer, "adjustedTimeLeftInPhase", out long adjustedTimeLeftMs))
            return adjustedTimeLeftMs;

        if (TryGetNumberAsInt64(timer, "timeLeftInPhase", out long timeLeftMs))
            return timeLeftMs;

        if (TryGetNumberAsInt64(timer, "adjustedTimeLeftInPhaseInSec", out long adjustedTimeLeftSeconds))
            return adjustedTimeLeftSeconds * 1000L;

        if (TryGetNumberAsInt64(timer, "timeLeftInPhaseInSec", out long timeLeftSeconds))
            return timeLeftSeconds * 1000L;

        return 0;
    }

    private long GetEffectiveTimeLeftMs(string? sessionId, TimerSnapshot timerSnapshot, out DateTime observedAtUtc)
    {
        DateTime now = DateTime.UtcNow;
        observedAtUtc = now;
        long timeLeftMs = GetPayloadAgeAdjustedTimeLeftMs(timerSnapshot);

        bool shouldResetBaseline = !string.Equals(_lastTimerSessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(_lastTimerPhase, timerSnapshot.Phase, StringComparison.Ordinal)
            || _lastTimerObservedAtUtc == DateTime.MinValue
            || timeLeftMs != _lastReportedTimeLeftMs;

        if (shouldResetBaseline)
        {
            _lastTimerSessionId = sessionId;
            _lastTimerPhase = timerSnapshot.Phase;
            _lastReportedTimeLeftMs = timeLeftMs;
            _lastEffectiveTimeLeftMs = timeLeftMs;
            _lastTimerObservedAtUtc = now;
            return timeLeftMs;
        }

        if (timerSnapshot.IsInfinite)
            return _lastEffectiveTimeLeftMs;

        long elapsedMs = (long)(now - _lastTimerObservedAtUtc).TotalMilliseconds;
        if (elapsedMs <= 0)
            return _lastEffectiveTimeLeftMs;

        _lastEffectiveTimeLeftMs = Math.Max(0, _lastEffectiveTimeLeftMs - elapsedMs);
        _lastTimerObservedAtUtc = now;
        return _lastEffectiveTimeLeftMs;
    }

    private static long GetPayloadAgeAdjustedTimeLeftMs(TimerSnapshot timerSnapshot)
    {
        long timeLeftMs = Math.Max(0, timerSnapshot.TimeLeftMs);
        if (timerSnapshot.IsInfinite)
            return timeLeftMs;

        long payloadAgeMs = (long)(DateTime.UtcNow - timerSnapshot.ObservedAtUtc).TotalMilliseconds;
        long maxExpectedPayloadAgeMs = Math.Max(timerSnapshot.TotalTimeInPhaseMs, 0)
            + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
        if (payloadAgeMs <= 0 || payloadAgeMs > maxExpectedPayloadAgeMs)
            return timeLeftMs;

        return Math.Max(0, timeLeftMs - payloadAgeMs);
    }

    private static DateTime GetTimerObservedAtUtc(long? internalNowInEpochMs, long totalTimeInPhaseMs, DateTime fallbackObservedAtUtc)
    {
        if (internalNowInEpochMs is not long epochMs)
            return fallbackObservedAtUtc;

        try
        {
            DateTime internalObservedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
            long payloadAgeMs = (long)(fallbackObservedAtUtc - internalObservedAtUtc).TotalMilliseconds;
            long maxExpectedPayloadAgeMs = Math.Max(totalTimeInPhaseMs, 0)
                + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;

            return payloadAgeMs >= 0 && payloadAgeMs <= maxExpectedPayloadAgeMs
                ? internalObservedAtUtc
                : fallbackObservedAtUtc;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallbackObservedAtUtc;
        }
    }

    private static long GetMillisecondsUntilLock(long timeLeftMs, int delaySeconds)
    {
        if (delaySeconds <= 0)
            return 0;

        long thresholdMs = delaySeconds * 1000L;
        return Math.Max(0, timeLeftMs - thresholdMs);
    }

    private static int GetLockDelaySeconds(int configuredDelaySeconds, bool useLastSecondFallback)
    {
        return useLastSecondFallback ? 1 : configuredDelaySeconds;
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
    }

    private void LogStatus(ref string? lastMessage, string message)
    {
        if (string.Equals(lastMessage, message, StringComparison.Ordinal))
            return;

        lastMessage = message;
        Log(message);
    }

    private static string FormatChampionIds(IReadOnlyCollection<int> championIds)
    {
        return championIds.Count == 0 ? "none" : string.Join(", ", championIds.Select(FormatChampion));
    }

    private static string FormatChampion(int championId)
    {
        return ChampionCatalog.FormatWithName(championId);
    }

    private static string FormatTimeLeft(long timeLeftMs)
    {
        if (timeLeftMs <= 0)
            return "0.0s";

        return $"{timeLeftMs / 1000d:F1}s";
    }

    private static int GetDisplayTimeLeftSeconds(long timeLeftMs)
    {
        if (timeLeftMs <= 0)
            return 0;

        return (int)(timeLeftMs / 1000L);
    }

    private static string? GetSessionId(JsonElement root)
    {
        if (root.TryGetProperty("multiUserChatId", out var chatIdProperty))
            return chatIdProperty.GetString();

        return null;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out value);
    }

    private static bool TryGetNumberAsInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (property.TryGetInt64(out value))
            return true;

        value = (long)property.GetDouble();
        return true;
    }

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
