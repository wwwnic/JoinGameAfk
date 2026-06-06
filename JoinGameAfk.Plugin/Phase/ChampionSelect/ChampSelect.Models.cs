using JoinGameAfk.Enums;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private sealed record ChampionPlanChoice(int ChampionId, Position SourcePosition);

    private sealed record TimerSnapshot(
        string Phase,
        long TotalTimeInPhaseMs,
        long TimeLeftMs,
        long? InternalNowInEpochMs,
        bool IsInfinite,
        DateTime ObservedAtUtc);

    private sealed record ScheduledLockState(
        string SessionId,
        int ActionId,
        int ChampionId,
        int LockDelaySeconds,
        DateTime LockAtUtc,
        CancellationTokenSource CancellationTokenSource);

    private sealed record LockSoundAlertSchedule(string AlertId, int ThresholdSeconds, int Urgency);

    private sealed record ActiveLockCountdownStatus(
        string ActionType,
        long TimeLeftMilliseconds,
        DateTime ObservedAtUtc);

    private sealed record DraftActionDisplayState(
        int ActorCellId,
        int ChampionId,
        string ActionType,
        string SelectionState,
        bool IsActionInProgress,
        bool IsCompleted);
}