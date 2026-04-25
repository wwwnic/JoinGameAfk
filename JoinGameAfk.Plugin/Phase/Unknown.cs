using JoinGameAfk.Enums;
using JoinGameAfk.Interface;

namespace JoinGameAfk.Phase
{
    internal class Unknown : IPhaseHandler
    {
        public ClientPhase ClientPhase => ClientPhase.Unknown;

        public void Handle()
        {
        }
    }
}