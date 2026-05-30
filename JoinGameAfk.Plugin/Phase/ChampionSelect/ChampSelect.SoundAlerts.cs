using System.Text.Json;
using JoinGameAfk.Model;
using LcuClient;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private void TryPlayActionStartedSoundAlert(string sessionId, int actionId, string actionType)
    {
        string? alertId = actionType switch
        {
            "pick" => SoundAlertIds.PickActionStart,
            "ban" => SoundAlertIds.BanActionStart,
            _ => null
        };

        if (alertId is null)
            return;

        TryPlaySoundAlertOnce(alertId, $"{sessionId}:{alertId}:{actionId}", $"{FormatActionType(actionType)} action sound alert");
    }

    private void TryPlayManualSelectionOverrideSoundAlert(string sessionId, int actionId, int championId, bool isPickAction)
    {
        TryPlaySoundAlertOnce(
            SoundAlertIds.ManualSelectionOverride,
            $"{sessionId}:{SoundAlertIds.ManualSelectionOverride}:{actionId}:{championId}",
            $"{GetActionLabel(isPickAction)} manual selection override sound alert");
    }

    private void TryPlayAllOptionsUnavailableSoundAlert(
        string sessionId,
        int actionId,
        JsonElement root,
        int localPlayerCellId,
        IReadOnlyCollection<int> preferredChampionIds,
        IReadOnlySet<int> excludedChampionIds,
        ChampionOwnershipSnapshot ownershipSnapshot,
        bool manualSelectionOverride,
        bool isPickAction)
    {
        if (!AreAllConfiguredOptionsUnavailable(
            root,
            localPlayerCellId,
            actionId,
            preferredChampionIds,
            excludedChampionIds,
            ownershipSnapshot,
            manualSelectionOverride,
            isPickAction))
        {
            return;
        }

        TryPlaySoundAlertOnce(
            SoundAlertIds.AllOptionsUnavailable,
            $"{sessionId}:{SoundAlertIds.AllOptionsUnavailable}:{actionId}:{GetActionLabel(isPickAction)}",
            $"{GetActionLabel(isPickAction)} configured options unavailable sound alert");
    }

    private static string FormatActionType(string actionType)
    {
        return actionType switch
        {
            "pick" => "Pick",
            "ban" => "Ban",
            _ => "Champion select"
        };
    }

    private void ScheduleLockSoundAlerts(ScheduledLockState scheduledLock, bool isPickAction)
    {
        if (_playSoundAlert is null)
            return;

        var alertSchedules = GetActiveLockSoundAlertSchedules(isPickAction);
        if (alertSchedules.Count == 0)
            return;

        TryPreloadSoundAlert(
            isPickAction ? SoundAlertIds.PickLockComplete : SoundAlertIds.BanLockComplete,
            $"{GetActionLabel(isPickAction)} auto-lock complete sound preload");

        DateTime now = DateTime.UtcNow;
        for (int index = 0; index < alertSchedules.Count; index++)
        {
            var schedule = alertSchedules[index];
            TryPreloadSoundAlert(schedule.AlertId, $"{GetActionLabel(isPickAction)} auto-lock countdown sound preload");

            DateTime alertAtUtc = scheduledLock.LockAtUtc.AddSeconds(-schedule.ThresholdSeconds);
            DateTime alertUntilUtc = index + 1 < alertSchedules.Count
                ? scheduledLock.LockAtUtc.AddSeconds(-alertSchedules[index + 1].ThresholdSeconds)
                : scheduledLock.LockAtUtc;

            if (alertUntilUtc <= now)
                continue;

            int? playbackDurationSeconds = index + 1 < alertSchedules.Count
                ? null
                : GetLockSoundPlaybackDurationSeconds(alertAtUtc > now ? alertAtUtc : now, alertUntilUtc);
            if (playbackDurationSeconds is <= 0)
                continue;

            if (alertAtUtc <= now)
                TryPlayLockCountdownSoundAlert(scheduledLock, isPickAction, schedule.AlertId, playbackDurationSeconds);
            else
                _ = RunScheduledLockSoundAlertAsync(scheduledLock, isPickAction, schedule.AlertId, alertAtUtc, playbackDurationSeconds);
        }
    }

    private List<LockSoundAlertSchedule> GetActiveLockSoundAlertSchedules(bool isPickAction)
    {
        var countdownAlert = CreateLockSoundAlertSchedule(
            isPickAction ? SoundAlertIds.PickLockCountdown : SoundAlertIds.BanLockCountdown,
            SoundAlertDefaults.DefaultLockCountdownThresholdSeconds,
            urgency: 0);
        var closeAlert = CreateLockSoundAlertSchedule(
            isPickAction ? SoundAlertIds.PickLockSoon : SoundAlertIds.BanLockSoon,
            SoundAlertDefaults.DefaultLockSoonThresholdSeconds,
            urgency: 1);

        var schedules = new List<LockSoundAlertSchedule>();
        bool countdownAlertActive = _soundSettings.IsSoundAlertActive(countdownAlert.AlertId);
        bool closeAlertActive = _soundSettings.IsSoundAlertActive(closeAlert.AlertId);
        if (countdownAlertActive && (!closeAlertActive || countdownAlert.ThresholdSeconds > closeAlert.ThresholdSeconds))
            schedules.Add(countdownAlert);

        if (closeAlertActive)
            schedules.Add(closeAlert);

        return schedules
            .OrderByDescending(schedule => schedule.ThresholdSeconds)
            .ThenBy(schedule => schedule.Urgency)
            .ToList();
    }

    private LockSoundAlertSchedule CreateLockSoundAlertSchedule(string alertId, int fallbackThresholdSeconds, int urgency)
    {
        return new LockSoundAlertSchedule(
            alertId,
            _soundSettings.GetSoundAlertThresholdSeconds(alertId) ?? fallbackThresholdSeconds,
            urgency);
    }

    private static int GetLockSoundPlaybackDurationSeconds(DateTime playbackStartsAtUtc, DateTime playbackEndsAtUtc)
    {
        return Math.Clamp(
            (int)Math.Ceiling((playbackEndsAtUtc - playbackStartsAtUtc).TotalSeconds),
            0,
            SoundSettings.MaxSoundAlertPlaybackDurationSeconds);
    }

    private async Task RunScheduledLockSoundAlertAsync(ScheduledLockState scheduledLock, bool isPickAction, string alertId, DateTime alertAtUtc, int? playbackDurationSeconds)
    {
        CancellationToken cancellationToken = scheduledLock.CancellationTokenSource.Token;
        try
        {
            TimeSpan delay = alertAtUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsScheduledLockCurrent(scheduledLock, isPickAction))
                return;

            TryPlayLockCountdownSoundAlert(scheduledLock, isPickAction, alertId, playbackDurationSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            string actionLabel = isPickAction ? "Pick" : "Ban";
            Log($"{actionLabel} lock sound alert failed for {FormatChampion(scheduledLock.ChampionId)} on actionId={scheduledLock.ActionId}: {ex.Message}");
        }
    }

    private void TryPlayLockCompleteSoundAlert(string sessionId, int actionId, int championId, bool isPickAction)
    {
        string alertId = isPickAction ? SoundAlertIds.PickLockComplete : SoundAlertIds.BanLockComplete;
        TryPlaySoundAlertOnce(
            alertId,
            $"{sessionId}:{alertId}:{actionId}:{championId}",
            $"{GetActionLabel(isPickAction)} auto-lock sound alert");
    }

    private void TryPlayLockCountdownSoundAlert(ScheduledLockState scheduledLock, bool isPickAction, string alertId, int? playbackDurationSeconds)
    {
        if (!_soundSettings.IsSoundAlertActive(alertId) || _playSoundAlert is null)
            return;

        bool repeatPlayback = _soundSettings.IsSoundAlertInfinitePlaybackEnabled(alertId);
        if (!repeatPlayback)
            StopLockSoundChannel(scheduledLock, isPickAction);

        _playSoundAlert(SoundAlertPlaybackRequest.PlayAlert(
            alertId,
            $"{GetActionLabel(isPickAction)} auto-lock countdown sound alert",
            repeatPlayback ? GetLockSoundChannelKey(scheduledLock, isPickAction) : null,
            repeatPlayback ? playbackDurationSeconds : null));
    }

    private void TryPreloadSoundAlert(string alertId, string context)
    {
        if (!_soundSettings.IsSoundAlertActive(alertId) || _playSoundAlert is null)
            return;

        _playSoundAlert(SoundAlertPlaybackRequest.PreloadAlert(alertId, context));
    }

    private void StopLockSoundChannel(ScheduledLockState scheduledLock, bool isPickAction)
    {
        _playSoundAlert?.Invoke(SoundAlertPlaybackRequest.StopChannel(GetLockSoundChannelKey(scheduledLock, isPickAction)));
    }

    private bool IsScheduledLockCurrent(ScheduledLockState scheduledLock, bool isPickAction)
    {
        ScheduledLockState? currentSchedule = isPickAction ? _scheduledPickLock : _scheduledBanLock;
        return ReferenceEquals(currentSchedule, scheduledLock)
            && !scheduledLock.CancellationTokenSource.IsCancellationRequested;
    }

    private static string GetLockSoundChannelKey(ScheduledLockState scheduledLock, bool isPickAction)
    {
        string actionType = isPickAction ? "pick" : "ban";
        return $"champ-select-auto-lock:{scheduledLock.SessionId}:{scheduledLock.ActionId}:{actionType}";
    }

    private static string GetActionLabel(bool isPickAction)
    {
        return isPickAction ? "Pick" : "Ban";
    }

    private void TryPlaySoundAlertOnce(string alertId, string dedupeKey, string context, int? playbackDurationSeconds = null)
    {
        if (!_soundSettings.IsSoundAlertActive(alertId) || _playSoundAlert is null)
            return;

        if (!_playedSoundAlertKeys.Add(dedupeKey))
            return;

        _playSoundAlert(SoundAlertPlaybackRequest.PlayAlert(alertId, context, playbackDurationSeconds: playbackDurationSeconds));
    }
}