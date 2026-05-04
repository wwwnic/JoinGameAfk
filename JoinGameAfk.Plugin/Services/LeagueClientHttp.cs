using System.Net.Http;
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
            private bool _disposed;

            public LeagueClientHttp(AuthModel aAuthToken)
            {
                _authToken = aAuthToken ?? throw new ArgumentNullException(nameof(aAuthToken));

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

            public async Task<string> GetSessionAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-gameflow/v1/session";
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

            private async Task<string> PostAsync(string endpoint, object? body, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
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

            public async Task AcceptMatchAsync(CancellationToken cancellationToken = default)
            {
                string endpoint = "/lol-matchmaking/v1/ready-check/accept";
                await PostAsync(endpoint, body: null, cancellationToken).ConfigureAwait(false);
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
