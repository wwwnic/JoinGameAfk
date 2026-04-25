using JoinGameAfk.Enums;

namespace JoinGameAfk.Interface
{
    /// <summary>
    /// A phase returned from the LCU API.
    /// </summary>
    public interface IPhaseHandler
    {
        ClientPhase ClientPhase { get; }

        void Handle();
    }
}