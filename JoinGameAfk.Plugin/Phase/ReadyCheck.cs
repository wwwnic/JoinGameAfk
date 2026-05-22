using System.Net;
using System.Net.Http;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using static LcuClient.Lcu;

public class ReadyCheck : IPhaseHandler
{
    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly Action<string>? _log;
    private readonly object _pendingAcceptLock = new();
    private CancellationTokenSource? _pendingAcceptCts;

    public ReadyCheck(LeagueClientHttp http, ChampSelectSettings settings, Action<string>? log = null)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public ClientPhase ClientPhase => ClientPhase.ReadyCheck;

    public Task HandleAsync(CancellationToken cancellationToken)
    {
        CancelPendingAccept();

        if (!_settings.IsInQueueAutomationActive())
        {
            Log("Ready check detected. Auto-accept is disabled.");
            return Task.CompletedTask;
        }

        var pendingAcceptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_pendingAcceptLock)
        {
            _pendingAcceptCts = pendingAcceptCts;
        }

        _ = AcceptAfterDelayAsync(pendingAcceptCts);
        return Task.CompletedTask;
    }

    public void CancelPendingAccept()
    {
        CancellationTokenSource? pendingAcceptCts;
        lock (_pendingAcceptLock)
        {
            pendingAcceptCts = _pendingAcceptCts;
            _pendingAcceptCts = null;
        }

        try
        {
            pendingAcceptCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task AcceptAfterDelayAsync(CancellationTokenSource pendingAcceptCts)
    {
        CancellationToken cancellationToken = pendingAcceptCts.Token;
        int delaySeconds = Math.Max(0, _settings.ReadyCheckAcceptDelaySeconds);

        try
        {
            if (delaySeconds > 0)
            {
                Log($"Ready check detected. Waiting {delaySeconds}s before auto-accept so you can respond manually.");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_settings.IsInQueueAutomationActive())
            {
                Log("Ready check auto-accept skipped because auto-accept is disabled.");
                return;
            }

            if (!IsCurrentPendingAccept(pendingAcceptCts))
                return;

            ReadyCheckAcceptDecision decision = await GetReadyCheckAcceptDecisionAsync(cancellationToken);
            if (!decision.ShouldAccept)
            {
                Log(decision.SkipMessage);
                return;
            }

            if (!IsCurrentPendingAccept(pendingAcceptCts))
                return;

            bool accepted = await _http.AcceptMatchAsync(cancellationToken);
            Log(accepted
                ? "Ready check accepted automatically."
                : "Ready check auto-accept skipped because it was already accepted or inactive.");
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
        finally
        {
            lock (_pendingAcceptLock)
            {
                if (ReferenceEquals(_pendingAcceptCts, pendingAcceptCts))
                    _pendingAcceptCts = null;
            }

            pendingAcceptCts.Dispose();
        }
    }

    private bool IsCurrentPendingAccept(CancellationTokenSource pendingAcceptCts)
    {
        lock (_pendingAcceptLock)
        {
            return ReferenceEquals(_pendingAcceptCts, pendingAcceptCts);
        }
    }

    private async Task<ReadyCheckAcceptDecision> GetReadyCheckAcceptDecisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            string readyCheckJson = await _http.GetReadyCheckAsync(cancellationToken);
            return ParseReadyCheckAcceptDecision(readyCheckJson);
        }
        catch (HttpRequestException ex) when (IsExpectedReadyCheckInactiveStatus(ex.StatusCode))
        {
            return ReadyCheckAcceptDecision.Skip("Ready check auto-accept skipped because the ready check is no longer active.");
        }
    }

    private static ReadyCheckAcceptDecision ParseReadyCheckAcceptDecision(string readyCheckJson)
    {
        try
        {
            using var document = JsonDocument.Parse(readyCheckJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ReadyCheckAcceptDecision.Skip("Ready check auto-accept skipped because the ready-check state could not be verified.");

            string? state = GetStringProperty(document.RootElement, "state");
            string? playerResponse = GetStringProperty(document.RootElement, "playerResponse");

            if (IsReadyCheckInProgress(state) && IsNoPlayerResponse(playerResponse))
                return ReadyCheckAcceptDecision.Accept;

            if (IsDeclinedPlayerResponse(playerResponse))
                return ReadyCheckAcceptDecision.Skip("Ready check auto-accept skipped because you declined the ready check.");

            if (IsAcceptedPlayerResponse(playerResponse))
                return ReadyCheckAcceptDecision.Skip("Ready check auto-accept skipped because it was already accepted manually.");

            return ReadyCheckAcceptDecision.Skip(
                $"Ready check auto-accept skipped because current state is {FormatReadyCheckValue(state)} and player response is {FormatReadyCheckValue(playerResponse)}.");
        }
        catch (JsonException)
        {
            return ReadyCheckAcceptDecision.Skip("Ready check auto-accept skipped because the ready-check state could not be verified.");
        }
    }

    private static string? GetStringProperty(JsonElement rootElement, string propertyName)
    {
        return rootElement.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool IsReadyCheckInProgress(string? state)
    {
        return string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoPlayerResponse(string? playerResponse)
    {
        return string.Equals(playerResponse, "None", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeclinedPlayerResponse(string? playerResponse)
    {
        return string.Equals(playerResponse, "Declined", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAcceptedPlayerResponse(string? playerResponse)
    {
        return string.Equals(playerResponse, "Accepted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedReadyCheckInactiveStatus(HttpStatusCode? statusCode)
    {
        return statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.NotFound
            or HttpStatusCode.Conflict;
    }

    private static string FormatReadyCheckValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value;
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
    }

    private readonly record struct ReadyCheckAcceptDecision(bool ShouldAccept, string SkipMessage)
    {
        public static ReadyCheckAcceptDecision Accept { get; } = new(true, string.Empty);

        public static ReadyCheckAcceptDecision Skip(string message)
        {
            return new ReadyCheckAcceptDecision(false, message);
        }
    }
}
