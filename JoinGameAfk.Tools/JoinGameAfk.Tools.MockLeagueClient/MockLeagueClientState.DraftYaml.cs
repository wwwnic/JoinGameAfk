using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.Tools.MockLeagueClient;

internal sealed partial class MockLeagueClientState
{
    public DraftYamlConfiguration ExportDraftYamlConfiguration()
    {
        lock (_lock)
        {
            if (_queueMode == MockQueueMode.DraftPick)
                SaveCurrentDraftStepStateCore();

            return new DraftYamlConfiguration
            {
                Version = 1,
                QueueId = _queueMode == MockQueueMode.DraftPick ? _queueId : (int)LeagueQueueId.NormalDraft,
                QueueName = _queueMode == MockQueueMode.DraftPick ? _queueName : "Normal Draft",
                LocalSlot = _localPlayerCellId,
                LocalRole = _localPlayerAssignedPosition,
                RevealEnemyPickIntents = _revealEnemyPickIntents,
                ChampionOwnership = CreateYamlChampionOwnership(),
                ChampionGrid = CreateYamlChampionGrid(),
                ActivePhase = _draftStep.ToString(),
                BlueTeam = CreateYamlTeamSlots(_myTeam),
                RedTeam = CreateYamlTeamSlots(_theirTeam),
                Phases = CreateYamlPhases()
            };
        }
    }

    public void ImportDraftYamlConfiguration(DraftYamlConfiguration configuration)
    {
        lock (_lock)
        {
            ApplyDraftYamlConfigurationCore(configuration);
        }

        OnChanged();
    }

    private void ApplyDraftYamlConfigurationCore(DraftYamlConfiguration configuration)
    {
        NormalizeYamlConfiguration(configuration);
        ParsedYamlChampionOwnership? championOwnership = configuration.ChampionOwnership is null
            ? null
            : ParseYamlChampionOwnership(configuration.ChampionOwnership);
        ParsedYamlChampionGrid? championGrid = configuration.ChampionGrid is null
            ? null
            : ParseYamlChampionGrid(configuration.ChampionGrid);
        _queueMode = MockQueueMode.DraftPick;
        SetQueueCore(configuration.QueueId, NormalizeText(configuration.QueueName, "Normal Draft"));
        _localPlayerCellId = NormalizeLocalPlayerCellId(GetYamlLocalSlot(configuration));
        _localPlayerAssignedPosition = MockLeagueClientRoles.NormalizeDisplayRole(GetYamlLocalRole(configuration));
        _revealEnemyPickIntents = configuration.RevealEnemyPickIntents;
        ApplyYamlChampionOwnershipCore(championOwnership);
        ApplyYamlChampionGridCore(championGrid);
        _sharedDraftPickHoverChampionIds.Clear();
        _sharedDraftBanHoverChampionIds.Clear();

        var rolesByCellId = CreateYamlRoleMap(configuration);
        _draftStepStates.Clear();
        foreach (var step in DraftPickSteps.All.Select(option => option.Step))
        {
            var state = CreateDefaultDraftStepState(step);
            ApplyYamlRolesToSlots(state.MyTeam, rolesByCellId);
            ApplyYamlRolesToSlots(state.TheirTeam, rolesByCellId);

            if (TryGetYamlPhase(configuration, step, out var phase))
            {
                ApplyYamlTimedActions(state.Actions, phase.TimedActions);
                state = new DraftStepState(
                    state.TimerPhase,
                    NormalizeYamlTimeLeftSeconds(phase.TimeLeftSeconds, state.TimeLeftSeconds),
                    state.MyTeam,
                    state.TheirTeam,
                    state.MyTeamBans,
                    state.TheirTeamBans,
                    state.Actions,
                    CreateYamlOptionalTimedActions(phase.OptionalTimedActions));
            }

            _draftStepStates[step] = state;
        }

        var activeStep = TryParseDraftStep(configuration.ActivePhase, out var parsedStep)
            ? parsedStep
            : DraftPickStep.Planning;

        LoadDraftStepStateCore(activeStep);
    }

