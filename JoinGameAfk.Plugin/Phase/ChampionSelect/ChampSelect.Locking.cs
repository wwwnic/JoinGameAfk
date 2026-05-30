namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
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

    private void ScheduleLock(string sessionId, int actionId, int championId, int lockDelaySeconds, DateTime lockAtUtc, bool isPickAction, CancellationToken cancellationToken)
    {
        ScheduledLockState? currentSchedule = isPickAction ? _scheduledPickLock : _scheduledBanLock;
        if (currentSchedule is not null
            && string.Equals(currentSchedule.SessionId, sessionId, StringComparison.Ordinal)
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
        var scheduledLock = new ScheduledLockState(sessionId, actionId, championId, lockDelaySeconds, lockAtUtc, linkedCts);
        if (isPickAction)
            _scheduledPickLock = scheduledLock;
        else
            _scheduledBanLock = scheduledLock;

        ScheduleLockSoundAlerts(scheduledLock, isPickAction);
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

            cancellationToken.ThrowIfCancellationRequested();
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
        string actionLabel = GetActionLabel(isPickAction);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsScheduledLockCurrent(scheduledLock, isPickAction))
            return;

        StopLockSoundChannel(scheduledLock, isPickAction);
        Log($"{actionLabel} scheduled lock window reached. Locking {FormatChampion(scheduledLock.ChampionId)} on actionId={scheduledLock.ActionId}.");
        await _http.CompleteActionAsync(scheduledLock.ActionId, scheduledLock.ChampionId, cancellationToken);

        if (isPickAction)
            _hasPicked = true;
        else
            _hasBanned = true;

        TryPlayLockCompleteSoundAlert(scheduledLock.SessionId, scheduledLock.ActionId, scheduledLock.ChampionId, isPickAction);
        Log($"{actionLabel} locked successfully. Champion={FormatChampion(scheduledLock.ChampionId)}, actionId={scheduledLock.ActionId}.");
    }

    private void CancelScheduledPickLock()
    {
        if (_scheduledPickLock is not { } scheduledLock)
            return;

        StopLockSoundChannel(scheduledLock, isPickAction: true);
        scheduledLock.CancellationTokenSource.Cancel();
        _scheduledPickLock = null;
    }

    private void CancelScheduledBanLock()
    {
        if (_scheduledBanLock is not { } scheduledLock)
            return;

        StopLockSoundChannel(scheduledLock, isPickAction: false);
        scheduledLock.CancellationTokenSource.Cancel();
        _scheduledBanLock = null;
    }

    private void ClearScheduledLockIfCurrent(ScheduledLockState scheduledLock, bool isPickAction)
    {
        if (isPickAction)
        {
            if (ReferenceEquals(_scheduledPickLock, scheduledLock))
            {
                StopLockSoundChannel(scheduledLock, isPickAction);
                _scheduledPickLock = null;
            }

            return;
        }

        if (ReferenceEquals(_scheduledBanLock, scheduledLock))
        {
            StopLockSoundChannel(scheduledLock, isPickAction);
            _scheduledBanLock = null;
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
}