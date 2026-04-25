using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using System.Text.Json;
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

    public void Handle()
    {
        _ = AcceptAfterDelayAsync();
    }

    private async Task AcceptAfterDelayAsync()
    {
        int delaySeconds = Math.Max(0, _settings.ReadyCheckAcceptDelaySeconds);

        try
        {
            if (delaySeconds > 0)
            {
                Log($"Ready check detected. Waiting {delaySeconds}s before auto-accept so you can respond manually.");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
            }

            if (!await IsStillInReadyCheckAsync().ConfigureAwait(false))
            {
                Log("Ready check was already handled manually. Skipping auto-accept.");
                return;
            }

            await _http.AcceptMatchAsync().ConfigureAwait(false);
            Log("Ready check accepted automatically.");
        }
        catch (Exception ex)
        {
            Log($"Ready check auto-accept skipped: {ex.Message}");
        }
    }

    private async Task<bool> IsStillInReadyCheckAsync()
    {
        string json = await _http.GetSessionAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("phase", out var phaseProperty)
            && string.Equals(phaseProperty.GetString(), ClientPhase.ReadyCheck.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
    }
}