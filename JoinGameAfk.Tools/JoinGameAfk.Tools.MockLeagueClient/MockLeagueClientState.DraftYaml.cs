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
                QueueId = _queueMode == MockQueueMode.DraftPick ? _queueId : 400,
                QueueName = _queueMode == MockQueueMode.DraftPick ? _queueName : "Normal Draft",
                LocalSlot = _localPlayerCellId,
                LocalRole = _localPlayerAssignedPosition,
                RevealEnemyPickIntents = _revealEnemyPickIntents,
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
        _queueMode = MockQueueMode.DraftPick;
        SetQueueCore(configuration.QueueId, NormalizeText(configuration.QueueName, "Normal Draft"));
        _localPlayerCellId = NormalizeLocalPlayerCellId(GetYamlLocalSlot(configuration));
        _localPlayerAssignedPosition = MockLeagueClientRoles.NormalizeDisplayRole(GetYamlLocalRole(configuration));
        _revealEnemyPickIntents = configuration.RevealEnemyPickIntents;
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
               && ChampionCatalog.TryGetById(championId, out var champion)
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
            return champion.Id;
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
