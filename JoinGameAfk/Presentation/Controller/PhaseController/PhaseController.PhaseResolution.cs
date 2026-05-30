using System.Text.Json;
using JoinGameAfk.Enums;
using LcuClient;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private async Task<ClientPhase> ResolveCurrentPhaseAsync(
            Lcu.LeagueClientHttp http,
            LcuEventSnapshot eventSnapshot,
            CancellationToken cancellationToken)
        {
            if (eventSnapshot.Phase is ClientPhase eventPhase)
                return eventPhase;

            if (eventSnapshot.HasChampSelectSession)
            {
                return IsChampSelectFlow(_lastObservedPhase)
                    ? _lastObservedPhase
                    : ClientPhase.ChampSelect;
            }

            if (IsChampSelectFlow(_lastObservedPhase))
                return _lastObservedPhase;

            var result = await TryGetCurrentPhaseAsync(http, cancellationToken);
            if (result.ReceivedPhase)
            {
                _hasReceivedPhaseResponse = true;
                return result.Phase;
            }

            return ClientPhase.Unknown;
        }

        private static bool TryGetPhaseFromEvent(Lcu.LeagueClientEvent apiEvent, out ClientPhase phase)
        {
            phase = ClientPhase.Unknown;

            if (string.Equals(apiEvent.Uri, "/lol-gameflow/v1/gameflow-phase", StringComparison.OrdinalIgnoreCase))
                return TryParseClientPhase(apiEvent.DataJson.Trim().Trim('"'), out phase);

            try
            {
                using var document = JsonDocument.Parse(apiEvent.DataJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("phase", out var phaseProperty))
                {
                    return TryParseClientPhase(phaseProperty.GetString(), out phase);
                }
            }
            catch (JsonException)
            {
            }

            return false;
        }

        private static bool IsReadyCheckInProgress(Lcu.LeagueClientEvent apiEvent)
        {
            if (IsDeleteEvent(apiEvent))
                return false;

            try
            {
                using var document = JsonDocument.Parse(apiEvent.DataJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (!document.RootElement.TryGetProperty("state", out var stateProperty))
                    return true;

                string? state = stateProperty.GetString();
                return string.IsNullOrWhiteSpace(state)
                    || string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private static bool IsJsonObject(string json)
        {
            string trimmedJson = json.TrimStart();
            return trimmedJson.StartsWith('{');
        }

        private static bool IsDeleteEvent(Lcu.LeagueClientEvent apiEvent)
        {
            return string.Equals(apiEvent.EventType, "Delete", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameAuth(AuthModel? left, AuthModel? right)
        {
            return left is not null
                && right is not null
                && string.Equals(left.Port, right.Port, StringComparison.Ordinal)
                && string.Equals(left.Base64Token, right.Base64Token, StringComparison.Ordinal);
        }

        private static async Task<(bool ReceivedPhase, ClientPhase Phase)> TryGetCurrentPhaseAsync(Lcu.LeagueClientHttp http, CancellationToken cancellationToken)
        {
            try
            {
                string responseBody = await http.GetGameflowPhaseAsync(cancellationToken);
                string trimmedResponseBody = responseBody.Trim();

                if (!trimmedResponseBody.StartsWith('"')
                    && TryParseClientPhase(trimmedResponseBody.Trim('"'), out var rawPhase))
                {
                    return (true, rawPhase);
                }

                using var doc = JsonDocument.Parse(trimmedResponseBody);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    string? phaseStr = doc.RootElement.GetString();
                    if (TryParseClientPhase(phaseStr, out var phase))
                        return (true, phase);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            return (false, ClientPhase.Unknown);
        }

        private static bool TryParseClientPhase(string? phaseText, out ClientPhase phase)
        {
            phase = ClientPhase.Unknown;
            if (string.IsNullOrWhiteSpace(phaseText))
                return false;

            if (Enum.TryParse(phaseText, true, out phase))
                return true;

            if (string.Equals(phaseText, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                phase = ClientPhase.InGame;
                return true;
            }

            if (string.Equals(phaseText, "None", StringComparison.OrdinalIgnoreCase))
            {
                phase = ClientPhase.Unknown;
                return true;
            }

            return false;
        }

        private static bool IsChampSelectFlow(ClientPhase phase)
        {
            return phase is ClientPhase.ChampSelect or ClientPhase.Planning;
        }
    }
}