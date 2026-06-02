using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using JoinGameAfk.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JoinGameAfk.Tools.MockLeagueClient;

internal sealed class MockLeagueClientServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly MockLeagueClientState _state;
    private readonly int _port;
    private readonly string _token;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<Guid, WebSocketConnection> _webSockets = new();
    private WebApplication? _app;

    public MockLeagueClientServer(MockLeagueClientState state, int port, string token, Action<string> log)
    {
        _state = state;
        _port = port;
        _token = token;
        _log = log;
    }

    public bool IsRunning => _app is not null;

    public async Task StartAsync()
    {
        if (_app is not null)
            return;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(_port, ConfigureHttpsEndpoint);
        });

        var app = builder.Build();
        app.UseWebSockets();
        app.Use(HandleWebSocketRootAsync);
        MapRoutes(app);

        await app.StartAsync();
        _app = app;
        _log($"Mock League Client listening on https://127.0.0.1:{_port}/ with token '{_token}'.");
    }

    public async Task StopAsync()
    {
        if (_app is null)
            return;

        foreach (var connection in _webSockets.Values)
        {
            try
            {
                if (connection.Socket.State == WebSocketState.Open)
                {
                    await connection.Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Mock server stopped.",
                        CancellationToken.None);
                }
            }
            catch
            {
            }

            connection.Dispose();
        }

        _webSockets.Clear();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
        _log("Mock League Client stopped.");
    }

    public async Task EmitSnapshotAsync()
    {
        if (_app is null)
            return;

        await BroadcastEventAsync("/lol-gameflow/v1/gameflow-phase", _state.GetGameflowPhase());
        await BroadcastEventAsync("/lol-gameflow/v1/session", _state.GetGameflowSessionPayload());
        await BroadcastEventAsync("/lol-lobby/v2/lobby", _state.GetLobbyPayload());

        if (_state.HasReadyCheck())
            await BroadcastEventAsync("/lol-matchmaking/v1/ready-check", _state.GetReadyCheckPayload());

        if (_state.HasChampSelectSession())
            await BroadcastEventAsync("/lol-champ-select/v1/session", _state.GetChampSelectSessionPayload());
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static void ConfigureHttpsEndpoint(ListenOptions listenOptions)
    {
        listenOptions.UseHttps();
    }

    private void MapRoutes(WebApplication app)
    {
        app.MapGet("/", () => Results.Text("JoinGameAfk MockLeagueClient is running.", "text/plain"));

        app.MapGet("/lol-gameflow/v1/gameflow-phase", () =>
        {
            LogRequest("GET", "/lol-gameflow/v1/gameflow-phase");
            return Results.Json(_state.GetGameflowPhase(), JsonOptions);
        });

        app.MapGet("/lol-gameflow/v1/session", () =>
        {
            LogRequest("GET", "/lol-gameflow/v1/session");
            return Results.Json(_state.GetGameflowSessionPayload(), JsonOptions);
        });

        app.MapGet("/lol-lobby/v2/lobby", () =>
        {
            LogRequest("GET", "/lol-lobby/v2/lobby");
            return Results.Json(_state.GetLobbyPayload(), JsonOptions);
        });

        app.MapGet("/lol-matchmaking/v1/ready-check", () =>
        {
            LogRequest("GET", "/lol-matchmaking/v1/ready-check");
            return _state.HasReadyCheck()
                ? Results.Json(_state.GetReadyCheckPayload(), JsonOptions)
                : Results.NotFound();
        });

        app.MapPost("/lol-matchmaking/v1/ready-check/accept", async () =>
        {
            LogRequest("POST", "/lol-matchmaking/v1/ready-check/accept");
            if (!_state.HasReadyCheck())
                return Results.Conflict();

            _state.AcceptReadyCheck();
            await BroadcastEventAsync("/lol-matchmaking/v1/ready-check", _state.GetReadyCheckPayload());
            return Results.Json(new { accepted = true }, JsonOptions);
        });

        app.MapGet("/lol-champ-select/v1/session", () =>
        {
            LogRequest("GET", "/lol-champ-select/v1/session");
            return _state.HasChampSelectSession()
                ? Results.Json(_state.GetChampSelectSessionPayload(), JsonOptions)
                : Results.NotFound();
        });

        app.MapPatch("/lol-champ-select/v1/session/actions/{actionId:int}", async (int actionId, HttpContext context) =>
        {
            var patch = await ReadActionPatchAsync(context);
            LogRequest("PATCH", $"/lol-champ-select/v1/session/actions/{actionId}", patch.Description);

            if (!_state.PatchAction(actionId, patch.ChampionId, patch.Completed))
                return Results.NotFound();

            await BroadcastEventAsync("/lol-champ-select/v1/session", _state.GetChampSelectSessionPayload());
            return Results.Json(new { updated = true }, JsonOptions);
        });

        app.MapGet("/lol-summoner/v1/current-summoner", () =>
        {
            LogRequest("GET", "/lol-summoner/v1/current-summoner");
            return Results.Json(_state.GetCurrentSummonerPayload(), JsonOptions);
        });

        app.MapGet("/lol-champions/v1/inventories/{summonerId:long}/champions-minimal", (long summonerId) =>
        {
            LogRequest("GET", $"/lol-champions/v1/inventories/{summonerId}/champions-minimal");
            return Results.Json(_state.GetChampionInventoryPayload(), JsonOptions);
        });

        app.MapGet("/lol-champions/v1/owned-champions-minimal", () =>
        {
            LogRequest("GET", "/lol-champions/v1/owned-champions-minimal");
            return Results.Json(_state.GetChampionInventoryPayload(), JsonOptions);
        });
    }

    private async Task HandleWebSocketRootAsync(HttpContext context, Func<Task> next)
    {
        if (!context.WebSockets.IsWebSocketRequest || context.Request.Path != "/")
        {
            await next();
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        var connection = new WebSocketConnection(socket);
        _webSockets[id] = connection;
        _log("LCU websocket connected.");

        try
        {
            await SendSnapshotToSocketAsync(connection);
            await ReceiveLoopAsync(id, connection, context.RequestAborted);
        }
        finally
        {
            _webSockets.TryRemove(id, out _);
            connection.Dispose();
            _log("LCU websocket disconnected.");
        }
    }

    private async Task ReceiveLoopAsync(Guid id, WebSocketConnection connection, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
        {
            var result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (message.Contains("OnJsonApiEvent", StringComparison.Ordinal))
            {
                _log("LCU websocket subscribed to OnJsonApiEvent.");
                await SendSnapshotToSocketAsync(connection);
            }
            else
            {
                _log($"LCU websocket message: {message}");
            }
        }

        _webSockets.TryRemove(id, out _);
    }

    private async Task SendSnapshotToSocketAsync(WebSocketConnection connection)
    {
        await SendEventToSocketAsync(connection, "/lol-gameflow/v1/gameflow-phase", _state.GetGameflowPhase());
        await SendEventToSocketAsync(connection, "/lol-gameflow/v1/session", _state.GetGameflowSessionPayload());
        await SendEventToSocketAsync(connection, "/lol-lobby/v2/lobby", _state.GetLobbyPayload());

        if (_state.HasReadyCheck())
            await SendEventToSocketAsync(connection, "/lol-matchmaking/v1/ready-check", _state.GetReadyCheckPayload());

        if (_state.HasChampSelectSession())
            await SendEventToSocketAsync(connection, "/lol-champ-select/v1/session", _state.GetChampSelectSessionPayload());
    }

    private async Task BroadcastEventAsync(string uri, object data)
    {
        string json = CreateEventJson(uri, data);
        foreach (var pair in _webSockets.ToArray())
        {
            try
            {
                await SendTextAsync(pair.Value, json);
            }
            catch (Exception ex)
            {
                _log($"LCU websocket send failed: {ex.Message}");
                if (_webSockets.TryRemove(pair.Key, out var removed))
                    removed.Dispose();
            }
        }

        _log($"LCU websocket event: {uri}");
    }

    private static async Task SendEventToSocketAsync(WebSocketConnection connection, string uri, object data)
    {
        await SendTextAsync(connection, CreateEventJson(uri, data));
    }

    private static string CreateEventJson(string uri, object data)
    {
        object envelope = new object[]
        {
            8,
            "OnJsonApiEvent",
            new
            {
                uri,
                eventType = "Update",
                data
            }
        };

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static async Task SendTextAsync(WebSocketConnection connection, string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        await connection.SendLock.WaitAsync();
        try
        {
            if (connection.Socket.State == WebSocketState.Open)
            {
                await connection.Socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None);
            }
        }
        finally
        {
            connection.SendLock.Release();
        }
    }

    private static async Task<ActionPatch> ReadActionPatchAsync(HttpContext context)
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body);
        var root = document.RootElement;
        int? championId = root.TryGetProperty("championId", out var championIdProperty)
                          && championIdProperty.ValueKind == JsonValueKind.Number
                          && championIdProperty.TryGetInt32(out int championIdValue)
            ? championIdValue
            : null;
        bool? completed = root.TryGetProperty("completed", out var completedProperty)
                          && completedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? completedProperty.GetBoolean()
            : null;

        string description = FormatActionPatchDescription(championId, completed);
        return new ActionPatch(championId, completed, description);
    }

    private static string FormatActionPatchDescription(int? championId, bool? completed)
    {
        return JsonSerializer.Serialize(
            new
            {
                championId,
                champion = championId is int id ? FormatChampionName(id) : null,
                completed
            },
            JsonOptions);
    }

    private static string FormatChampionName(int championId)
    {
        if (championId <= 0)
            return "No champion";

        return ChampionCatalog.TryGetById(championId, out var champion) && champion is not null
            ? champion.Name
            : $"Champion {championId}";
    }

    private void LogRequest(string method, string endpoint, string? detail = null)
    {
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        _log($"HTTP {method} {endpoint}{suffix}");
    }

    private sealed record ActionPatch(int? ChampionId, bool? Completed, string Description);

    private sealed class WebSocketConnection : IDisposable
    {
        public WebSocketConnection(WebSocket socket)
        {
            Socket = socket;
        }

        public WebSocket Socket { get; }

        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public void Dispose()
        {
            SendLock.Dispose();
            Socket.Dispose();
        }
    }
}
