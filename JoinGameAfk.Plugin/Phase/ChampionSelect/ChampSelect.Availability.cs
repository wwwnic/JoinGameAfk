using System.Net;
using System.Text.Json;
using JoinGameAfk.Model;
using LcuClient;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private static bool AreAllConfiguredOptionsUnavailable(
        JsonElement root,
        int localPlayerCellId,
        int actionId,
        IReadOnlyCollection<int> championIds,
        IReadOnlySet<int> excludedChampionIds,
        ChampionOwnershipSnapshot ownershipSnapshot,
        bool manualSelectionOverride,
        bool isPickAction)
    {
        return !manualSelectionOverride
            && championIds.Count > 0
            && !HasAvailableConfiguredChampion(root, localPlayerCellId, actionId, championIds, excludedChampionIds, ownershipSnapshot, isPickAction);
    }

    private static bool HasAvailableConfiguredChampion(
        JsonElement root,
        int localPlayerCellId,
        int actionId,
        IReadOnlyCollection<int> championIds,
        IReadOnlySet<int> excludedChampionIds,
        ChampionOwnershipSnapshot ownershipSnapshot,
        bool isPickAction)
    {
        foreach (int championId in championIds)
        {
            if (championId <= 0 || excludedChampionIds.Contains(championId))
                continue;

            string? unavailableStatus = isPickAction
                ? GetPickChampionUnavailableStatus(root, localPlayerCellId, actionId, championId, ownershipSnapshot)
                : GetChampionUnavailableStatus(root, localPlayerCellId, actionId, championId, includeLocalPlayerTeamSelection: true);
            if (unavailableStatus is null)
                return true;
        }

        return false;
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
}