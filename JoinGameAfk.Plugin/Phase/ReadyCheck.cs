using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using static LcuClient.Lcu;

public class ReadyCheck : IPhaseHandler
{
    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly Action<string>? _log;

    public ReadyCheck(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public ClientPhase ClientPhase => ClientPhase.ReadyCheck;

    public Task HandleAsync(CancellationToken cancellationToken)
    {
        _ = AcceptAfterDelayAsync(cancellationToken);
        return Task.CompletedTask;
    }

    private async Task AcceptAfterDelayAsync(CancellationToken cancellationToken)
    {
        int delaySeconds = Math.Max(0, _settings.ReadyCheckAcceptDelaySeconds);

        try
        {
            if (delaySeconds > 0)
            {
                Log($"Ready check detected. Waiting {delaySeconds}s before auto-accept so you can respond manually.");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await _http.AcceptMatchAsync(cancellationToken);
            Log("Ready check accepted automatically.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log("Ready check auto-accept canceled.");
            Log($"AcceptAfterDelayAsync Token cancellation requested.");
        }
        catch (Exception ex)
        {
            Log($"Ready check auto-accept skipped: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
    }
}