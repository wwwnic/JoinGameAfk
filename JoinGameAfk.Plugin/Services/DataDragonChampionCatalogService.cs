using System.Globalization;
using System.Text.Json;
using JoinGameAfk.Model;

namespace JoinGameAfk.Plugin.Services
{
    public sealed class DataDragonChampionCatalogService : IChampionCatalogRemoteService
    {
        private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
        private const string ChampionUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/en_US/champion.json";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<ChampionCatalogRemoteData> FetchLatestChampionCatalogAsync(CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient
            {
                Timeout = RequestTimeout
            };

            string dataDragonVersion = await FetchLatestVersionAsync(httpClient, cancellationToken).ConfigureAwait(false);
            var champions = await FetchChampionsAsync(httpClient, dataDragonVersion, cancellationToken).ConfigureAwait(false);

            return new ChampionCatalogRemoteData(dataDragonVersion, champions);
        }

        private static async Task<string> FetchLatestVersionAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            using var response = await httpClient.GetAsync(VersionsUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var versions = await JsonSerializer.DeserializeAsync<List<string>>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string? latestVersion = versions?.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
            if (latestVersion is null)
                throw new InvalidOperationException("Riot Data Dragon returned no versions.");

            return latestVersion.Trim();
        }

        private static async Task<IReadOnlyList<ChampionCatalogRemoteChampion>> FetchChampionsAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            CancellationToken cancellationToken)
        {
            string championUrl = string.Format(CultureInfo.InvariantCulture, ChampionUrlFormat, dataDragonVersion);

            using var response = await httpClient.GetAsync(championUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var dataDragonCatalog = await JsonSerializer.DeserializeAsync<DataDragonChampionCatalog>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (dataDragonCatalog?.Data is null || dataDragonCatalog.Data.Count == 0)
                throw new InvalidOperationException("Riot Data Dragon returned no champion data.");

            return dataDragonCatalog.Data.Values
                .Select(CreateRemoteChampion)
                .Where(champion => champion is not null)
                .Select(champion => champion!)
                .ToList();
        }

        private static ChampionCatalogRemoteChampion? CreateRemoteChampion(DataDragonChampion champion)
        {
            if (string.IsNullOrWhiteSpace(champion.Key)
                || !int.TryParse(champion.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int championId)
                || championId <= 0
                || string.IsNullOrWhiteSpace(champion.Name))
            {
                return null;
            }

            return new ChampionCatalogRemoteChampion(championId, champion.Name.Trim());
        }

        private sealed class DataDragonChampionCatalog
        {
            public Dictionary<string, DataDragonChampion> Data { get; set; } = [];
        }

        private sealed class DataDragonChampion
        {
            public string? Key { get; set; }

            public string? Name { get; set; }
        }
    }
}
