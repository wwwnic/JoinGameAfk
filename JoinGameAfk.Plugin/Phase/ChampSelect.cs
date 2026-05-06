using System.Net;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
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
        int ActionId,
        int ChampionId,
        int LockDelaySeconds,
        DateTime LockAtUtc,
        CancellationTokenSource CancellationTokenSource);

    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly Action<string>? _log;
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
    private DateTime _pickHoverReadyAtUtc;
    private DateTime _banHoverReadyAtUtc;
    private int _pickRetryStateActionId;
    private int _banRetryStateActionId;
    private readonly HashSet<int> _failedPickChampionIds = [];
    private readonly HashSet<int> _failedBanChampionIds = [];
    private ScheduledLockState? _scheduledPickLock;
    private ScheduledLockState? _scheduledBanLock;

    public ClientPhase ClientPhase => ClientPhase.ChampSelect;

    public DashboardStatus LastDashboardStatus { get; private set; } = new();

    public ChampSelect(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await _http.GetChampSelectSessionAsync(cancellationToken);
            DateTime sessionObservedAtUtc = DateTime.UtcNow;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? sessionId = GetSessionId(root);
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
            TimerSnapshot timerSnapshot = GetTimerSnapshot(root, sessionObservedAtUtc);
            string champSelectPhase = timerSnapshot.Phase;
            long timeLeftMs = GetEffectiveTimeLeftMs(sessionId, timerSnapshot, out DateTime timeLeftObservedAtUtc);
            bool championSelectAutomationEnabled = _settings.IsChampionSelectAutomationActive();

            if (!_hasLoggedSessionSummary)
            {
                Log($"Champ Select session ready. Position={assignedPosition}, picks=[{FormatChampionIds(mergedPickIds)}], bans=[{FormatChampionIds(mergedBanIds)}], automation={championSelectAutomationEnabled}, autoHover={_settings.AutoHoverChampionEnabled}, autoLock={_settings.AutoLockSelectionEnabled}, pickLockDelay={_settings.PickLockDelaySeconds}s, banLockDelay={_settings.BanLockDelaySeconds}s.");
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

                        if (type == "pick" && localPickActionId == 0)
                            localPickActionId = actionId;

                        if (type == "ban" && localBanActionId == 0)
                            localBanActionId = actionId;

                        if (championSelectAutomationEnabled && type == "pick" && mergedPickIds.Count > 0 && !_hasPicked)
                        {
                            await HandlePickActionAsync(root, localPlayerCellId, actionId, currentChampionId, isInProgress, timeLeftMs, timeLeftObservedAtUtc, mergedPickIds, cancellationToken);
                        }
                        else if (championSelectAutomationEnabled && type == "ban" && mergedBanIds.Count > 0 && !_hasBanned)
                        {
                            await HandleBanActionAsync(root, localPlayerCellId, actionId, currentChampionId, isInProgress, champSelectPhase, timeLeftMs, timeLeftObservedAtUtc, mergedBanIds, cancellationToken);
                        }
                    }
                }
            }

            LastDashboardStatus = BuildDashboardStatus(root, localPlayerCellId, localPickActionId, localBanActionId, pickChoices, banChoices, assignedPosition, champSelectPhase, timeLeftMs, timeLeftObservedAtUtc, localPlayerActiveActionType);
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

    private DashboardStatus BuildDashboardStatus(JsonElement root, int localPlayerCellId, int pickActionId, int banActionId, IReadOnlyList<ChampionPlanChoice> pickChoices, IReadOnlyList<ChampionPlanChoice> banChoices, Position assignedPosition, string champSelectPhase, long timeLeftMs, DateTime timeLeftObservedAtUtc, string? localPlayerActiveActionType)
    {
        return new DashboardStatus
        {
            CurrentPosition = assignedPosition,
            MyTeamSlots = BuildTeamSlotItems(root, "myTeam", localPlayerCellId),
            TheirTeamSlots = BuildTeamSlotItems(root, "theirTeam", localPlayerCellId),
            MyTeamBans = BuildTeamBanItems(root, "myTeamBans", "myTeam"),
            TheirTeamBans = BuildTeamBanItems(root, "theirTeamBans", "theirTeam"),
            PickChampionPriority = BuildChampionPlanItems(root, localPlayerCellId, pickActionId, pickChoices, _failedPickChampionIds, "Pick"),
            BanChampionPriority = BuildChampionPlanItems(root, localPlayerCellId, banActionId, banChoices, _failedBanChampionIds, "Ban"),
            PickChampionText = "No picks configured",
            BanChampionText = "No bans configured",
            PickLockText = BuildLockText(_settings.PickLockDelaySeconds, _manualPickSelectionOverride),
            BanLockText = BuildLockText(_settings.BanLockDelaySeconds, _manualBanSelectionOverride),
            ChampSelectSubPhase = GetSubPhaseLabel(champSelectPhase, localPlayerActiveActionType),
            TimeLeftSeconds = GetDisplayTimeLeftSeconds(timeLeftMs),
            TimeLeftMilliseconds = Math.Max(0, timeLeftMs),
            TimeLeftObservedAtUtc = timeLeftObservedAtUtc,
        };
    }

    private static IReadOnlyList<DashboardChampionPlanItem> BuildChampionPlanItems(JsonElement root, int localPlayerCellId, int actionId, IReadOnlyList<ChampionPlanChoice> championChoices, IReadOnlySet<int> failedChampionIds, string availableStatusText)
    {
        return championChoices
            .Select(choice =>
            {
                string? unavailableStatus = failedChampionIds.Contains(choice.ChampionId)
                    ? "Failed"
                    : GetChampionUnavailableStatus(root, localPlayerCellId, actionId, choice.ChampionId);

                return new DashboardChampionPlanItem
                {
                    ChampionId = choice.ChampionId,
                    Name = FormatChampion(choice.ChampionId),
                    SourcePosition = choice.SourcePosition,
                    IsAvailable = unavailableStatus is null,
                    StatusText = unavailableStatus ?? availableStatusText
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
                ChampionInitial = GetChampionInitial(championName, championId),
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

    private static string GetChampionInitial(string championName, int championId)
    {
        if (championId <= 0 || string.IsNullOrWhiteSpace(championName))
            return "?";

        return championName[..1].ToUpperInvariant();
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
        if (string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
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
            "FINALIZATION" => "Waiting",
            _ => "Waiting",
        };
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
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _banHoverReadyAtUtc = DateTime.MinValue;
        _pickRetryStateActionId = 0;
        _banRetryStateActionId = 0;
        _failedPickChampionIds.Clear();
        _failedBanChampionIds.Clear();
        CancelScheduledPickLock();
        CancelScheduledBanLock();
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

    private async Task HandlePickActionAsync(JsonElement root, int localPlayerCellId, int actionId, int currentChampionId, bool isInProgress, long timeLeftMs, DateTime timeLeftObservedAtUtc, IReadOnlyCollection<int> preferredChampionIds, CancellationToken cancellationToken)
    {
        EnsureRetryStateForAction(actionId, isPickAction: true);

        if (_hasHoveredPick
            && !_manualPickSelectionOverride
            && _hoveredPickChampionId != 0
            && IsChampionUnavailable(root, localPlayerCellId, actionId, _hoveredPickChampionId))
        {
            _failedPickChampionIds.Add(_hoveredPickChampionId);
            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(_hoveredPickChampionId)} is no longer available. Trying next configured champion.");
            ResetPickHover();
            _pendingPickHoverActionId = actionId;
            _pickHoverReadyAtUtc = DateTime.UtcNow;
        }

        if (currentChampionId == 0 && _hasHoveredPick && !_manualPickSelectionOverride)
        {
            ResetPickHover();
        }
        else if (currentChampionId != 0)
        {
            if (_hasHoveredPick && currentChampionId != _hoveredPickChampionId && !_manualPickSelectionOverride)
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
            _pickHoverReadyAtUtc = DateTime.MinValue;
            LogStatus(ref _lastPickStatusMessage, $"Pick action detected. Auto-hover is disabled, so the app is waiting for your manual champion selection.");
        }
        else if (!_hasHoveredPick && !_manualPickSelectionOverride)
        {
            if (ShouldAttemptHover(actionId, isPickAction: true, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                await TryHoverChampionAsync(root, localPlayerCellId, actionId, preferredChampionIds, _failedPickChampionIds, isPickAction: true, actionLabel: "Pick", cancellationToken);
            }
            else
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick action detected. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Waiting {hoverDelaySeconds}s before hovering.");
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
            ScheduleLock(actionId, championIdToLock, pickLockDelaySeconds, pickLockAtUtc, isPickAction: true, cancellationToken);
            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(championIdToLock)}. Scheduled lock at <= {pickLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        try
        {
            CancelScheduledPickLock();
            LogStatus(ref _lastPickStatusMessage, $"Pick lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            await _http.CompleteActionAsync(actionId, championIdToLock, cancellationToken);
            _hasPicked = true;
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
            _pickHoverReadyAtUtc = DateTime.UtcNow;
        }
    }

    private async Task HandleBanActionAsync(JsonElement root, int localPlayerCellId, int actionId, int currentChampionId, bool isInProgress, string champSelectPhase, long timeLeftMs, DateTime timeLeftObservedAtUtc, IReadOnlyCollection<int> preferredChampionIds, CancellationToken cancellationToken)
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
            _banHoverReadyAtUtc = DateTime.UtcNow;
        }

        if (currentChampionId == 0 && _hasHoveredBan && !_manualBanSelectionOverride)
        {
            ResetBanHover();
        }
        else if (currentChampionId != 0)
        {
            if (_hasHoveredBan && currentChampionId != _hoveredBanChampionId && !_manualBanSelectionOverride)
            {
                _manualBanSelectionOverride = true;
                LogStatus(ref _lastBanStatusMessage, $"Ban selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredBan = true;
            _hoveredBanChampionId = currentChampionId;
        }

        if (!_hasHoveredBan && !_manualBanSelectionOverride && !_settings.AutoHoverChampionEnabled && !string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
        {
            _pendingBanHoverActionId = 0;
            _banHoverReadyAtUtc = DateTime.MinValue;
            LogStatus(ref _lastBanStatusMessage, $"Ban action detected. Auto-hover is disabled, so the app is waiting for your manual champion selection.");
        }
        else if (!_hasHoveredBan && !_manualBanSelectionOverride && !string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldAttemptHover(actionId, isPickAction: false, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                await TryHoverChampionAsync(root, localPlayerCellId, actionId, preferredChampionIds, _failedBanChampionIds, isPickAction: false, actionLabel: "Ban", cancellationToken);
            }
            else
            {
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

            if (!string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
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
            ScheduleLock(actionId, championIdToLock, banLockDelaySeconds, banLockAtUtc, isPickAction: false, cancellationToken);
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
            _banHoverReadyAtUtc = DateTime.UtcNow;
        }
    }

    private void ScheduleLock(int actionId, int championId, int lockDelaySeconds, DateTime lockAtUtc, bool isPickAction, CancellationToken cancellationToken)
    {
        ScheduledLockState? currentSchedule = isPickAction ? _scheduledPickLock : _scheduledBanLock;
        if (currentSchedule is not null
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
        var scheduledLock = new ScheduledLockState(actionId, championId, lockDelaySeconds, lockAtUtc, linkedCts);
        if (isPickAction)
            _scheduledPickLock = scheduledLock;
        else
            _scheduledBanLock = scheduledLock;

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
        string actionLabel = isPickAction ? "Pick" : "Ban";
        Log($"{actionLabel} scheduled lock window reached. Locking {FormatChampion(scheduledLock.ChampionId)} on actionId={scheduledLock.ActionId}.");
        await _http.CompleteActionAsync(scheduledLock.ActionId, scheduledLock.ChampionId, cancellationToken);

        if (isPickAction)
            _hasPicked = true;
        else
            _hasBanned = true;

        Log($"{actionLabel} locked successfully. Champion={FormatChampion(scheduledLock.ChampionId)}, actionId={scheduledLock.ActionId}.");
    }

    private void CancelScheduledPickLock()
    {
        _scheduledPickLock?.CancellationTokenSource.Cancel();
        _scheduledPickLock = null;
    }

    private void CancelScheduledBanLock()
    {
        _scheduledBanLock?.CancellationTokenSource.Cancel();
        _scheduledBanLock = null;
    }

    private void ClearScheduledLockIfCurrent(ScheduledLockState scheduledLock, bool isPickAction)
    {
        if (isPickAction)
        {
            if (ReferenceEquals(_scheduledPickLock, scheduledLock))
                _scheduledPickLock = null;

            return;
        }

        if (ReferenceEquals(_scheduledBanLock, scheduledLock))
            _scheduledBanLock = null;
    }

    private async Task TryHoverChampionAsync(JsonElement root, int localPlayerCellId, int actionId, IReadOnlyCollection<int> championIds, HashSet<int> excludedChampionIds, bool isPickAction, string actionLabel, CancellationToken cancellationToken)
    {
        foreach (var championId in championIds)
        {
            if (excludedChampionIds.Contains(championId))
                continue;

            string? unavailableStatus = GetChampionUnavailableStatus(root, localPlayerCellId, actionId, championId, includeLocalPlayerTeamSelection: !isPickAction);
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
        _hasHoveredPick = false;
        _manualPickSelectionOverride = false;
        _hoveredPickChampionId = 0;
        _pendingPickHoverActionId = 0;
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _lastPickStatusMessage = null;
    }

    private void ResetBanHover()
    {
        CancelScheduledBanLock();
        _hasHoveredBan = false;
        _manualBanSelectionOverride = false;
        _hoveredBanChampionId = 0;
        _pendingBanHoverActionId = 0;
        _banHoverReadyAtUtc = DateTime.MinValue;
        _lastBanStatusMessage = null;
    }

    private bool ShouldAttemptHover(int actionId, bool isPickAction, out int hoverDelaySeconds)
    {
        hoverDelaySeconds = Math.Max(0, _settings.ChampionHoverDelaySeconds);
        DateTime now = DateTime.UtcNow;

        if (isPickAction)
        {
            if (_pendingPickHoverActionId != actionId)
            {
                _pendingPickHoverActionId = actionId;
                _pickHoverReadyAtUtc = now.AddSeconds(hoverDelaySeconds);
            }

            return now >= _pickHoverReadyAtUtc;
        }

        if (_pendingBanHoverActionId != actionId)
        {
            _pendingBanHoverActionId = actionId;
            _banHoverReadyAtUtc = now.AddSeconds(hoverDelaySeconds);
        }

        return now >= _banHoverReadyAtUtc;
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
