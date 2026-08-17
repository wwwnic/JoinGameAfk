using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LcuClient
{
    public partial class Lcu
    {
        public sealed class LeagueClientHttp : IDisposable
        {
            private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

            private readonly AuthModel _authToken;
            private readonly HttpClient _httpClient;
            private readonly Action<string>? _log;
            private bool _disposed;

            public LeagueClientHttp(AuthModel aAuthToken, Action<string>? log = null)
            {
                _authToken = aAuthToken ?? throw new ArgumentNullException(nameof(aAuthToken));
                _log = log;

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                _httpClient = new HttpClient(handler)
                {
                    BaseAddress = new Uri($"https://127.0.0.1:{aAuthToken.Port}/"),
                    Timeout = RequestTimeout
                };

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _authToken.Base64Token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            public async Task<string> GetGameflowSessionAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-gameflow/v1/session";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public async Task<string> GetLobbyAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-lobby/v2/lobby";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public async Task<string> GetGameflowPhaseAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-gameflow/v1/gameflow-phase";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public bool HasAuthToken()
            {
                return _authToken != null;
            }

            public async Task<string> GetChampSelectSessionAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-champ-select/v1/session";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public async Task<string> GetOwnedChampionsMinimalAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-champions/v1/owned-champions-minimal";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public async Task<string> GetChampSelectAllGridChampionsAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-champ-select/v1/all-grid-champions";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public Task<string> GetChampionSummaryAsync(CancellationToken cancellationToken = default)
            {
                return GetAsync("/lol-game-data/assets/v1/champion-summary.json", cancellationToken);
            }

            public Task<string> GetChampionDetailsAsync(
                int championId,
                CancellationToken cancellationToken = default)
            {
                if (championId <= 0)
                    throw new ArgumentOutOfRangeException(nameof(championId));

                return GetAsync($"/lol-game-data/assets/v1/champions/{championId}.json", cancellationToken);
            }

            public Task<string> GetRegionLocaleAsync(CancellationToken cancellationToken = default)
            {
                return GetAsync("/riotclient/region-locale", cancellationToken);
            }

            public Task<byte[]> GetGameDataAssetAsync(
                string assetPath,
                CancellationToken cancellationToken = default)
            {
                if (string.IsNullOrWhiteSpace(assetPath)
                    || !assetPath.StartsWith("/lol-game-data/assets/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("A local League Client game-data asset path is required.", nameof(assetPath));
                }

                return GetBytesAsync(assetPath, cancellationToken);
            }

            public async Task<string> GetReadyCheckAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-matchmaking/v1/ready-check";
                return await GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            public async Task HoverChampionAsync(int actionId, int championId, CancellationToken cancellationToken = default)
            {
                string endpoint = $"/lol-champ-select/v1/session/actions/{actionId}";
                await PatchAsync(endpoint, new { championId }, cancellationToken).ConfigureAwait(false);
            }

            public async Task CompleteActionAsync(int actionId, int championId, CancellationToken cancellationToken = default)
            {
                string endpoint = $"/lol-champ-select/v1/session/actions/{actionId}";
                await PatchAsync(endpoint, new { championId, completed = true }, cancellationToken).ConfigureAwait(false);
            }

            private async Task<string> GetAsync(string endpoint, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                LogRequest(HttpMethod.Get, endpoint, body: null);
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.Zero
                };
                request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));

                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            private async Task<byte[]> GetBytesAsync(string endpoint, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                LogRequest(HttpMethod.Get, endpoint, body: null);
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                // The shared client defaults to application/json. Riot's local
                // asset handler rejects that media type for images with HTTP 400.
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                request.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.Zero
                };
                request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));

                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }

            private async Task<string> PostAsync(string endpoint, object? body, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                LogRequest(HttpMethod.Post, endpoint, body);
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = CreateJsonContent(body)
                };

                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            private async Task<string> PatchAsync(string endpoint, object? body, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                LogRequest(HttpMethod.Patch, endpoint, body);
                using var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
                {
                    Content = CreateJsonContent(body)
                };

                using HttpResponseMessage response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            public async Task<bool> AcceptMatchAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-matchmaking/v1/ready-check/accept";

                try
                {
                    await PostAsync(endpoint, body: null, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (HttpRequestException ex) when (IsExpectedReadyCheckAcceptConflict(ex.StatusCode))
                {
                    _log?.Invoke($"LCU request handled as already accepted or inactive: POST {endpoint} returned {(int)ex.StatusCode!.Value}.");
                    return false;
                }
            }

            private static bool IsExpectedReadyCheckAcceptConflict(HttpStatusCode? statusCode)
            {
                return statusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.Conflict;
            }

            private void LogRequest(HttpMethod method, string endpoint, object? body)
            {
                string bodyText = body is null
                    ? string.Empty
                    : $" body={JsonSerializer.Serialize(body)}";

                _log?.Invoke($"LCU request: {method.Method} {endpoint}{bodyText}");
            }

            private static StringContent? CreateJsonContent(object? body)
            {
                if (body == null)
                    return null;

                var json = JsonSerializer.Serialize(body);
                return new StringContent(json, Encoding.UTF8, "application/json");
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
