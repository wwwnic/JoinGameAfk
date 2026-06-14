using System.Globalization;
using System.Text.Json;
using JoinGameAfk.Model;

namespace JoinGameAfk.Plugin.Services
{
    public sealed class DataDragonChampionCatalogService : IChampionCatalogRemoteService
    {
        private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
        private const string RealmUrlFormat = "https://ddragon.leagueoflegends.com/realms/{0}.json";
        private const string ChampionUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/champion.json";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Func<string?> _preferredLocaleProvider;
        private readonly Func<string?> _platformIdProvider;

        public DataDragonChampionCatalogService(
            Func<string?>? preferredLocaleProvider = null,
            Func<string?>? platformIdProvider = null)
        {
            _preferredLocaleProvider = preferredLocaleProvider ?? (() => RegionLocale.DefaultLocale);
            _platformIdProvider = platformIdProvider ?? (() => RegionLocale.DefaultPlatformId);
        }

        public async Task<ChampionCatalogRemoteData> FetchLatestChampionCatalogAsync(CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient
            {
                Timeout = RequestTimeout
            };

            string dataDragonVersion = await FetchLatestDataDragonVersionAsync(
                    httpClient,
                    GetPlatformId(),
                    cancellationToken)
                .ConfigureAwait(false);
            var champions = await FetchChampionsAsync(httpClient, dataDragonVersion, GetPreferredLocale(), cancellationToken).ConfigureAwait(false);

            return new ChampionCatalogRemoteData(dataDragonVersion, champions);
        }

        public async Task<string> FetchLatestDataDragonVersionAsync(CancellationToken cancellationToken = default)
        {
            using var httpClient = new HttpClient
            {
                Timeout = RequestTimeout
            };

            return await FetchLatestDataDragonVersionAsync(httpClient, GetPlatformId(), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<ChampionCatalogRemoteData> FetchChampionCatalogAsync(
            string dataDragonVersion,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(dataDragonVersion))
                throw new ArgumentException("Data Dragon version is required.", nameof(dataDragonVersion));

            dataDragonVersion = dataDragonVersion.Trim();

            using var httpClient = new HttpClient
            {
                Timeout = RequestTimeout
            };

            var champions = await FetchChampionsAsync(httpClient, dataDragonVersion, GetPreferredLocale(), cancellationToken).ConfigureAwait(false);
            return new ChampionCatalogRemoteData(dataDragonVersion, champions);
        }

        private static async Task<string> FetchLatestDataDragonVersionAsync(
            HttpClient httpClient,
            string platformId,
            CancellationToken cancellationToken)
        {
            if (RegionLocale.TryGetDataDragonRealm(platformId, out string realm))
            {
                string? regionalVersion = await TryFetchRealmVersionAsync(httpClient, realm, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(regionalVersion))
                    return regionalVersion.Trim();
            }

            return await FetchLatestGlobalDataDragonVersionAsync(httpClient, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string?> TryFetchRealmVersionAsync(
            HttpClient httpClient,
            string realm,
            CancellationToken cancellationToken)
        {
            string realmUrl = string.Format(CultureInfo.InvariantCulture, RealmUrlFormat, realm);
            using var response = await httpClient.GetAsync(realmUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var realmData = await JsonSerializer.DeserializeAsync<DataDragonRealm>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return realmData?.V;
        }

        private static async Task<string> FetchLatestGlobalDataDragonVersionAsync(
            HttpClient httpClient,
            CancellationToken cancellationToken)
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
            string locale,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(locale))
                locale = RegionLocale.DefaultLocale;

            DataDragonChampionCatalog? catalog = await TryFetchChampionCatalogAsync(
                    httpClient,
                    dataDragonVersion,
                    locale,
                    cancellationToken)
                .ConfigureAwait(false);
            string fetchedLocale = locale;

            if (catalog is null
                && !string.Equals(locale, RegionLocale.DefaultLocale, StringComparison.OrdinalIgnoreCase))
            {
                catalog = await TryFetchChampionCatalogAsync(
                        httpClient,
                        dataDragonVersion,
                        RegionLocale.DefaultLocale,
                        cancellationToken)
                    .ConfigureAwait(false);
                fetchedLocale = RegionLocale.DefaultLocale;
            }

            if (catalog is null)
            {
                throw new HttpRequestException(
                    $"Riot Data Dragon champion data is unavailable for {locale} in version {dataDragonVersion}.");
            }

            bool hasEnglishNames = string.Equals(
                fetchedLocale,
                RegionLocale.DefaultLocale,
                StringComparison.OrdinalIgnoreCase);
            return CreateRemoteChampions(catalog, hasEnglishNames);
        }

        private static async Task<DataDragonChampionCatalog?> TryFetchChampionCatalogAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string locale,
            CancellationToken cancellationToken)
        {
            string championUrl = string.Format(
                CultureInfo.InvariantCulture,
                ChampionUrlFormat,
                dataDragonVersion,
                locale);

            using var response = await httpClient.GetAsync(championUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await DeserializeChampionCatalogAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<DataDragonChampionCatalog> DeserializeChampionCatalogAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            var dataDragonCatalog = await JsonSerializer.DeserializeAsync<DataDragonChampionCatalog>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (dataDragonCatalog?.Data is null || dataDragonCatalog.Data.Count == 0)
                throw new InvalidOperationException("Riot Data Dragon returned no champion data.");

            return dataDragonCatalog;
        }

        private static IReadOnlyList<ChampionCatalogRemoteChampion> CreateRemoteChampions(
            DataDragonChampionCatalog catalog,
            bool hasEnglishNames)
        {
            return catalog.Data.Values
                .Select(champion => CreateRemoteChampion(champion, hasEnglishNames))
                .Where(champion => champion is not null)
                .Select(champion => champion!)
                .ToList();
        }

        private string GetPreferredLocale()
        {
            return RegionLocale.NormalizeLocale(_preferredLocaleProvider());
        }

        private string GetPlatformId()
        {
            return RegionLocale.NormalizePlatformId(_platformIdProvider());
        }

        private static ChampionCatalogRemoteChampion? CreateRemoteChampion(
            DataDragonChampion champion,
            bool hasEnglishName)
        {
            if (string.IsNullOrWhiteSpace(champion.Key)
                || !int.TryParse(champion.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int championKey)
                || championKey <= 0
                || string.IsNullOrWhiteSpace(champion.Name))
            {
                return null;
            }

            string name = champion.Name.Trim();
            string? id = string.IsNullOrWhiteSpace(champion.Id)
                ? null
                : champion.Id.Trim();

            return new ChampionCatalogRemoteChampion(
                championKey,
                name,
                EnglishName: hasEnglishName ? name : null,
                Id: id);
        }

        private sealed class DataDragonChampionCatalog
        {
            public Dictionary<string, DataDragonChampion> Data { get; set; } = [];
        }

        private sealed class DataDragonRealm
        {
            public string? V { get; set; }
        }

        private sealed class DataDragonChampion
        {
            public string? Id { get; set; }

            public string? Key { get; set; }

            public string? Name { get; set; }
        }
    }
}
