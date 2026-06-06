using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using LcuClient;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private DashboardStatus BuildDashboardStatus(
        JsonElement root,
        int localPlayerCellId,
        int pickActionId,
        int banActionId,
        IReadOnlyList<ChampionPlanChoice> pickChoices,
        IReadOnlyList<ChampionPlanChoice> banChoices,
        IReadOnlyCollection<int> pickChampionIds,
        IReadOnlyCollection<int> banChampionIds,
        ChampionOwnershipSnapshot ownershipSnapshot,
        Position assignedPosition,
        string champSelectPhase,
        long timeLeftMs,
        DateTime timeLeftObservedAtUtc,
        string? localPlayerActiveActionType,
        bool localPlayerPickCompleted,
        bool localPlayerBanCompleted,
        bool localPlayerPlanningHoverCompleted)
    {
        bool isPlanningPhase = IsPlanningPhase(champSelectPhase);
        var activeLockCountdown = GetActiveLockCountdownStatus(champSelectPhase, localPlayerActiveActionType, timeLeftMs, timeLeftObservedAtUtc);
        var draftActionStates = BuildDraftActionDisplayStates(root, suppressInProgress: isPlanningPhase);

        return new DashboardStatus
        {
            CurrentPosition = assignedPosition,
            MyTeamSlots = BuildTeamSlotItems(root, "myTeam", localPlayerCellId, draftActionStates, suppressPickIntentActionState: isPlanningPhase),
            TheirTeamSlots = BuildTeamSlotItems(root, "theirTeam", localPlayerCellId, draftActionStates, suppressPickIntentActionState: isPlanningPhase),
            MyTeamBans = BuildTeamBanItems(root, "myTeamBans", "myTeam", draftActionStates),
            TheirTeamBans = BuildTeamBanItems(root, "theirTeamBans", "theirTeam", draftActionStates),
            PickChampionPriority = BuildChampionPlanItems(root, localPlayerCellId, pickActionId, pickChoices, _failedPickChampionIds, ownershipSnapshot, requiresOwnedChampion: true, availableStatusText: "Pick"),
            BanChampionPriority = BuildChampionPlanItems(root, localPlayerCellId, banActionId, banChoices, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, requiresOwnedChampion: false, availableStatusText: "Ban"),
            PickChampionText = "No picks configured",
            BanChampionText = "No bans configured",
            PickLockText = BuildLockText(_settings.PickLockDelaySeconds, _manualPickSelectionOverride),
            BanLockText = BuildLockText(_settings.BanLockDelaySeconds, _manualBanSelectionOverride),
            ChampSelectSubPhase = GetSubPhaseLabel(champSelectPhase, localPlayerActiveActionType, localPlayerPickCompleted, localPlayerBanCompleted, localPlayerPlanningHoverCompleted),
            AllConfiguredOptionsUnavailable = HasAllConfiguredOptionsUnavailableWarning(
                root,
                localPlayerCellId,
                pickActionId,
                banActionId,
                pickChampionIds,
                banChampionIds,
                ownershipSnapshot,
                champSelectPhase,
                localPlayerActiveActionType,
                localPlayerPickCompleted,
                localPlayerBanCompleted),
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

    private bool HasAllConfiguredOptionsUnavailableWarning(
        JsonElement root,
        int localPlayerCellId,
        int pickActionId,
        int banActionId,
        IReadOnlyCollection<int> pickChampionIds,
        IReadOnlyCollection<int> banChampionIds,
        ChampionOwnershipSnapshot ownershipSnapshot,
        string champSelectPhase,
        string? localPlayerActiveActionType,
        bool localPlayerPickCompleted,
        bool localPlayerBanCompleted)
    {
        if (!localPlayerPickCompleted
            && pickActionId != 0
            && (IsPlanningPhase(champSelectPhase) || string.Equals(localPlayerActiveActionType, "pick", StringComparison.Ordinal)))
        {
            return AreAllConfiguredOptionsUnavailable(
                root,
                localPlayerCellId,
                pickActionId,
                pickChampionIds,
                _failedPickChampionIds,
                ownershipSnapshot,
                _manualPickSelectionOverride,
                isPickAction: true);
        }

        if (!localPlayerBanCompleted
            && banActionId != 0
            && string.Equals(localPlayerActiveActionType, "ban", StringComparison.Ordinal))
        {
            return AreAllConfiguredOptionsUnavailable(
                root,
                localPlayerCellId,
                banActionId,
                banChampionIds,
                _failedBanChampionIds,
                ChampionOwnershipSnapshot.Unknown,
                _manualBanSelectionOverride,
                isPickAction: false);
        }

        return false;
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
            && _rolePlanSettings.Preferences.TryGetValue(position, out var rolePreference)
            ? selector(rolePreference)
            : [];

        var defaultIds = _rolePlanSettings.Preferences.TryGetValue(Position.Default, out var defaultPreference)
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

    private static IReadOnlyList<DashboardTeamSlotItem> BuildTeamSlotItems(
        JsonElement root,
        string teamPropertyName,
        int localPlayerCellId,
        IReadOnlyList<DraftActionDisplayState> draftActionStates,
        bool suppressPickIntentActionState)
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
            int lockedPickChampionId = GetLockedPickChampionId(member);
            int hoverPickChampionId = GetHoverPickChampionId(member);
            bool hasCellId = TryGetInt32(member, "cellId", out int cellId);
            bool isLocalPlayer = hasCellId && cellId == localPlayerCellId;
            var activeAction = hasCellId ? GetActiveActionForCell(draftActionStates, cellId) : null;
            int championId = GetTeamSlotChampionId(lockedPickChampionId, hoverPickChampionId, activeAction);
            string actionType = GetTeamSlotActionType(lockedPickChampionId, hoverPickChampionId, activeAction, suppressPickIntentActionState);
            string selectionState = GetTeamSlotSelectionState(lockedPickChampionId, hoverPickChampionId, activeAction, suppressPickIntentActionState);
            string championName = championId > 0 ? FormatChampion(championId) : "No champion";

            slots.Add(new DashboardTeamSlotItem
            {
                CellId = cellId,
                ChampionId = championId,
                ChampionName = championName,
                RoleName = GetPositionDisplayName(position),
                IsLocalPlayer = isLocalPlayer,
                ActionType = actionType,
                SelectionState = selectionState,
                IsActionInProgress = activeAction is not null
            });
        }

        return slots;
    }

    private static int GetLockedPickChampionId(JsonElement member)
    {
        if (TryGetInt32(member, "championId", out int championId) && championId > 0)
            return championId;

        return 0;
    }

    private static int GetHoverPickChampionId(JsonElement member)
    {
        return TryGetInt32(member, "championPickIntent", out int championPickIntent) && championPickIntent > 0
            ? championPickIntent
            : 0;
    }

    private static int GetTeamSlotChampionId(
        int lockedPickChampionId,
        int hoverPickChampionId,
        DraftActionDisplayState? activeAction)
    {
        if (lockedPickChampionId > 0)
            return lockedPickChampionId;

        if (hoverPickChampionId > 0)
            return hoverPickChampionId;

        return activeAction is not null
               && string.Equals(activeAction.ActionType, DashboardDraftActionType.Pick, StringComparison.Ordinal)
               && activeAction.ChampionId > 0
            ? activeAction.ChampionId
            : 0;
    }

    private static string GetTeamSlotActionType(
        int lockedPickChampionId,
        int hoverPickChampionId,
        DraftActionDisplayState? activeAction,
        bool suppressPickIntentActionState)
    {
        if (activeAction is not null)
            return activeAction.ActionType;

        if (lockedPickChampionId > 0)
            return DashboardDraftActionType.Pick;

        if (!suppressPickIntentActionState && hoverPickChampionId > 0)
            return DashboardDraftActionType.Pick;

        return DashboardDraftActionType.None;
    }

    private static string GetTeamSlotSelectionState(
        int lockedPickChampionId,
        int hoverPickChampionId,
        DraftActionDisplayState? activeAction,
        bool suppressPickIntentActionState)
    {
        if (activeAction is not null
            && string.Equals(activeAction.ActionType, DashboardDraftActionType.Ban, StringComparison.Ordinal))
        {
            return activeAction.SelectionState;
        }

        if (lockedPickChampionId > 0)
            return DashboardDraftSelectionState.Locked;

        if (!suppressPickIntentActionState && hoverPickChampionId > 0)
            return DashboardDraftSelectionState.Hover;

        if (activeAction is not null)
            return activeAction.SelectionState;

        return DashboardDraftSelectionState.None;
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

    private static IReadOnlyList<DashboardChampionPlanItem> BuildTeamBanItems(
        JsonElement root,
        string bansPropertyName,
        string teamPropertyName,
        IReadOnlyList<DraftActionDisplayState> draftActionStates)
    {
        var teamCellIds = GetTeamCellIds(root, teamPropertyName);
        var actionBanStates = draftActionStates
            .Where(action =>
                string.Equals(action.ActionType, DashboardDraftActionType.Ban, StringComparison.Ordinal)
                && teamCellIds.Contains(action.ActorCellId)
                && action.ChampionId > 0
                && (action.IsCompleted || action.IsActionInProgress))
            .ToList();

        var championIdsFromBans = GetBanChampionIdsFromBans(root, bansPropertyName);
        var championIdsFromActions = actionBanStates.Select(action => action.ChampionId).ToHashSet();
        var items = new List<DashboardChampionPlanItem>();
        var seenChampionIds = new HashSet<int>();

        foreach (int championId in championIdsFromBans)
        {
            if (!seenChampionIds.Add(championId)
                || !ShouldShowBanChampion(championId, championIdsFromActions))
            {
                continue;
            }

            items.Add(CreateTeamBanItem(
                championId,
                DashboardDraftSelectionState.Locked,
                isActionInProgress: false));
        }

        foreach (var action in actionBanStates)
        {
            if (!seenChampionIds.Add(action.ChampionId)
                || !ShouldShowBanChampion(action.ChampionId, championIdsFromActions))
            {
                continue;
            }

            items.Add(CreateTeamBanItem(
                action.ChampionId,
                action.SelectionState == DashboardDraftSelectionState.Locked
                    ? DashboardDraftSelectionState.Locked
                    : DashboardDraftSelectionState.Hover,
                action.IsActionInProgress));
        }

        return items;
    }

    private static bool ShouldShowBanChampion(int championId, IReadOnlySet<int> actionBanChampionIds)
    {
        return ChampionCatalog.TryGetById(championId, out _)
            || actionBanChampionIds.Contains(championId);
    }

    private static DashboardChampionPlanItem CreateTeamBanItem(
        int championId,
        string selectionState,
        bool isActionInProgress)
    {
        return new DashboardChampionPlanItem
        {
            ChampionId = championId,
            Name = FormatChampion(championId),
            StatusText = selectionState == DashboardDraftSelectionState.Hover ? "Hover ban" : "Banned",
            ActionType = DashboardDraftActionType.Ban,
            SelectionState = selectionState,
            IsActionInProgress = isActionInProgress
        };
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

    private static IReadOnlyList<DraftActionDisplayState> BuildDraftActionDisplayStates(JsonElement root, bool suppressInProgress)
    {
        if (!root.TryGetProperty("actions", out var actions)
            || actions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var actionStates = new List<DraftActionDisplayState>();

        foreach (var actionGroup in actions.EnumerateArray())
        {
            if (actionGroup.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var action in actionGroup.EnumerateArray())
            {
                if (!TryGetInt32(action, "actorCellId", out int actorCellId))
                    continue;

                string type = action.TryGetProperty("type", out var typeProperty)
                    ? typeProperty.GetString() ?? string.Empty
                    : string.Empty;
                string actionType = GetDashboardActionType(type);

                if (string.IsNullOrWhiteSpace(actionType))
                    continue;

                int championId = TryGetInt32(action, "championId", out int actionChampionId)
                    ? Math.Max(0, actionChampionId)
                    : 0;
                bool completed = TryGetBool(action, "completed", out bool completedValue) && completedValue;
                bool inProgress = TryGetBool(action, "isInProgress", out bool inProgressValue) && inProgressValue;

                actionStates.Add(new DraftActionDisplayState(
                    actorCellId,
                    championId,
                    actionType,
                    GetActionSelectionState(championId, completed),
                    !suppressInProgress && inProgress && !completed,
                    completed));
            }
        }

        return actionStates;
    }

    private static DraftActionDisplayState? GetActiveActionForCell(
        IEnumerable<DraftActionDisplayState> draftActionStates,
        int cellId)
    {
        return draftActionStates.FirstOrDefault(action => action.ActorCellId == cellId && action.IsActionInProgress);
    }

    private static string GetDashboardActionType(string type)
    {
        if (string.Equals(type, "pick", StringComparison.OrdinalIgnoreCase))
            return DashboardDraftActionType.Pick;

        if (string.Equals(type, "ban", StringComparison.OrdinalIgnoreCase))
            return DashboardDraftActionType.Ban;

        return DashboardDraftActionType.None;
    }

    private static string GetActionSelectionState(int championId, bool completed)
    {
        if (completed)
            return DashboardDraftSelectionState.Locked;

        return championId > 0
            ? DashboardDraftSelectionState.Hover
            : DashboardDraftSelectionState.None;
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

    private static string GetSubPhaseLabel(string champSelectPhase, string? localPlayerActiveActionType, bool localPlayerPickCompleted, bool localPlayerBanCompleted, bool localPlayerPlanningHoverCompleted)
    {
        if (localPlayerPickCompleted)
            return "Pick done";

        if (IsPlanningPhase(champSelectPhase))
            return localPlayerPlanningHoverCompleted
                ? "Planning done"
                : "Planning";

        if (localPlayerActiveActionType is not null)
        {
            return localPlayerActiveActionType switch
            {
                "ban" => "Ban",
                "pick" => "Pick",
                _ => "Waiting",
            };
        }

        if (localPlayerBanCompleted)
            return "Ban done";

        return champSelectPhase.ToUpperInvariant() switch
        {
            "BAN_PICK" => "Waiting",
            "FINALIZATION" => "Pick done",
            _ => "Waiting",
        };
    }
}
