using System.Text.Json;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private static TimerSnapshot GetTimerSnapshot(JsonElement sessionRoot, DateTime sessionObservedAtUtc)
    {
        return TryCreateTimerSnapshot(sessionRoot, sessionObservedAtUtc, out var timerSnapshot)
            ? timerSnapshot
            : new TimerSnapshot(string.Empty, 0, 0, null, IsInfinite: false, DateTime.UtcNow);
    }

    private static bool TryCreateTimerSnapshot(JsonElement source, DateTime fallbackObservedAtUtc, out TimerSnapshot timerSnapshot)
    {
        timerSnapshot = new TimerSnapshot(string.Empty, 0, 0, null, IsInfinite: false, fallbackObservedAtUtc);

        if (!TryGetTimerElement(source, out var timer))
            return false;

        string phase = timer.TryGetProperty("phase", out var phaseProperty)
            ? phaseProperty.GetString() ?? string.Empty
            : string.Empty;

        long timeLeftMs = GetTimerTimeLeftMs(timer);
        long totalTimeInPhaseMs = TryGetNumberAsInt64(timer, "totalTimeInPhase", out long totalTime)
            ? totalTime
            : 0;
        long? internalNowInEpochMs = TryGetNumberAsInt64(timer, "internalNowInEpochMs", out long internalNow)
            ? internalNow
            : null;
        bool isInfinite = TryGetBool(timer, "isInfinite", out bool infinite) && infinite;
        DateTime observedAtUtc = GetTimerObservedAtUtc(internalNowInEpochMs, totalTimeInPhaseMs, fallbackObservedAtUtc);

        timerSnapshot = new TimerSnapshot(
            phase,
            Math.Max(0, totalTimeInPhaseMs),
            Math.Max(0, timeLeftMs),
            internalNowInEpochMs,
            isInfinite,
            observedAtUtc);
        return true;
    }

    private static bool TryGetTimerElement(JsonElement source, out JsonElement timer)
    {
        timer = default;
        if (source.ValueKind != JsonValueKind.Object)
            return false;

        if (source.TryGetProperty("timer", out var nestedTimer))
        {
            timer = nestedTimer;
            return timer.ValueKind == JsonValueKind.Object;
        }

        timer = source;
        return true;
    }

    private static long GetTimerTimeLeftMs(JsonElement timer)
    {
        if (TryGetNumberAsInt64(timer, "adjustedTimeLeftInPhase", out long adjustedTimeLeftMs))
            return adjustedTimeLeftMs;

        if (TryGetNumberAsInt64(timer, "timeLeftInPhase", out long timeLeftMs))
            return timeLeftMs;

        if (TryGetNumberAsInt64(timer, "adjustedTimeLeftInPhaseInSec", out long adjustedTimeLeftSeconds))
            return adjustedTimeLeftSeconds * 1000L;

        if (TryGetNumberAsInt64(timer, "timeLeftInPhaseInSec", out long timeLeftSeconds))
            return timeLeftSeconds * 1000L;

        return 0;
    }

    private long GetEffectiveTimeLeftMs(string? sessionId, TimerSnapshot timerSnapshot, out DateTime observedAtUtc)
    {
        DateTime now = DateTime.UtcNow;
        observedAtUtc = now;
        long timeLeftMs = GetPayloadAgeAdjustedTimeLeftMs(timerSnapshot);

        bool shouldResetBaseline = !string.Equals(_lastTimerSessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(_lastTimerPhase, timerSnapshot.Phase, StringComparison.Ordinal)
            || _lastTimerObservedAtUtc == DateTime.MinValue
            || timeLeftMs != _lastReportedTimeLeftMs;

        if (shouldResetBaseline)
        {
            _lastTimerSessionId = sessionId;
            _lastTimerPhase = timerSnapshot.Phase;
            _lastReportedTimeLeftMs = timeLeftMs;
            _lastEffectiveTimeLeftMs = timeLeftMs;
            _lastTimerObservedAtUtc = now;
            return timeLeftMs;
        }

        if (timerSnapshot.IsInfinite)
            return _lastEffectiveTimeLeftMs;

        long elapsedMs = (long)(now - _lastTimerObservedAtUtc).TotalMilliseconds;
        if (elapsedMs <= 0)
            return _lastEffectiveTimeLeftMs;

        _lastEffectiveTimeLeftMs = Math.Max(0, _lastEffectiveTimeLeftMs - elapsedMs);
        _lastTimerObservedAtUtc = now;
        return _lastEffectiveTimeLeftMs;
    }

    private static long GetPayloadAgeAdjustedTimeLeftMs(TimerSnapshot timerSnapshot)
    {
        long timeLeftMs = Math.Max(0, timerSnapshot.TimeLeftMs);
        if (timerSnapshot.IsInfinite)
            return timeLeftMs;

        long payloadAgeMs = (long)(DateTime.UtcNow - timerSnapshot.ObservedAtUtc).TotalMilliseconds;
        long maxExpectedPayloadAgeMs = Math.Max(timerSnapshot.TotalTimeInPhaseMs, 0)
            + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
        if (payloadAgeMs <= 0 || payloadAgeMs > maxExpectedPayloadAgeMs)
            return timeLeftMs;

        return Math.Max(0, timeLeftMs - payloadAgeMs);
    }

    private static DateTime GetTimerObservedAtUtc(long? internalNowInEpochMs, long totalTimeInPhaseMs, DateTime fallbackObservedAtUtc)
    {
        if (internalNowInEpochMs is not long epochMs)
            return fallbackObservedAtUtc;

        try
        {
            DateTime internalObservedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
            long payloadAgeMs = (long)(fallbackObservedAtUtc - internalObservedAtUtc).TotalMilliseconds;
            long maxExpectedPayloadAgeMs = Math.Max(totalTimeInPhaseMs, 0)
                + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;

            return payloadAgeMs >= 0 && payloadAgeMs <= maxExpectedPayloadAgeMs
                ? internalObservedAtUtc
                : fallbackObservedAtUtc;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallbackObservedAtUtc;
        }
    }
}