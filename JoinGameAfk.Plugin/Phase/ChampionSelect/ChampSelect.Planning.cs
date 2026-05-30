namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private static bool CanHoverPickNow(string champSelectPhase, bool isPickInProgress)
    {
        return isPickInProgress || IsPlanningPhase(champSelectPhase);
    }

    private static bool IsPlanningPhase(string champSelectPhase)
    {
        return string.Equals(champSelectPhase, "PLANNING", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatChampSelectPhase(string champSelectPhase)
    {
        return string.IsNullOrWhiteSpace(champSelectPhase)
            ? "unknown"
            : champSelectPhase;
    }

    private void ScheduleHoverWake(int actionId, string champSelectPhase, DateTime wakeAtUtc, CancellationToken cancellationToken)
    {
        if (_requestRefresh is null || wakeAtUtc <= DateTime.UtcNow)
            return;

        string phaseKey = string.IsNullOrWhiteSpace(champSelectPhase)
            ? string.Empty
            : champSelectPhase;

        if (_scheduledHoverWake is not null
            && _scheduledHoverWakeActionId == actionId
            && string.Equals(_scheduledHoverWakePhase, phaseKey, StringComparison.OrdinalIgnoreCase)
            && Math.Abs((_scheduledHoverWakeAtUtc - wakeAtUtc).TotalMilliseconds) < 250
            && !_scheduledHoverWake.IsCancellationRequested)
        {
            return;
        }

        CancelScheduledHoverWake();

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scheduledHoverWake = linkedCts;
        _scheduledHoverWakeActionId = actionId;
        _scheduledHoverWakePhase = phaseKey;
        _scheduledHoverWakeAtUtc = wakeAtUtc;
        _ = RunScheduledHoverWakeAsync(wakeAtUtc, linkedCts);
    }

    private async Task RunScheduledHoverWakeAsync(DateTime wakeAtUtc, CancellationTokenSource wakeCancellationTokenSource)
    {
        try
        {
            TimeSpan delay = wakeAtUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, wakeCancellationTokenSource.Token);

            _requestRefresh?.Invoke();
        }
        catch (OperationCanceledException) when (wakeCancellationTokenSource.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_scheduledHoverWake, wakeCancellationTokenSource))
            {
                _scheduledHoverWake = null;
                _scheduledHoverWakeActionId = 0;
                _scheduledHoverWakePhase = string.Empty;
                _scheduledHoverWakeAtUtc = DateTime.MinValue;
            }

            wakeCancellationTokenSource.Dispose();
        }
    }

    private void CancelScheduledHoverWake()
    {
        _scheduledHoverWake?.Cancel();
        _scheduledHoverWake = null;
        _scheduledHoverWakeActionId = 0;
        _scheduledHoverWakePhase = string.Empty;
        _scheduledHoverWakeAtUtc = DateTime.MinValue;
    }

    private bool ShouldAttemptHover(int actionId, string champSelectPhase, long timeLeftMs, bool isPickAction, out int hoverDelaySeconds)
    {
        string phaseKey = string.IsNullOrWhiteSpace(champSelectPhase)
            ? string.Empty
            : champSelectPhase;
        hoverDelaySeconds = GetHoverDelaySeconds(phaseKey);
        DateTime now = DateTime.UtcNow;
        DateTime initialHoverReadyAtUtc = ShouldHoverImmediatelyForRemainingTime(timeLeftMs, hoverDelaySeconds)
            ? now
            : now.AddSeconds(hoverDelaySeconds);

        if (isPickAction)
        {
            if (_pendingPickHoverActionId != actionId || !string.Equals(_pendingPickHoverPhase, phaseKey, StringComparison.OrdinalIgnoreCase))
            {
                _pendingPickHoverActionId = actionId;
                _pendingPickHoverPhase = phaseKey;
                _pickHoverReadyAtUtc = initialHoverReadyAtUtc;
            }

            return now >= _pickHoverReadyAtUtc;
        }

        if (_pendingBanHoverActionId != actionId || !string.Equals(_pendingBanHoverPhase, phaseKey, StringComparison.OrdinalIgnoreCase))
        {
            _pendingBanHoverActionId = actionId;
            _pendingBanHoverPhase = phaseKey;
            _banHoverReadyAtUtc = initialHoverReadyAtUtc;
        }

        return now >= _banHoverReadyAtUtc;
    }

    private static bool ShouldHoverImmediatelyForRemainingTime(long timeLeftMs, int hoverDelaySeconds)
    {
        if (hoverDelaySeconds <= 0)
            return true;

        return timeLeftMs > 0 && timeLeftMs <= hoverDelaySeconds * 1000L;
    }

    private int GetHoverDelaySeconds(string champSelectPhase)
    {
        int configuredDelaySeconds = Math.Max(0, _settings.ChampionHoverDelaySeconds);

        return IsPlanningPhase(champSelectPhase)
            ? Math.Max(configuredDelaySeconds, Math.Max(0, _settings.PlanningHoverDelaySeconds))
            : configuredDelaySeconds;
    }
}