namespace JoinGameAfk.Presentation.Controller
{
    public partial class PhaseController
    {
        private void Log(string message)
        {
            if (!_isShutdownRequested)
                _logsPage.WriteLine(message);

            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            if (!_isShutdownRequested)
                _logsPage.WriteErrorLine(message);

            Console.Error.WriteLine(message);
        }
    }
}