    private static void NormalizeYamlConfiguration(DraftYamlConfiguration configuration)
    {
        configuration.BlueTeam ??= [];
        configuration.RedTeam ??= [];
        configuration.Phases ??= [];
        foreach (var phase in configuration.Phases.Values.Where(phase => phase is not null))
        {
            phase.TimedActions ??= [];
            phase.OptionalTimedActions ??= [];
        }
    }

    private DraftYamlChampionOwnershipConfiguration CreateYamlChampionOwnership()
    {
        return new DraftYamlChampionOwnershipConfiguration
        {
            Default = _championOwnershipMode == MockChampionOwnershipMode.AllChampions ? "all" : "none",
            Owned = _championOwnershipMode == MockChampionOwnershipMode.ConfiguredInventory
                ? _ownedChampionIds
                    .Except(_notOwnedChampionIds)
                    .Order()
                    .Select(championId => GetChampionName(championId) ?? championId.ToString())
                    .ToList()
                : null,
            NotOwned = _notOwnedChampionIds.Count > 0
                ? _notOwnedChampionIds
                    .Order()
                    .Select(championId => GetChampionName(championId) ?? championId.ToString())
                    .ToList()
                : null
        };
    }

    private DraftYamlChampionGridConfiguration? CreateYamlChampionGrid()
    {
        var overrides = _champSelectGridChampionOverrides.Values.ToList();
        var championGrid = new DraftYamlChampionGridConfiguration
        {
            FreeToPlay = CreateYamlChampionGridList(overrides, champion => champion.FreeToPlay),
            FreeToPlayForQueue = CreateYamlChampionGridList(overrides, champion => champion.FreeToPlayForQueue),
            LoyaltyReward = CreateYamlChampionGridList(overrides, champion => champion.LoyaltyReward),
            XboxGPReward = CreateYamlChampionGridList(overrides, champion => champion.XboxGPReward),
            Rented = CreateYamlChampionGridList(overrides, champion => champion.Rented),
            Disabled = CreateYamlChampionGridList(overrides, champion => champion.Disabled)
        };

        return championGrid.FreeToPlay is null
            && championGrid.FreeToPlayForQueue is null
            && championGrid.LoyaltyReward is null
            && championGrid.XboxGPReward is null
            && championGrid.Rented is null
            && championGrid.Disabled is null
            ? null
            : championGrid;
    }

    private static List<string>? CreateYamlChampionGridList(
        IEnumerable<MockChampSelectGridChampion> champions,
        Func<MockChampSelectGridChampion, bool> selector)
    {
        var championNames = champions
            .Where(selector)
            .Select(champion => champion.Id)
            .Order()
            .Select(championId => GetChampionName(championId) ?? championId.ToString())
            .ToList();

        return championNames.Count == 0 ? null : championNames;
    }

    private void ApplyYamlChampionOwnershipCore(ParsedYamlChampionOwnership? championOwnership)
    {
        if (championOwnership is null)
            return;

        _championOwnershipMode = championOwnership.DefaultMode;
        _ownedChampionIds.Clear();
        _ownedChampionIds.UnionWith(championOwnership.OwnedChampionIds);
        _notOwnedChampionIds.Clear();
        _notOwnedChampionIds.UnionWith(championOwnership.NotOwnedChampionIds);
        _champSelectGridChampionOverrides.Clear();
    }

    private void ApplyYamlChampionGridCore(ParsedYamlChampionGrid? championGrid)
    {
        if (championGrid is null)
            return;

        _champSelectGridChampionOverrides.Clear();
        var championIds = new HashSet<int>();
        championIds.UnionWith(championGrid.FreeToPlayChampionIds);
        championIds.UnionWith(championGrid.FreeToPlayForQueueChampionIds);
        championIds.UnionWith(championGrid.LoyaltyRewardChampionIds);
        championIds.UnionWith(championGrid.XboxGPRewardChampionIds);
        championIds.UnionWith(championGrid.RentedChampionIds);
        championIds.UnionWith(championGrid.DisabledChampionIds);

        foreach (int championId in championIds)
        {
            _champSelectGridChampionOverrides[championId] = new MockChampSelectGridChampion(
                championId,
                IsYamlChampionOwned(championId),
                championGrid.FreeToPlayChampionIds.Contains(championId),
                championGrid.FreeToPlayForQueueChampionIds.Contains(championId),
                championGrid.LoyaltyRewardChampionIds.Contains(championId),
                championGrid.XboxGPRewardChampionIds.Contains(championId),
                championGrid.RentedChampionIds.Contains(championId),
                championGrid.DisabledChampionIds.Contains(championId));
        }
    }

