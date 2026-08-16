using System.Net.Http.Headers;
using LcuClient;

namespace JoinGameAfk.Tools.ClassicChampSelectRecorder;

internal sealed class LeagueClientProbe : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ChampSelectLogWriter _logWriter;

    public LeagueClientProbe(AuthModel auth, ChampSelectLogWriter logWriter)
    {
        _logWriter = logWriter;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{auth.Port}/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth.Base64Token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<bool> CaptureAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logWriter.WriteHttpError(uri, (int)response.StatusCode);
                return false;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logWriter.WriteHttpResponse(uri, json);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logWriter.WriteNote($"Snapshot failed for {uri}: {ex.GetType().Name}");
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
