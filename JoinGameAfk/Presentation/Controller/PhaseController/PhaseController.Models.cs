using JoinGameAfk.Enums;

namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private readonly record struct LcuEventSnapshot(
            ClientPhase? Phase,
            string? ChampSelectSessionJson,
            DateTime ChampSelectSessionObservedAtUtc,
            string? ReadyCheckJson,
            string? GameflowSessionJson,
            string? LobbyJson)
        {
            public bool HasChampSelectSession => !string.IsNullOrWhiteSpace(ChampSelectSessionJson);
            public bool HasReadyCheckJson => !string.IsNullOrWhiteSpace(ReadyCheckJson);
            public bool HasGameflowSessionJson => !string.IsNullOrWhiteSpace(GameflowSessionJson);
            public bool HasLobbyJson => !string.IsNullOrWhiteSpace(LobbyJson);
        }

        private readonly record struct QueueSupportState(
            int? QueueId,
            string QueueName,
            bool HasQueue,
            bool IsSupported)
        {
            public static QueueSupportState Unknown { get; } = new(null, string.Empty, false, false);
            public bool IsUnsupported => HasQueue && !IsSupported;
        }
    }
}