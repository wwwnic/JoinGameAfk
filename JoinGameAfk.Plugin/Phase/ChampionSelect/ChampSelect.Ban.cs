using System.Text.Json;
using LcuClient;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
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
                TryPlayManualSelectionOverrideSoundAlert(sessionId, actionId, currentChampionId, isPickAction: false);
                LogStatus(ref _lastBanStatusMessage, $"Ban selection changed manually to {FormatChampion(currentChampionId)}. Falling back to last-second auto-lock for your current selection.");
            }

            _hasHoveredBan = true;
            _hoveredBanChampionId = currentChampionId;
        }

        TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, _manualBanSelectionOverride, isPickAction: false);

        if (!_hasHoveredBan && !_manualBanSelectionOverride && !_settings.AutoHoverChampionEnabled && !IsPlanningPhase(champSelectPhase))
        {
            _pendingBanHoverActionId = 0;
            _pendingBanHoverPhase = string.Empty;
            _banHoverReadyAtUtc = DateTime.MinValue;
            LogStatus(ref _lastBanStatusMessage, $"Ban action detected. Auto-hover is disabled, so the app is waiting for your manual champion selection.");
        }
        else if (!_hasHoveredBan && !_manualBanSelectionOverride && !IsPlanningPhase(champSelectPhase))
        {
            if (ShouldAttemptHover(actionId, champSelectPhase, timeLeftMs, isPickAction: false, out int hoverDelaySeconds))
            {
                LogStatus(ref _lastBanStatusMessage, $"Ban hover delay satisfied. ActionId={actionId}, currentChampionId={currentChampionId}, inProgress={isInProgress}, phase={champSelectPhase}, timeLeft={FormatTimeLeft(timeLeftMs)}. Attempting hover.");
                await TryHoverChampionAsync(root, localPlayerCellId, actionId, preferredChampionIds, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, isPickAction: false, actionLabel: "Ban", cancellationToken);
                TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, _manualBanSelectionOverride, isPickAction: false);
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

            TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, _manualBanSelectionOverride, isPickAction: false);
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
            TryPlayAllOptionsUnavailableSoundAlert(sessionId, actionId, root, localPlayerCellId, preferredChampionIds, _failedBanChampionIds, ChampionOwnershipSnapshot.Unknown, _manualBanSelectionOverride, isPickAction: false);
        }
    }

    private static bool IsManualSelectionOverride(int currentChampionId, bool hasAppHoveredChampion, int appHoveredChampionId, bool manualSelectionOverride)
    {
        return currentChampionId != 0
            && !manualSelectionOverride
            && (!hasAppHoveredChampion || currentChampionId != appHoveredChampionId);
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
}