using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LcuClient
{
    public partial class Lcu
    {
        public class LeagueClientHttp
        {
            private readonly AuthModel fAuthToken;
            private readonly Uri _baseAddress;

            public LeagueClientHttp(AuthModel aAuthToken)
            {
                fAuthToken = aAuthToken;
                _baseAddress = new Uri($"https://127.0.0.1:{aAuthToken.Port}/");
            }

            public async Task<string> GetSessionAsync()
            {
                string endpoint = "/lol-gameflow/v1/session";
                return await GetAsync(endpoint).ConfigureAwait(false);
            }

            public bool HasAuthToken()
            {
                return fAuthToken != null;
            }

            public string GetSession()
            {
                return GetSessionAsync().GetAwaiter().GetResult();
            }

            public void AcceptMatch()
            {
                AcceptMatchAsync().GetAwaiter().GetResult();
            }

            public async Task<string> GetChampSelectSessionAsync()
            {
                string endpoint = "/lol-champ-select/v1/session";
                return await GetAsync(endpoint).ConfigureAwait(false);
            }

            public string GetChampSelectSession()
            {
                return GetChampSelectSessionAsync().GetAwaiter().GetResult();
            }

            public async Task HoverChampionAsync(int actionId, int championId)
            {
                string endpoint = $"/lol-champ-select/v1/session/actions/{actionId}";
                await PatchAsync(endpoint, new { championId }).ConfigureAwait(false);
            }

            public void HoverChampion(int actionId, int championId)
            {
                HoverChampionAsync(actionId, championId).GetAwaiter().GetResult();
            }

            public async Task CompleteActionAsync(int actionId, int championId)
            {
                string endpoint = $"/lol-champ-select/v1/session/actions/{actionId}";
                await PatchAsync(endpoint, new { championId, completed = true }).ConfigureAwait(false);
            }

            public void CompleteAction(int actionId, int championId)
            {
                CompleteActionAsync(actionId, championId).GetAwaiter().GetResult();
            }

            // Generic GET
            private async Task<string> GetAsync(string endpoint)
            {
                using var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.Zero
                };
                request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));
                request.Headers.ConnectionClose = true;

                HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            // Generic POST
            private async Task<string> PostAsync(string endpoint, object? body = null)
            {
                StringContent? content = null;
                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body);
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using var client = CreateClient();
                var response = await client.PostAsync(endpoint, content).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            // Generic PATCH
            private async Task<string> PatchAsync(string endpoint, object? body = null)
            {
                StringContent? content = null;
                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body);
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                using var client = CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Patch, endpoint) { Content = content };
                var response = await client.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            // Accept current match
            public async Task AcceptMatchAsync()
            {
                string endpoint = "/lol-matchmaking/v1/ready-check/accept";
                await PostAsync(endpoint).ConfigureAwait(false);
            }

            private HttpClient CreateClient()
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                var client = new HttpClient(handler)
                {
                    BaseAddress = _baseAddress
                };

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", fAuthToken.Base64Token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                return client;
            }
        }
    }
}
