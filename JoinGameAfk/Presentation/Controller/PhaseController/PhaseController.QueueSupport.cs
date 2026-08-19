using System.Net.Http;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using LcuClient;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private const int CustomQueueIdMinimum = 3000;

        private static readonly IReadOnlyDictionary<LeagueQueueId, string> SupportedQueueNames = new Dictionary<LeagueQueueId, string>
        {
            [LeagueQueueId.NormalDraft] = "Normal Draft",
            [LeagueQueueId.RankedSoloDuo] = "Ranked Solo/Duo",
            [LeagueQueueId.BlindPick] = "Blind Pick",
            [LeagueQueueId.RankedFlex] = "Ranked Flex",
            [LeagueQueueId.CustomDraft] = "Custom Game Draft",
            [LeagueQueueId.LeagueClassicCustomDraft] = "League Classic Custom Draft",
            [LeagueQueueId.LeagueClassicPvpDraft] = "League Classic"
        };

        private static readonly IReadOnlyDictionary<LeagueQueueId, string> KnownQueueNames = new Dictionary<LeagueQueueId, string>
        {
            [LeagueQueueId.NormalDraft] = "Normal Draft",
            [LeagueQueueId.RankedSoloDuo] = "Ranked Solo/Duo",
            [LeagueQueueId.BlindPick] = "Blind Pick",
            [LeagueQueueId.RankedFlex] = "Ranked Flex",
            [LeagueQueueId.Aram] = "ARAM",
            [LeagueQueueId.Swiftplay] = "Swiftplay",
            [LeagueQueueId.Quickplay] = "Quickplay",
            [LeagueQueueId.Clash] = "Clash",
            [LeagueQueueId.AramClash] = "ARAM Clash",
            [LeagueQueueId.IntroBots] = "Intro Bots",
            [LeagueQueueId.BeginnerBots] = "Beginner Bots",
            [LeagueQueueId.IntermediateBots] = "Intermediate Bots",
            [LeagueQueueId.Arurf] = "ARURF",
            [LeagueQueueId.OneForAll] = "One for All",
            [LeagueQueueId.UltimateSpellbook] = "Ultimate Spellbook",
            [LeagueQueueId.Arena] = "Arena",
            [LeagueQueueId.ArenaAlternate] = "Arena",
            [LeagueQueueId.PickUrf] = "Pick URF",
            [LeagueQueueId.Brawl] = "Brawl",
            [LeagueQueueId.AramMayhem] = "ARAM: Mayhem",
            [LeagueQueueId.CustomDraft] = "Custom Game Draft",
            [LeagueQueueId.CustomClassic] = "Custom Game Classic"
        };

        private async Task<QueueSupportState> ResolveQueueSupportStateAsync(
            Lcu.LeagueClientHttp http,
            LcuEventSnapshot eventSnapshot,
            ClientPhase phase,
            CancellationToken cancellationToken)
        {
            if (phase is ClientPhase.Unknown or ClientPhase.InGame)
            {
                ResetQueueSupportState();
                return QueueSupportState.Unknown;
            }

            if (TryGetQueueSupportStateFromEventSnapshot(eventSnapshot, phase, out var eventState))
                return CacheQueueSupportState(eventState);

            if (IsChampSelectFlow(phase) && HasConcreteQueueId(_lastQueueSupportState))
            {
                _hasLookedUpQueueSupportDuringReadyCheck = false;
                return _lastQueueSupportState;
            }

            if (phase == ClientPhase.ReadyCheck)
            {
                if (_lastQueueSupportState.HasQueue)
                    return _lastQueueSupportState;

                if (_hasLookedUpQueueSupportDuringReadyCheck)
                    return _lastQueueSupportState;

                _hasLookedUpQueueSupportDuringReadyCheck = true;
            }
            else
            {
                _hasLookedUpQueueSupportDuringReadyCheck = false;
            }

            if (phase is ClientPhase.Lobby or ClientPhase.Matchmaking or ClientPhase.ReadyCheck)
            {
                if (await TryGetLobbyQueueSupportStateAsync(http, cancellationToken) is { HasQueue: true } lobbyState)
                    return CacheQueueSupportState(lobbyState);
            }

            if (IsChampSelectFlow(phase) || phase == ClientPhase.ReadyCheck)
            {
                if (await TryGetGameflowQueueSupportStateAsync(http, cancellationToken) is { HasQueue: true } gameflowState)
                    return CacheQueueSupportState(gameflowState);
            }

            return _lastQueueSupportState;
        }

        private static bool HasConcreteQueueId(QueueSupportState queueSupportState)
        {
            return queueSupportState.QueueId is int queueId && IsConcreteQueueId(queueId);
        }

        private static bool TryGetQueueSupportStateFromEventSnapshot(
            LcuEventSnapshot eventSnapshot,
            ClientPhase phase,
            out QueueSupportState queueSupportState)
        {
            try
            {
                if (IsChampSelectFlow(phase))
                {
                    return TryParseGameflowQueueSupportState(eventSnapshot.GameflowSessionJson, out queueSupportState)
                        || TryParseLobbyQueueSupportState(eventSnapshot.LobbyJson, out queueSupportState);
                }

                return TryParseLobbyQueueSupportState(eventSnapshot.LobbyJson, out queueSupportState)
                    || TryParseGameflowQueueSupportState(eventSnapshot.GameflowSessionJson, out queueSupportState);
            }
            catch (JsonException)
            {
                queueSupportState = QueueSupportState.Unknown;
                return false;
            }
        }

        private async Task<QueueSupportState?> TryGetLobbyQueueSupportStateAsync(
            Lcu.LeagueClientHttp http,
            CancellationToken cancellationToken)
        {
            try
            {
                string lobbyJson = await http.GetLobbyAsync(cancellationToken);
                return TryParseLobbyQueueSupportState(lobbyJson, out var state)
                    ? state
                    : QueueSupportState.Unknown;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return QueueSupportState.Unknown;
            }
        }

        private async Task<QueueSupportState?> TryGetGameflowQueueSupportStateAsync(
            Lcu.LeagueClientHttp http,
            CancellationToken cancellationToken)
        {
            try
            {
                string gameflowJson = await http.GetGameflowSessionAsync(cancellationToken);
                return TryParseGameflowQueueSupportState(gameflowJson, out var state)
                    ? state
                    : QueueSupportState.Unknown;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (JsonException)
            {
                return QueueSupportState.Unknown;
            }
        }

        private QueueSupportState CacheQueueSupportState(QueueSupportState queueSupportState)
        {
            if (queueSupportState.HasQueue)
                _lastQueueSupportState = queueSupportState;

            return queueSupportState;
        }

        private void ResetQueueSupportState()
        {
            _lastQueueSupportState = QueueSupportState.Unknown;
            _hasLookedUpQueueSupportDuringReadyCheck = false;
            _lastUnsupportedQueueLogKey = string.Empty;
        }

        private static bool TryParseLobbyQueueSupportState(string? lobbyJson, out QueueSupportState queueSupportState)
        {
            queueSupportState = QueueSupportState.Unknown;
            if (string.IsNullOrWhiteSpace(lobbyJson))
                return false;

            using var document = JsonDocument.Parse(lobbyJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("gameConfig", out var gameConfig)
                && gameConfig.ValueKind == JsonValueKind.Object
                && TryReadInt32(gameConfig, "queueId", out int gameConfigQueueId))
            {
                return TryCreateQueueSupportState(gameConfigQueueId, TryReadQueueDescription(gameConfig), out queueSupportState);
            }

            if (TryReadInt32(root, "queueId", out int rootQueueId))
            {
                return TryCreateQueueSupportState(rootQueueId, TryReadQueueDescription(root), out queueSupportState);
            }

            return false;
        }

        private static bool TryParseGameflowQueueSupportState(string? gameflowJson, out QueueSupportState queueSupportState)
        {
            queueSupportState = QueueSupportState.Unknown;
            if (string.IsNullOrWhiteSpace(gameflowJson))
                return false;

            using var document = JsonDocument.Parse(gameflowJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("gameData", out var gameData)
                && gameData.ValueKind == JsonValueKind.Object)
            {
                if (gameData.TryGetProperty("queue", out var queue)
                    && queue.ValueKind == JsonValueKind.Object
                    && TryReadQueueId(queue, out int queueObjectId))
                {
                    return TryCreateQueueSupportState(queueObjectId, TryReadQueueDescription(queue), out queueSupportState);
                }

                if (TryReadInt32(gameData, "queueId", out int gameDataQueueId))
                {
                    return TryCreateQueueSupportState(gameDataQueueId, TryReadQueueDescription(gameData), out queueSupportState);
                }
            }

            if (TryReadInt32(root, "queueId", out int rootQueueId))
            {
                return TryCreateQueueSupportState(rootQueueId, TryReadQueueDescription(root), out queueSupportState);
            }

            return false;
        }

        private static bool TryReadQueueId(JsonElement queue, out int queueId)
        {
            return TryReadInt32(queue, "id", out queueId)
                || TryReadInt32(queue, "queueId", out queueId);
        }

        private static bool TryReadInt32(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number)
                return property.TryGetInt32(out value);

            if (property.ValueKind == JsonValueKind.String
                && int.TryParse(property.GetString(), out value))
            {
                return true;
            }

            return false;
        }

        private static string TryReadQueueDescription(JsonElement element)
        {
            foreach (string propertyName in new[] { "description", "name", "shortName", "queueName", "gameMode" })
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.String)
                {
                    string? value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value)
                        && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        return value.Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static bool TryCreateQueueSupportState(int queueId, string queueDescription, out QueueSupportState queueSupportState)
        {
            queueSupportState = QueueSupportState.Unknown;
            if (!IsConcreteQueueId(queueId))
                return false;

            bool isSupported = IsSupportedQueueId(queueId);
            string queueName = GetQueueName(queueId, queueDescription);
            queueSupportState = new QueueSupportState(queueId, queueName, true, isSupported);
            return true;
        }

        private static bool IsConcreteQueueId(int queueId)
        {
            return queueId > 0;
        }

        private static bool IsSupportedQueueId(int queueId)
        {
            return SupportedQueueNames.ContainsKey((LeagueQueueId)queueId);
        }

        private static string GetQueueName(int queueId, string queueDescription)
        {
            var knownQueueId = (LeagueQueueId)queueId;
            if (SupportedQueueNames.TryGetValue(knownQueueId, out string? supportedName)
                && !string.IsNullOrWhiteSpace(supportedName))
                return supportedName;

            if (KnownQueueNames.TryGetValue(knownQueueId, out string? knownName))
                return knownName;

            if (IsCustomQueueId(queueId))
                return FormatCustomQueueName(queueDescription);

            return !string.IsNullOrWhiteSpace(queueDescription)
                ? queueDescription.Trim()
                : $"Queue {queueId}";
        }

        private static string FormatCustomQueueName(string queueDescription)
        {
            if (string.IsNullOrWhiteSpace(queueDescription))
                return "Custom Game";

            string description = queueDescription.Trim();
            return string.Equals(description, "CLASSIC", StringComparison.OrdinalIgnoreCase)
                ? "Custom Game Classic"
                : $"Custom Game {description}";
        }

        private static DashboardStatus BuildNonChampSelectDashboardStatus(QueueSupportState queueSupportState)
        {
            return ApplyQueueSupportWarning(new DashboardStatus(), queueSupportState);
        }

        private static DashboardStatus BuildUnsupportedModeDashboardStatus(QueueSupportState queueSupportState)
        {
            return ApplyQueueSupportWarning(new DashboardStatus(), queueSupportState);
        }

        private static DashboardStatus ApplyQueueSupportWarning(DashboardStatus status, QueueSupportState queueSupportState)
        {
            status = ApplyQueueFlowStatus(status, queueSupportState);

            if (!queueSupportState.IsUnsupported)
                return status;

            return status with
            {
                IsUnsupportedMode = true,
                UnsupportedQueueText = queueSupportState.QueueName,
                UnsupportedModeText = FormatUnsupportedModeText(queueSupportState)
            };
        }

        private static DashboardStatus ApplyQueueFlowStatus(DashboardStatus status, QueueSupportState queueSupportState)
        {
            return IsReadyCheckSkippedQueue(queueSupportState)
                    ? status with { SkipsReadyCheck = true }
                    : status;
        }

        private static ClientPhase GetEffectivePhaseForQueueFlow(ClientPhase phase, QueueSupportState queueSupportState)
        {
            return phase == ClientPhase.ReadyCheck && IsReadyCheckSkippedQueue(queueSupportState)
                ? ClientPhase.ChampSelect
                : phase;
        }

        private static bool IsReadyCheckSkippedQueue(QueueSupportState queueSupportState)
        {
            return queueSupportState.QueueId is int queueId
                && IsCustomQueueId(queueId);
        }

        private static bool IsCustomQueueId(int queueId)
        {
            // Riot's public League Classic queue lives in the otherwise custom-game
            // queue-id range, but it still uses the regular matchmaking ready check.
            return queueId >= CustomQueueIdMinimum
                && queueId != (int)LeagueQueueId.LeagueClassicPvpDraft;
        }

        private static string FormatUnsupportedQueueLogText(QueueSupportState queueSupportState)
        {
            return queueSupportState.QueueId is int queueId
                ? $"{queueSupportState.QueueName} (queue {queueId})"
                : queueSupportState.QueueName;
        }

        private static string FormatUnsupportedModeText(QueueSupportState queueSupportState)
        {
            string modeText = FormatUnsupportedQueueLogText(queueSupportState);
            return $"{modeText} is not supported for draft tools. Use Normal Draft, Ranked Solo/Duo, Ranked Flex, or League Classic; auto-accept can still work here.";
        }

        private void LogUnsupportedQueueIfNeeded(QueueSupportState queueSupportState)
        {
            string logKey = queueSupportState.QueueId?.ToString() ?? queueSupportState.QueueName;
            if (string.Equals(_lastUnsupportedQueueLogKey, logKey, StringComparison.Ordinal))
                return;

            _lastUnsupportedQueueLogKey = logKey;
            Log($"Unsupported queue detected: {FormatUnsupportedModeText(queueSupportState)}");
        }
    }
}
