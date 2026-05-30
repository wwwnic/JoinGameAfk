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