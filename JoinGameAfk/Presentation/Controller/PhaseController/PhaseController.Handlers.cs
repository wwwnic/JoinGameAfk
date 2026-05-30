using System.Net.Http;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Phase;
using JoinGameAfk.Plugin.Phase.ReadyCheck;
using LcuClient;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private void InitializeHandlers(Lcu.LeagueClientHttp http)
        {
            _phaseHandlers.Clear();
            _phaseHandlers.Add(new ReadyCheck(http, _generalSettings, Log));
            _phaseHandlers.Add(new ChampSelect(http, _generalSettings, _rolePlanSettings, _soundSettings, Log, SignalLcuEvent, HandleSoundAlertPlayback));
        }

        private async Task<bool> TryHandleChampSelectAsync(ChampSelect champSelect, LcuEventSnapshot eventSnapshot, CancellationToken cancellationToken)
        {
            try
            {
                if (eventSnapshot.HasChampSelectSession)
                {
                    await champSelect.HandleSessionJsonAsync(
                        eventSnapshot.ChampSelectSessionJson!,
                        eventSnapshot.ChampSelectSessionObservedAtUtc,
                        cancellationToken);
                }
                else
                {
                    await champSelect.HandleAsync(cancellationToken);
                }

                fPhaseProgressionPage.UpdateDashboardStatus(champSelect.LastDashboardStatus);
                return true;
            }
            catch (HttpRequestException ex)
            {
                Log($"Champ Select session unavailable. Returning to phase detection. {ex.Message}");
                _lastObservedPhase = ClientPhase.Unknown;
                _lastHandledPhase = ClientPhase.Unknown;
                fPhaseProgressionPage.UpdatePhase(ClientPhase.Unknown);
                fPhaseProgressionPage.UpdateDashboardStatus(new DashboardStatus());
                champSelect.Reset();
                return false;
            }
        }

        private async Task UpdateReadyCheckDashboardStatusAsync(
            Lcu.LeagueClientHttp http,
            LcuEventSnapshot eventSnapshot,
            QueueSupportState queueSupportState,
            CancellationToken cancellationToken)
        {
            string? readyCheckJson = eventSnapshot.HasReadyCheckJson
                ? eventSnapshot.ReadyCheckJson
                : null;

            if (string.IsNullOrWhiteSpace(readyCheckJson))
            {
                try
                {
                    readyCheckJson = await http.GetReadyCheckAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException)
                {
                    fPhaseProgressionPage.UpdateDashboardStatus(BuildNonChampSelectDashboardStatus(queueSupportState));
                    return;
                }
            }

            var readyCheckHandler = _phaseHandlers.OfType<ReadyCheck>().FirstOrDefault();
            string readyCheckResponse = GetReadyCheckResponse(readyCheckJson);
            if (!string.IsNullOrWhiteSpace(readyCheckResponse)
                || !_generalSettings.IsInQueueAutomationActive())
            {
                readyCheckHandler?.CancelPendingAccept();
            }

            ReadyCheck.AutoAcceptCountdownSnapshot countdown = string.IsNullOrWhiteSpace(readyCheckResponse)
                ? readyCheckHandler?.GetPendingAutoAcceptCountdown() ?? ReadyCheck.AutoAcceptCountdownSnapshot.Empty
                : ReadyCheck.AutoAcceptCountdownSnapshot.Empty;

            DashboardStatus status = new DashboardStatus
            {
                ReadyCheckResponse = readyCheckResponse,
                ReadyCheckAutoAcceptDelayMilliseconds = countdown.TotalDelayMilliseconds,
                ReadyCheckAutoAcceptTimeLeftMilliseconds = countdown.RemainingMilliseconds,
                ReadyCheckAutoAcceptObservedAtUtc = countdown.ObservedAtUtc
            };

            fPhaseProgressionPage.UpdateDashboardStatus(ApplyQueueSupportWarning(status, queueSupportState));
        }

        private static string GetReadyCheckResponse(string readyCheckJson)
        {
            try
            {
                using var document = JsonDocument.Parse(readyCheckJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("playerResponse", out JsonElement playerResponseProperty)
                    || playerResponseProperty.ValueKind != JsonValueKind.String)
                {
                    return string.Empty;
                }

                string? playerResponse = playerResponseProperty.GetString();
                if (string.Equals(playerResponse, "Accepted", StringComparison.OrdinalIgnoreCase))
                    return "Accepted";

                if (string.Equals(playerResponse, "Declined", StringComparison.OrdinalIgnoreCase))
                    return "Declined";
            }
            catch (JsonException)
            {
            }

            return string.Empty;
        }

        private static string? GetActionMessage(ClientPhase phase)
        {
            return phase switch
            {
                ClientPhase.ReadyCheck => "Ready check detected.",
                ClientPhase.ChampSelect => "Champion Select is now active. Automation will follow your settings.",
                _ => null
            };
        }
    }
}
