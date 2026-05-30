using System.Text.Json;
using JoinGameAfk.Enums;
using LcuClient;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
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
        bool localPlayerPickCompleted = false;
        bool localPlayerBanCompleted = false;

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

                    string type = action.TryGetProperty("type", out var typeProperty)
                        ? typeProperty.GetString() ?? string.Empty
                        : string.Empty;

                    if (!TryGetBool(action, "completed", out bool completed))
                        continue;

                    if (completed)
                    {
                        int? completedActionId = TryGetInt32(action, "id", out int completedActionIdValue)
                            ? completedActionIdValue
                            : null;
                        int completedChampionId = TryGetInt32(action, "championId", out int completedChampionIdValue)
                            ? completedChampionIdValue
                            : 0;

                        if (type == "pick")
                        {
                            localPlayerPickCompleted = true;
                            MarkLocalActionCompleted(completedActionId, completedChampionId, isPickAction: true);
                        }

                        if (type == "ban")
                        {
                            localPlayerBanCompleted = true;
                            MarkLocalActionCompleted(completedActionId, completedChampionId, isPickAction: false);
                        }

                        continue;
                    }

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

        LastDashboardStatus = BuildDashboardStatus(
            root,
            localPlayerCellId,
            localPickActionId,
            localBanActionId,
            pickChoices,
            banChoices,
            mergedPickIds,
            mergedBanIds,
            ownershipSnapshot,
            assignedPosition,
            champSelectPhase,
            timeLeftMs,
            timeLeftObservedAtUtc,
            localPlayerActiveActionType,
            localPlayerPickCompleted || _hasPicked,
            localPlayerBanCompleted || _hasBanned,
            _hasHoveredPick);
    }

    private void MarkLocalActionCompleted(int? actionId, int championId, bool isPickAction)
    {
        if (isPickAction)
        {
            bool hadScheduledLock = _scheduledPickLock is not null;
            CancelScheduledPickLock();
            ClearPendingPickAutomationAfterCompletion();
            _hasPicked = true;
            if (championId != 0)
                _hoveredPickChampionId = championId;

            if (hadScheduledLock)
                Log($"Pick already completed in League Client. Canceled scheduled auto-lock{FormatOptionalActionId(actionId)}.");

            return;
        }

        bool hadScheduledBanLock = _scheduledBanLock is not null;
        CancelScheduledBanLock();
        ClearPendingBanAutomationAfterCompletion();
        _hasBanned = true;
        if (championId != 0)
            _hoveredBanChampionId = championId;

        if (hadScheduledBanLock)
            Log($"Ban already completed in League Client. Canceled scheduled auto-lock{FormatOptionalActionId(actionId)}.");
    }

    private void ClearPendingPickAutomationAfterCompletion()
    {
        CancelScheduledHoverWake();
        _manualPickSelectionOverride = false;
        _pendingPickHoverActionId = 0;
        _pendingPickHoverPhase = string.Empty;
        _pickHoverReadyAtUtc = DateTime.MinValue;
    }

    private void ClearPendingBanAutomationAfterCompletion()
    {
        CancelScheduledHoverWake();
        _manualBanSelectionOverride = false;
        _pendingBanHoverActionId = 0;
        _pendingBanHoverPhase = string.Empty;
        _banHoverReadyAtUtc = DateTime.MinValue;
    }

    private static string FormatOptionalActionId(int? actionId)
    {
        return actionId is int value
            ? $" for actionId={value}"
            : string.Empty;
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
}