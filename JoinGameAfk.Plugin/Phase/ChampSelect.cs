using System.Net;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using static LcuClient.Lcu;

public class ChampSelect : IPhaseHandler
{
    private sealed record ChampionPlanChoice(int ChampionId, Position SourcePosition);

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

    public ClientPhase ClientPhase => ClientPhase.ChampSelect;

    public DashboardStatus LastDashboardStatus { get; private set; } = new();

    public ChampSelect(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public void Handle()
    {
        try
        {
            string json = _http.GetChampSelectSession();
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
            string champSelectPhase = GetChampSelectPhase(root);
            long totalTimeMs = GetTotalTimeInPhaseMs(root);
            long rawTimeLeftMs = GetAdjustedTimeLeftMs(root);
            long timeLeftMs = GetEffectiveTimeLeftMs(sessionId, champSelectPhase, rawTimeLeftMs);

            if (!_hasLoggedSessionSummary)
            {
                Log($"Champ Select session ready. Position={assignedPosition}, picks=[{FormatChampionIds(mergedPickIds)}], bans=[{FormatChampionIds(mergedBanIds)}], autoLock={_settings.AutoLockSelectionEnabled}, pickLockDelay={_settings.PickLockDelaySeconds}s, banLockDelay={_settings.BanLockDelaySeconds}s.");
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

                        if (type == "pick" && mergedPickIds.Count > 0 && !_hasPicked)
                        {
                            HandlePickAction(root, localPlayerCellId, actionId, currentChampionId, isInProgress, totalTimeMs, timeLeftMs, mergedPickIds);
                        }
                        else if (type == "ban" && mergedBanIds.Count > 0 && !_hasBanned)
                        {
                            HandleBanAction(root, localPlayerCellId, actionId, currentChampionId, isInProgress, champSelectPhase, totalTimeMs, timeLeftMs, mergedBanIds);
                        }
                    }
                }
            }

            LastDashboardStatus = BuildDashboardStatus(root, localPlayerCellId, localPickActionId, localBanActionId, pickChoices, banChoices, assignedPosition, champSelectPhase, timeLeftMs, localPlayerActiveActionType);
        }
        catch (Exception ex)
        {
            Log($"Champ Select handler error: {ex.Message}");
        }
    }

    private DashboardStatus BuildDashboardStatus(JsonElement root, int localPlayerCellId, int pickActionId, int banActionId, IReadOnlyList<ChampionPlanChoice> pickChoices, IReadOnlyList<ChampionPlanChoice> banChoices, Position assignedPosition, string champSelectPhase, long timeLeftMs, string? localPlayerActiveActionType)
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
            TimeLeftSeconds = (int)(timeLeftMs / 1000),
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

    private void HandlePickAction(JsonElement root, int localPlayerCellId, int actionId, int currentChampionId, bool isInProgress, long totalTimeMs, long timeLeftMs, IReadOnlyCollection<int> preferredChampionIds)
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

        if (!_hasHoveredPick && !_manualPickSelectionOverride)
        {
            if (ShouldAttemptHover(actionId, isPickAction: true, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastPickStatusMessage, $"Pick hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                TryHoverChampion(root, localPlayerCellId, actionId, preferredChampionIds, _failedPickChampionIds, isPickAction: true, actionLabel: "Pick");
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

            LogStatus(ref _lastPickStatusMessage, $"Pick hover not set yet. ActionId={actionId}. Will retry with remaining configured champions.");
            return;
        }

        if (!isInProgress)
        {
            Log($"Pick hovered {FormatChampion(championIdToLock)}. Waiting for pick action to become active. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        if (!_settings.AutoLockSelectionEnabled)
        {
            LogStatus(ref _lastPickStatusMessage, $"Pick hovered {FormatChampion(championIdToLock)}. Auto-lock is disabled, so the app will not lock before timer 0.");
            return;
        }

        int pickLockDelaySeconds = GetLockDelaySeconds(_settings.PickLockDelaySeconds, _manualPickSelectionOverride);
        if (!ShouldLock(totalTimeMs, timeLeftMs, pickLockDelaySeconds))
        {
            Log($"Pick hovered {FormatChampion(championIdToLock)}. Waiting to lock at <= {pickLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        try
        {
            LogStatus(ref _lastPickStatusMessage, $"Pick lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            _http.CompleteAction(actionId, championIdToLock);
            _hasPicked = true;
            Log($"Pick locked successfully. Champion={FormatChampion(championIdToLock)}, actionId={actionId}.");
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

    private void HandleBanAction(JsonElement root, int localPlayerCellId, int actionId, int currentChampionId, bool isInProgress, string champSelectPhase, long totalTimeMs, long timeLeftMs, IReadOnlyCollection<int> preferredChampionIds)
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

        if (!_hasHoveredBan && !_manualBanSelectionOverride && !string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldAttemptHover(actionId, isPickAction: false, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban hover delay elapsed. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                TryHoverChampion(root, localPlayerCellId, actionId, preferredChampionIds, _failedBanChampionIds, isPickAction: false, actionLabel: "Ban");
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
                LogStatus(ref _lastBanStatusMessage, $"Ban hover not set yet. ActionId={actionId}. Will retry with remaining configured champions.");
            }

            return;
        }

        if (!isInProgress)
        {
            Log($"Ban hovered {FormatChampion(championIdToLock)}. Waiting for ban action to become active. Current time left: {FormatTimeLeft(timeLeftMs)}.");
            return;
        }

        if (!_settings.AutoLockSelectionEnabled)
        {
            LogStatus(ref _lastBanStatusMessage, $"Ban hovered {FormatChampion(championIdToLock)}. Auto-lock is disabled, so the app will not lock before timer 0.");
            return;
        }

        int banLockDelaySeconds = GetLockDelaySeconds(_settings.BanLockDelaySeconds, _manualBanSelectionOverride);
        if (!ShouldLock(totalTimeMs, timeLeftMs, banLockDelaySeconds))
        {
            Log($"Ban hovered {FormatChampion(championIdToLock)}. Waiting to lock at <= {banLockDelaySeconds}s. Current time left: {FormatTimeLeft(timeLeftMs)}.");
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
            LogStatus(ref _lastBanStatusMessage, $"Ban lock window reached. Locking {FormatChampion(championIdToLock)} on actionId={actionId}. Time left: {FormatTimeLeft(timeLeftMs)}.");
            _http.CompleteAction(actionId, championIdToLock);
            _hasBanned = true;
            Log($"Ban locked successfully. Champion={FormatChampion(championIdToLock)}, actionId={actionId}.");
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

    private void TryHoverChampion(JsonElement root, int localPlayerCellId, int actionId, IReadOnlyCollection<int> championIds, HashSet<int> excludedChampionIds, bool isPickAction, string actionLabel)
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
                _http.HoverChampion(actionId, championId);

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
        _hasHoveredPick = false;
        _manualPickSelectionOverride = false;
        _hoveredPickChampionId = 0;
        _pendingPickHoverActionId = 0;
        _pickHoverReadyAtUtc = DateTime.MinValue;
        _lastPickStatusMessage = null;
    }

    private void ResetBanHover()
    {
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

    private static string GetChampSelectPhase(JsonElement root)
    {
        if (root.TryGetProperty("timer", out var timer)
            && timer.ValueKind == JsonValueKind.Object
            && timer.TryGetProperty("phase", out var phaseProperty))
        {
            return phaseProperty.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static long GetAdjustedTimeLeftMs(JsonElement root)
    {
        if (root.TryGetProperty("timer", out var timer)
            && timer.ValueKind == JsonValueKind.Object
            && timer.TryGetProperty("adjustedTimeLeftInPhase", out var timeLeftProp)
            && timeLeftProp.ValueKind == JsonValueKind.Number)
        {
            // The LCU returns this value as a floating-point number (e.g. 88632.5).
            return (long)timeLeftProp.GetDouble();
        }

        return 0;
    }

    private long GetEffectiveTimeLeftMs(string? sessionId, string champSelectPhase, long rawTimeLeftMs)
    {
        DateTime now = DateTime.UtcNow;

        if (rawTimeLeftMs < 0)
            rawTimeLeftMs = 0;

        bool shouldResetBaseline = !string.Equals(_lastTimerSessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(_lastTimerPhase, champSelectPhase, StringComparison.Ordinal)
            || _lastTimerObservedAtUtc == DateTime.MinValue
            || rawTimeLeftMs != _lastReportedTimeLeftMs;

        if (shouldResetBaseline)
        {
            _lastTimerSessionId = sessionId;
            _lastTimerPhase = champSelectPhase;
            _lastReportedTimeLeftMs = rawTimeLeftMs;
            _lastEffectiveTimeLeftMs = rawTimeLeftMs;
            _lastTimerObservedAtUtc = now;
            return rawTimeLeftMs;
        }

        long elapsedMs = (long)(now - _lastTimerObservedAtUtc).TotalMilliseconds;
        if (elapsedMs <= 0)
            return _lastEffectiveTimeLeftMs;

        _lastEffectiveTimeLeftMs = Math.Max(0, _lastEffectiveTimeLeftMs - elapsedMs);
        _lastTimerObservedAtUtc = now;
        return _lastEffectiveTimeLeftMs;
    }

    private static long GetTotalTimeInPhaseMs(JsonElement root)
    {
        if (root.TryGetProperty("timer", out var timer)
            && timer.ValueKind == JsonValueKind.Object
            && timer.TryGetProperty("totalTimeInPhase", out var totalTimeProp)
            && totalTimeProp.ValueKind == JsonValueKind.Number)
        {
            return (long)totalTimeProp.GetDouble();
        }

        return 0;
    }

    /// <summary>
    /// Returns true when the action should be locked in.
    /// A delaySeconds of 0 means lock immediately; otherwise wait until the timer
    /// shows that many seconds (or fewer) remaining.
    /// </summary>
    private static bool ShouldLock(long totalTimeMs, long timeLeftMs, int delaySeconds)
    {
        if (delaySeconds <= 0)
            return true;

        long thresholdMs = delaySeconds * 1000L;
        if (timeLeftMs <= thresholdMs)
            return true;

        if (totalTimeMs <= 0)
            return false;

        long elapsedMs = totalTimeMs - timeLeftMs;
        long startDelayMs = Math.Max(0, totalTimeMs - thresholdMs);
        return elapsedMs >= startDelayMs;
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