    private bool IsYamlChampionOwned(int championId)
    {
        if (_notOwnedChampionIds.Contains(championId))
            return false;

        return _championOwnershipMode == MockChampionOwnershipMode.AllChampions
            || _ownedChampionIds.Contains(championId);
    }

    private static ParsedYamlChampionOwnership ParseYamlChampionOwnership(
        DraftYamlChampionOwnershipConfiguration configuration)
    {
        string defaultOwnership = NormalizeYamlToken(
            string.IsNullOrWhiteSpace(configuration.Default)
                ? configuration.Mode ?? "none"
                : configuration.Default);
        MockChampionOwnershipMode defaultMode = defaultOwnership switch
        {
            "ALL" or "ALLCHAMPIONS" => MockChampionOwnershipMode.AllChampions,
            "NONE" or "LIST" or "CONFIGURED" or "CONFIGUREDINVENTORY" => MockChampionOwnershipMode.ConfiguredInventory,
            _ => throw new InvalidOperationException(
                "ChampionOwnership.Default must be either 'all' or 'none'.")
        };

        HashSet<int> ownedChampionIds = ParseYamlChampionIds(
            configuration.Owned ?? configuration.Champions,
            configuration.Owned is null && configuration.Champions is not null ? "Champions" : "Owned");
        HashSet<int> notOwnedChampionIds = ParseYamlChampionIds(configuration.NotOwned, "NotOwned");
        ownedChampionIds.ExceptWith(notOwnedChampionIds);

        return new ParsedYamlChampionOwnership(defaultMode, ownedChampionIds, notOwnedChampionIds);
    }

    private static ParsedYamlChampionGrid ParseYamlChampionGrid(
        DraftYamlChampionGridConfiguration configuration)
    {
        return new ParsedYamlChampionGrid(
            ParseYamlChampionIds(configuration.FreeToPlay, "FreeToPlay", "ChampionGrid"),
            ParseYamlChampionIds(configuration.FreeToPlayForQueue, "FreeToPlayForQueue", "ChampionGrid"),
            ParseYamlChampionIds(configuration.LoyaltyReward, "LoyaltyReward", "ChampionGrid"),
            ParseYamlChampionIds(configuration.XboxGPReward, "XboxGPReward", "ChampionGrid"),
            ParseYamlChampionIds(configuration.Rented, "Rented", "ChampionGrid"),
            ParseYamlChampionIds(configuration.Disabled, "Disabled", "ChampionGrid"));
    }

    private static HashSet<int> ParseYamlChampionIds(
        IEnumerable<string>? championReferences,
        string listName,
        string sectionName = "ChampionOwnership")
    {
        var championIds = new HashSet<int>();
        foreach (string? championReference in championReferences ?? [])
        {
            if (string.IsNullOrWhiteSpace(championReference))
                continue;

            if (TryResolveYamlChampionId(championReference, out int championId))
            {
                championIds.Add(championId);
                continue;
            }

            throw new InvalidOperationException(
                $"{sectionName}.{listName} contains an unknown champion: '{championReference}'.");
        }

        return championIds;
    }

    private static bool TryResolveYamlChampionId(string championReference, out int championId)
    {
        if (int.TryParse(championReference, out int numericChampionId)
            && ChampionCatalog.TryGetByKey(numericChampionId, out var championById)
            && championById is not null)
        {
            championId = numericChampionId;
            return true;
        }

        if (ChampionCatalog.TryGetByName(championReference, out var championByName)
            && championByName is not null)
        {
            championId = championByName.Key;
            return true;
        }

        championId = 0;
        return false;
    }

