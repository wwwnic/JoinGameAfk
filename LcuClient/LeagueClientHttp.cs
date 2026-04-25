using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LcuClient.Model;

namespace LcuClient
{
    public partial class Lcu
    {
        public class LeagueClientHttp
        {
            private readonly HttpClient _httpClient;
            private AuthModel fAuthToken;

            public LeagueClientHttp(AuthModel aAuthToken)
            {
                var handler = new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                _httpClient = new HttpClient(handler); fAuthToken = aAuthToken;

                _httpClient.BaseAddress = new Uri($"https://127.0.0.1:{aAuthToken.Port}/");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", aAuthToken.Base64Token);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            public async Task<string> GetSessionAsync()
            {
                string endpoint = "/lol-gameflow/v1/session";
                return await GetAsync(endpoint);
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

            // Generic GET
            private async Task<string> GetAsync(string endpoint)
            {
                HttpResponseMessage response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }

            // Generic POST
            private async Task<string> PostAsync(string endpoint, object body = null)
            {
                StringContent content = null;
                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body);
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var response = await _httpClient.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }

            // Accept current match
            public async Task AcceptMatchAsync()
            {
                bool InTesting = true;
                string type = InTesting ? "decline" : "accept";
                // Endpoint for accepting a match
                string endpoint = "/lol-matchmaking/v1/ready-check/" + type;
                await PostAsync(endpoint);
            }
        }
    }
}