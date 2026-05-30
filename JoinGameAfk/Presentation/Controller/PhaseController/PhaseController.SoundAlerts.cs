using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private void PlayPhaseSoundAlert(ClientPhase previousPhase, ClientPhase phase)
        {
            TryPlayChampSelectDodgeSoundAlert(previousPhase, phase);

            string? alertId = phase switch
            {
                ClientPhase.ReadyCheck => SoundAlertIds.ReadyCheck,
                ClientPhase.ChampSelect => SoundAlertIds.ChampSelectStart,
                _ => null
            };

            if (alertId is null)
                return;

            PlaySoundAlert(alertId, $"Phase {phase} sound alert");
        }

        private void TryPlayChampSelectDodgeSoundAlert(ClientPhase previousPhase, ClientPhase phase)
        {
            if (_hasPendingChampSelectExitSound && phase != ClientPhase.Unknown)
            {
                if (IsChampSelectDodgeReturnPhase(phase))
                    HandleChampSelectDodgeReturn();

                _hasPendingChampSelectExitSound = false;
            }

            if (!IsChampSelectFlow(previousPhase) || IsChampSelectFlow(phase))
                return;

            if (phase == ClientPhase.Unknown)
            {
                _hasPendingChampSelectExitSound = true;
                return;
            }

            if (IsChampSelectDodgeReturnPhase(phase))
                HandleChampSelectDodgeReturn();
        }

        private void HandleChampSelectDodgeReturn()
        {
            PlaySoundAlert(SoundAlertIds.ChampSelectEnded, "Champion select dodge sound alert");
            fPhaseProgressionPage.ShowReadyAcceptDashboardView();
        }

        private static bool IsChampSelectDodgeReturnPhase(ClientPhase phase)
        {
            return phase is ClientPhase.Lobby or ClientPhase.Matchmaking or ClientPhase.ReadyCheck;
        }

        private void PlaySoundAlert(string alertId, string context)
        {
            PlaySoundAlert(alertId, context, playbackDurationSecondsOverride: null, channelKey: null);
        }

        private void HandleSoundAlertPlayback(SoundAlertPlaybackRequest request)
        {
            switch (request.Command)
            {
                case SoundAlertPlaybackCommand.StopChannel:
                    _notificationSoundPlayer.StopChannel(request.ChannelKey);
                    return;

                case SoundAlertPlaybackCommand.PreloadAlert:
                    PreloadSoundAlert(request.AlertId, request.Context);
                    return;

                case SoundAlertPlaybackCommand.PlayAlert:
                    if (request.AlertId is not null)
                    {
                        PlaySoundAlert(
                            request.AlertId,
                            request.Context,
                            request.PlaybackDurationSeconds,
                            request.ChannelKey);
                    }

                    return;
            }
        }

        private void PreloadSoundAlert(string? alertId, string context)
        {
            if (alertId is null || !_soundSettings.IsSoundAlertActive(alertId))
                return;

            _notificationSoundPlayer.PreloadAlert(
                _soundSettings.GetSoundAlertSoundKey(alertId),
                context);
        }

        private void PlaySoundAlert(string alertId, string context, int? playbackDurationSecondsOverride, string? channelKey)
        {
            if (!_soundSettings.IsSoundAlertActive(alertId))
                return;

            _notificationSoundPlayer.PlayAlert(
                _soundSettings.GetSoundAlertSoundKey(alertId),
                _soundSettings.GetSoundAlertEffectiveVolumePercent(alertId),
                context,
                new NotificationSoundPlaybackOptions(
                    playbackDurationSecondsOverride ?? _soundSettings.GetSoundAlertPlaybackDurationSeconds(alertId),
                    channelKey));
        }
    }
}