    private static int GetYamlLocalSlot(DraftYamlConfiguration configuration)
    {
        return configuration.LocalSlot
               ?? configuration.LocalPlayerCellId
               ?? 1;
    }

    private static string GetYamlLocalRole(DraftYamlConfiguration configuration)
    {
        return configuration.LocalRole
               ?? configuration.LocalPlayerRole
               ?? MockLeagueClientRoles.DefaultRole;
    }

    private Dictionary<string, DraftYamlPhaseConfiguration> CreateYamlPhases()
    {
        var phases = new Dictionary<string, DraftYamlPhaseConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in DraftPickSteps.All.Select(option => option.Step))
        {
            var state = _draftStepStates.TryGetValue(step, out var existingState)
                ? existingState
                : CreateDefaultDraftStepState(step);

            phases[step.ToString()] = new DraftYamlPhaseConfiguration
            {
                TimeLeftSeconds = state.TimeLeftSeconds,
                TimedActions = CreateYamlTimedActions(state.Actions),
                OptionalTimedActions = CreateYamlOptionalTimedActions(state.CustomTimedActions)
            };
        }

        return phases;
    }

    private static List<DraftYamlTeamSlot> CreateYamlTeamSlots(IEnumerable<TeamSlot> slots)
    {
        return slots
            .OrderBy(slot => slot.CellId)
            .Select(slot => new DraftYamlTeamSlot
            {
                Cell = slot.CellId,
                Role = MockLeagueClientRoles.NormalizeDisplayRole(slot.AssignedPosition)
            })
            .ToList();
    }

    private static List<DraftYamlTimedAction> CreateYamlTimedActions(IEnumerable<ChampSelectAction> actions)
    {
        return actions
            .Where(HasYamlTimedActionConfiguration)
            .OrderBy(action => action.ActorCellId)
            .ThenBy(action => action.Type, StringComparer.OrdinalIgnoreCase)
            .Select(action =>
            {
                int championId = action.TargetChampionId;
                return new DraftYamlTimedAction
                {
                    Cell = action.ActorCellId,
                    Type = NormalizeYamlActionType(action.Type),
                    Champion = GetChampionName(championId),
                    HoverAtSeconds = action.HoverAtSeconds,
                    LockAtSeconds = action.LockAtSeconds
                };
            })
            .ToList();
    }

    private static int NormalizeYamlTimeLeftSeconds(int? timeLeftSeconds, int fallbackSeconds)
    {
        return timeLeftSeconds is int value
            ? Math.Max(0, value)
            : fallbackSeconds;
    }

    private static bool HasYamlTimedActionConfiguration(ChampSelectAction action)
    {
        return action.TargetChampionId > 0
               || action.HoverAtSeconds.HasValue
               || action.LockAtSeconds.HasValue;
    }

    private static List<DraftYamlOptionalTimedAction> CreateYamlOptionalTimedActions(IEnumerable<TimedCustomAction> actions)
    {
        return actions
            .OrderBy(action => action.TriggerAtSeconds)
            .ThenBy(action => action.Id)
            .Select(action => new DraftYamlOptionalTimedAction
            {
                Id = action.Id > 0 ? action.Id : null,
                Type = action.Type.ToString(),
                SourceCell = action.SourceCellId,
                TargetCell = action.TargetCellId,
                TriggerAtSeconds = action.TriggerAtSeconds
            })
            .ToList();
    }

    private static string? GetChampionName(int championId)
    {
        return championId > 0
               && ChampionCatalog.TryGetByKey(championId, out var champion)
               && champion is not null
            ? champion.Name
            : null;
    }

    private static Dictionary<int, string> CreateYamlRoleMap(DraftYamlConfiguration configuration)
    {
        return configuration.BlueTeam
            .Concat(configuration.RedTeam)
            .Where(slot => IsValidCellId(slot.Cell))
            .GroupBy(slot => slot.Cell)
            .ToDictionary(
                group => group.Key,
                group => MockLeagueClientRoles.NormalizeDisplayRole(group.Last().Role));
    }

    private static void ApplyYamlRolesToSlots(IReadOnlyList<TeamSlot> slots, IReadOnlyDictionary<int, string> rolesByCellId)
    {
        foreach (var slot in slots)
        {
            if (rolesByCellId.TryGetValue(slot.CellId, out string? role))
                slot.AssignedPosition = role;
        }
    }

    private static bool TryGetYamlPhase(
        DraftYamlConfiguration configuration,
        DraftPickStep step,
        out DraftYamlPhaseConfiguration phase)
    {
        phase = new DraftYamlPhaseConfiguration();

        if (configuration.Phases.TryGetValue(step.ToString(), out var exactMatch) && exactMatch is not null)
        {
            phase = exactMatch;
            return true;
        }

        foreach (var candidate in configuration.Phases)
        {
            if (TryParseDraftStep(candidate.Key, out var candidateStep) && candidateStep == step && candidate.Value is not null)
            {
                phase = candidate.Value;
                return true;
            }
        }

        return false;
    }

    private static void ApplyYamlTimedActions(
        IReadOnlyList<ChampSelectAction> actions,
        IEnumerable<DraftYamlTimedAction> yamlActions)
    {
        foreach (var yamlAction in yamlActions)
        {
            if (!IsValidCellId(yamlAction.Cell))
                continue;

            string actionType = NormalizeYamlActionType(yamlAction.Type);
            var action = actions.FirstOrDefault(candidate =>
                candidate.ActorCellId == yamlAction.Cell
                && string.Equals(candidate.Type, actionType, StringComparison.OrdinalIgnoreCase));

            if (action is null)
                continue;

            action.TargetChampionId = ResolveYamlChampionId(yamlAction);
            action.HoverAtSeconds = NormalizeTriggerSeconds(yamlAction.HoverAtSeconds);
            action.LockAtSeconds = NormalizeTriggerSeconds(yamlAction.LockAtSeconds);
        }
    }

    private static List<TimedCustomAction> CreateYamlOptionalTimedActions(IEnumerable<DraftYamlOptionalTimedAction> yamlActions)
    {
        int nextId = 1;
        var actions = new List<TimedCustomAction>();
        foreach (var yamlAction in yamlActions)
        {
            int id = yamlAction.Id is > 0 ? yamlAction.Id.Value : nextId;
            nextId = Math.Max(nextId, id + 1);

            actions.Add(CloneWithNormalizedCustomAction(new TimedCustomAction
            {
                Id = id,
                Type = ParseYamlCustomActionType(yamlAction.Type),
                SourceCellId = yamlAction.SourceCell,
                TargetCellId = yamlAction.TargetCell,
                TriggerAtSeconds = yamlAction.TriggerAtSeconds
            }));
        }

        return actions;
    }

    private static int ResolveYamlChampionId(DraftYamlTimedAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Champion)
            && ChampionCatalog.TryGetByName(action.Champion, out var champion)
            && champion is not null)
        {
            return champion.Key;
        }

        return 0;
    }

    private static string NormalizeYamlActionType(string? type)
    {
        return string.Equals(type, "ban", StringComparison.OrdinalIgnoreCase)
            ? "ban"
            : "pick";
    }

    private static TimedCustomActionType ParseYamlCustomActionType(string? value)
    {
        string normalizedValue = NormalizeYamlToken(value);
        foreach (TimedCustomActionType type in Enum.GetValues<TimedCustomActionType>())
        {
            if (NormalizeYamlToken(type.ToString()) == normalizedValue
                || NormalizeYamlToken(GetCustomTimedActionTypeDisplayName(type)) == normalizedValue)
            {
                return type;
            }
        }

        return TimedCustomActionType.RoleSwap;
    }

    private static bool TryParseDraftStep(string? value, out DraftPickStep step)
    {
        if (Enum.TryParse(value, ignoreCase: true, out step))
            return true;

        string normalizedValue = NormalizeYamlToken(value);
        foreach (var option in DraftPickSteps.All)
        {
            if (NormalizeYamlToken(option.DisplayName) == normalizedValue
                || NormalizeYamlToken(option.DetailText) == normalizedValue)
            {
                step = option.Step;
                return true;
            }
        }

        step = DraftPickStep.Planning;
        return false;
    }

    private static string NormalizeYamlToken(string? value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}
