using System.Net;
using System.Net.Http;
using System.Text.Json;
using JoinGameAfk.Enums;
using JoinGameAfk.Interface;
using JoinGameAfk.Model;
using static LcuClient.Lcu;

namespace JoinGameAfk.Phase;

public class ReadyCheck : IPhaseHandler
{
    private readonly LeagueClientHttp _http;
    private readonly ChampSelectSettings _settings;
    private readonly Action<string>? _log;
    private readonly object _pendingAcceptLock = new();
    private PendingAccept? _pendingAccept;

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

        int delaySeconds = Math.Max(0, _settings.ReadyCheckAcceptDelaySeconds);
        long delayMilliseconds = delaySeconds * 1000L;
        DateTime scheduledAtUtc = DateTime.UtcNow;
        var pendingAcceptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pendingAccept = new PendingAccept(
            pendingAcceptCts,
            delayMilliseconds,
            scheduledAtUtc.AddMilliseconds(delayMilliseconds));

        lock (_pendingAcceptLock)
        {
            _pendingAccept = pendingAccept;
        }

        _ = AcceptAfterDelayAsync(pendingAccept);
        return Task.CompletedTask;
    }

    public void CancelPendingAccept()
    {
        PendingAccept? pendingAccept;
        lock (_pendingAcceptLock)
        {
            pendingAccept = _pendingAccept;
            _pendingAccept = null;
        }

        try
        {
            pendingAccept?.CancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public AutoAcceptCountdownSnapshot GetPendingAutoAcceptCountdown()
    {
        lock (_pendingAcceptLock)
        {
            if (_pendingAccept is not { TotalDelayMilliseconds: > 0 } pendingAccept
                || !_settings.IsInQueueAutomationActive())
            {
                return AutoAcceptCountdownSnapshot.Empty;
            }

            DateTime observedAtUtc = DateTime.UtcNow;
            long remainingMilliseconds = Math.Max(
                0,
                (long)Math.Ceiling((pendingAccept.AcceptAtUtc - observedAtUtc).TotalMilliseconds));

            return new AutoAcceptCountdownSnapshot(
                pendingAccept.TotalDelayMilliseconds,
                remainingMilliseconds,
                observedAtUtc);
        }
    }

    private async Task AcceptAfterDelayAsync(PendingAccept pendingAccept)
    {
        CancellationToken cancellationToken = pendingAccept.CancellationTokenSource.Token;
        long delayMilliseconds = pendingAccept.TotalDelayMilliseconds;
        double delaySeconds = delayMilliseconds / 1000d;

        try
        {
            if (delayMilliseconds > 0)
            {
                Log($"Ready check detected. Waiting {delaySeconds:0.#}s before auto-accept so you can respond manually.");
                await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_settings.IsInQueueAutomationActive())
            {
                Log("Ready check auto-accept skipped because auto-accept is disabled.");
                return;
            }

            if (!IsCurrentPendingAccept(pendingAccept))
                return;

            ReadyCheckAcceptDecision decision = await GetReadyCheckAcceptDecisionAsync(cancellationToken);
            if (!decision.ShouldAccept)
            {
                Log(decision.SkipMessage);
                return;
            }

            if (!IsCurrentPendingAccept(pendingAccept))
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
                if (ReferenceEquals(_pendingAccept, pendingAccept))
                    _pendingAccept = null;
            }

            pendingAccept.CancellationTokenSource.Dispose();
        }
    }

    private bool IsCurrentPendingAccept(PendingAccept pendingAccept)
    {
        lock (_pendingAcceptLock)
        {
            return ReferenceEquals(_pendingAccept, pendingAccept);
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

    private sealed record PendingAccept(
        CancellationTokenSource CancellationTokenSource,
        long TotalDelayMilliseconds,
        DateTime AcceptAtUtc);

    public readonly record struct AutoAcceptCountdownSnapshot(
        long TotalDelayMilliseconds,
        long RemainingMilliseconds,
        DateTime ObservedAtUtc)
    {
        public static AutoAcceptCountdownSnapshot Empty { get; } = new(-1, -1, DateTime.MinValue);

        public bool HasValue => TotalDelayMilliseconds > 0
            && RemainingMilliseconds >= 0
            && ObservedAtUtc != DateTime.MinValue;
    }
}
