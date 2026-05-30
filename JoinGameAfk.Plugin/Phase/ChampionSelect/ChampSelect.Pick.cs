using System.Text.Json;
using LcuClient;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
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
                TryPlayManualSelectionOverrideSoundAlert(sessionId, actionId, currentChampionId, isPickAction: true);
                LogStatus(ref _lastPickStatusMessage, $"Pick selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredPick = true;
            _hoveredPickChampionId = currentChampionId;
        }

        TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedPickChampionIds, ownershipSnapshot, _manualPickSelectionOverride, isPickAction: true);

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
            if (ShouldAttemptHover(actionId, champSelectPhase, timeLeftMs, isPickAction: true, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastPickStatusMessage, $"{hoverActionLabel} delay satisfied. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                await TryHoverChampionAsync(root, localPlayerCellId, actionId, preferredChampionIds, _failedPickChampionIds, ownershipSnapshot, isPickAction: true, actionLabel: hoverActionLabel, cancellationToken);
                TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedPickChampionIds, ownershipSnapshot, _manualPickSelectionOverride, isPickAction: true);
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

            TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedPickChampionIds, ownershipSnapshot, _manualPickSelectionOverride, isPickAction: true);
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
            TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedPickChampionIds, ownershipSnapshot, _manualPickSelectionOverride, isPickAction: true);
        }
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
}