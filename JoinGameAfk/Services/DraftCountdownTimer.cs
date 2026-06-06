using System.Globalization;
using System.Windows.Threading;
using JoinGameAfk.Model;

namespace JoinGameAfk.Services
{
    public sealed record DraftCountdownTimerSnapshot(
        string PhaseTimeText,
        string LockTimeText,
        string ActiveLockActionType)
    {
        public bool HasActiveLockTimer => !string.IsNullOrWhiteSpace(ActiveLockActionType) && LockTimeText != "--";
    }

    public sealed class DraftCountdownTimer
    {
        private const double TimerRenderBoundaryPaddingMs = 10;
        private const double MinimumTimerRenderDelayMs = 25;
        private const double MaximumTimerRenderDelayMs = 1000;
        private const double TimerBaselineRefreshToleranceMs = 50;

        private readonly DispatcherTimer _renderTimer;
        private readonly Action<DraftCountdownTimerSnapshot> _renderSnapshot;
        private string _activeLockActionType = string.Empty;
        private CountdownBaseline _phaseTimer = CountdownBaseline.Empty;
        private CountdownBaseline _lockTimer = CountdownBaseline.Empty;

        public DraftCountdownTimer(Action<DraftCountdownTimerSnapshot> renderSnapshot)
        {
            _renderSnapshot = renderSnapshot;
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(MaximumTimerRenderDelayMs)
            };
            _renderTimer.Tick += (_, _) => RenderAndSchedule();
        }

        public void Update(DashboardStatus status)
        {
            _phaseTimer = CreatePhaseBaseline(status);

            if (status.ActiveLockTimeLeftMilliseconds < 0 || string.IsNullOrWhiteSpace(status.ActiveLockActionType))
            {
                _activeLockActionType = string.Empty;
                _lockTimer = CountdownBaseline.Empty;
            }
            else
            {
                string activeLockActionType = status.ActiveLockActionType;
                var lockTimer = new CountdownBaseline(
                    Math.Max(0, status.ActiveLockTimeLeftMilliseconds),
                    NormalizeObservedAtUtc(status.ActiveLockTimeLeftObservedAtUtc));

                if (!ShouldKeepExistingLockBaseline(activeLockActionType, lockTimer))
                    _lockTimer = lockTimer;

                _activeLockActionType = activeLockActionType;
            }

            RenderAndSchedule();
        }

        public void Stop()
        {
            _renderTimer.Stop();
        }

        private void RenderAndSchedule()
        {
            var phaseRender = RenderPhaseTimer();
            var lockRender = RenderLockTimer();

            _renderSnapshot(new DraftCountdownTimerSnapshot(
                phaseRender.Text,
                lockRender.Text,
                lockRender.HasValue ? _activeLockActionType : string.Empty));

            ScheduleNextRender(phaseRender.RemainingMilliseconds, lockRender.RemainingMilliseconds);
        }

        private CountdownRender RenderPhaseTimer()
        {
            if (!_phaseTimer.HasValue)
                return CountdownRender.Empty;

            double remainingMs = _phaseTimer.GetRemainingMilliseconds();
            return new CountdownRender(
                GetDisplayTimeLeftSeconds(remainingMs).ToString(CultureInfo.InvariantCulture),
                remainingMs);
        }

        private CountdownRender RenderLockTimer()
        {
            if (!_lockTimer.HasValue)
                return CountdownRender.Empty;

            double remainingMs = _lockTimer.GetRemainingMilliseconds();
            return new CountdownRender(FormatPreciseTimeLeft(remainingMs), remainingMs);
        }

        private void ScheduleNextRender(double phaseRemainingMs, double lockRemainingMs)
        {
            _renderTimer.Stop();
            double delayMs = double.PositiveInfinity;

            if (phaseRemainingMs > 0)
                delayMs = Math.Min(delayMs, GetDelayToNextWholeSecond(phaseRemainingMs));

            if (lockRemainingMs > 0)
                delayMs = Math.Min(delayMs, GetDelayToNextTenthSecond(lockRemainingMs));

            if (double.IsPositiveInfinity(delayMs))
                return;

            delayMs = Math.Clamp(
                delayMs,
                MinimumTimerRenderDelayMs,
                MaximumTimerRenderDelayMs);

            _renderTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _renderTimer.Start();
        }

        private static CountdownBaseline CreatePhaseBaseline(DashboardStatus status)
        {
            long timeLeftMs = status.TimeLeftMilliseconds >= 0
                ? status.TimeLeftMilliseconds
                : status.TimeLeftSeconds >= 0
                    ? status.TimeLeftSeconds * 1000L
                    : -1;

            return timeLeftMs < 0
                ? CountdownBaseline.Empty
                : new CountdownBaseline(Math.Max(0, timeLeftMs), NormalizeObservedAtUtc(status.TimeLeftObservedAtUtc));
        }

        private bool ShouldKeepExistingLockBaseline(string activeLockActionType, CountdownBaseline lockTimer)
        {
            if (!_lockTimer.HasValue || !lockTimer.HasValue)
                return false;

            if (!string.Equals(_activeLockActionType, activeLockActionType, StringComparison.Ordinal))
                return false;

            double currentRemainingMs = _lockTimer.GetRemainingMilliseconds();
            if (currentRemainingMs <= 0)
                return false;

            double nextRemainingMs = lockTimer.GetRemainingMilliseconds();
            return nextRemainingMs > currentRemainingMs + TimerBaselineRefreshToleranceMs;
        }

        private static DateTime NormalizeObservedAtUtc(DateTime observedAtUtc)
        {
            return observedAtUtc == DateTime.MinValue
                ? DateTime.UtcNow
                : observedAtUtc;
        }

        private static double GetDelayToNextWholeSecond(double remainingMs)
        {
            double millisecondsUntilNextVisibleChange = remainingMs % 1000d;
            if (millisecondsUntilNextVisibleChange <= 0)
                millisecondsUntilNextVisibleChange = 1000d;

            return millisecondsUntilNextVisibleChange + TimerRenderBoundaryPaddingMs;
        }

        private static double GetDelayToNextTenthSecond(double remainingMs)
        {
            double millisecondsUntilNextVisibleChange = remainingMs % 100d;
            if (millisecondsUntilNextVisibleChange <= 0)
                millisecondsUntilNextVisibleChange = 100d;

            return millisecondsUntilNextVisibleChange + TimerRenderBoundaryPaddingMs;
        }

        private static int GetDisplayTimeLeftSeconds(double timeLeftMs)
        {
            if (timeLeftMs <= 0)
                return 0;

            return (int)(timeLeftMs / 1000d);
        }

        private static string FormatPreciseTimeLeft(double timeLeftMs)
        {
            if (timeLeftMs <= 0)
                return "0.0s";

            return (timeLeftMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        private readonly record struct CountdownBaseline(long TimeLeftMilliseconds, DateTime ObservedAtUtc)
        {
            public static CountdownBaseline Empty { get; } = new(-1, DateTime.MinValue);

            public bool HasValue => TimeLeftMilliseconds >= 0 && ObservedAtUtc != DateTime.MinValue;

            public double GetRemainingMilliseconds()
            {
                if (!HasValue)
                    return -1;

                double elapsedMs = Math.Max(0, (DateTime.UtcNow - ObservedAtUtc).TotalMilliseconds);
                return Math.Max(0, TimeLeftMilliseconds - elapsedMs);
            }
        }

        private readonly record struct CountdownRender(string Text, double RemainingMilliseconds)
        {
            public static CountdownRender Empty { get; } = new("--", -1);

            public bool HasValue => RemainingMilliseconds >= 0;
        }
    }
}
