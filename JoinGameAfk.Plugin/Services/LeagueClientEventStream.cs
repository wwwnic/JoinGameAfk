using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LcuClient
{
    public partial class Lcu
    {
        public sealed class LeagueClientEventStream : IDisposable
        {
            private const int ReceiveBufferSize = 32 * 1024;
            private readonly AuthModel _authToken;
            private readonly Action<LeagueClientEvent>? _eventReceived;
            private readonly Action<string>? _log;
            private ClientWebSocket? _webSocket;
            private bool _disposed;

            public LeagueClientEventStream(
                AuthModel authToken,
                Action<LeagueClientEvent>? eventReceived = null,
                Action<string>? log = null)
            {
                _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
                _eventReceived = eventReceived;
                _log = log;
            }

            public async Task RunAsync(CancellationToken cancellationToken)
            {
                ThrowIfDisposed();

                using var webSocket = CreateWebSocket();
                _webSocket = webSocket;

                await webSocket.ConnectAsync(new Uri($"wss://127.0.0.1:{_authToken.Port}/"), cancellationToken)
                    .ConfigureAwait(false);

                await SendTextAsync(webSocket, "[5,\"OnJsonApiEvent\"]", cancellationToken)
                    .ConfigureAwait(false);

                _log?.Invoke("LCU websocket event stream connected.");
                await ReceiveLoopAsync(webSocket, cancellationToken).ConfigureAwait(false);
            }

            private ClientWebSocket CreateWebSocket()
            {
                var webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader("Authorization", $"Basic {_authToken.Base64Token}");
                webSocket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                return webSocket;
            }

            private static async Task SendTextAsync(ClientWebSocket webSocket, string message, CancellationToken cancellationToken)
            {
                byte[] payload = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(
                        new ArraySegment<byte>(payload),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            private async Task ReceiveLoopAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
            {
                var buffer = new byte[ReceiveBufferSize];

                while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    using var messageStream = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                            .ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        messageStream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                        continue;

                    string message = Encoding.UTF8.GetString(messageStream.ToArray());
                    if (TryParseJsonApiEvent(message, out var apiEvent))
                        _eventReceived?.Invoke(apiEvent);
                }
            }

            private static bool TryParseJsonApiEvent(string message, out LeagueClientEvent apiEvent)
            {
                apiEvent = default;

                try
                {
                    using var document = JsonDocument.Parse(message);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
                        return false;

                    int messageType = root[0].ValueKind == JsonValueKind.Number && root[0].TryGetInt32(out int value)
                        ? value
                        : 0;
                    if (messageType != 8)
                        return false;

                    string eventName = root[1].GetString() ?? string.Empty;
                    if (!string.Equals(eventName, "OnJsonApiEvent", StringComparison.Ordinal))
                        return false;

                    var payload = root[2];
                    if (payload.ValueKind != JsonValueKind.Object)
                        return false;

                    string uri = payload.TryGetProperty("uri", out var uriProperty)
                        ? uriProperty.GetString() ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(uri))
                        return false;

                    string eventType = payload.TryGetProperty("eventType", out var eventTypeProperty)
                        ? eventTypeProperty.GetString() ?? string.Empty
                        : string.Empty;
                    string dataJson = payload.TryGetProperty("data", out var dataProperty)
                        ? dataProperty.GetRawText()
                        : string.Empty;

                    apiEvent = new LeagueClientEvent(uri, eventType, dataJson);
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _webSocket?.Dispose();
                _disposed = true;
            }
        }

        public readonly record struct LeagueClientEvent(string Uri, string EventType, string DataJson);
    }
}
