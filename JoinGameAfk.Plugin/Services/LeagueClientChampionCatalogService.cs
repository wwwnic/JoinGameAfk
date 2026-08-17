using System.Text.Json;
using JoinGameAfk.Model;
using LcuClient;

namespace JoinGameAfk.Plugin.Services
{
    public sealed class LeagueClientChampionCatalogService : IChampionCatalogRemoteService
    {
        public const string LocalCatalogVersion = ChampionCatalogSourceIds.LeagueClient;
        private const int FirstModeVariantChampionId = 60000;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly Lcu.LeagueClientHttp _http;
        private readonly LeagueClientRegionLocaleInfo? _regionLocale;

        public LeagueClientChampionCatalogService(
            Lcu.LeagueClientHttp http,
            LeagueClientRegionLocaleInfo? regionLocale = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _regionLocale = regionLocale;
        }

        public async Task<ChampionCatalogRemoteData> FetchLatestChampionCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            string summaryJson = await _http.GetChampionSummaryAsync(cancellationToken).ConfigureAwait(false);
            LeagueClientRegionLocaleInfo regionLocale = _regionLocale
                ?? await LeagueClientRegionLocaleService.FetchAsync(_http, cancellationToken).ConfigureAwait(false);

            var summaries = JsonSerializer.Deserialize<List<LeagueClientChampionSummary>>(
                    summaryJson,
                    SerializerOptions)
                ?? [];
            string locale = regionLocale.Locale;
            bool isEnglish = locale.StartsWith("en_", StringComparison.OrdinalIgnoreCase);

            var champions = summaries
                // Mode-specific duplicates such as League Classic/Jade use 600xx IDs.
                // The base champion catalog must keep the canonical IDs used by role plans.
                .Where(champion => champion.Id > 0
                    && champion.Id < FirstModeVariantChampionId
                    && !string.IsNullOrWhiteSpace(champion.Name))
                .Select(champion => new ChampionCatalogRemoteChampion(
                    champion.Id,
                    champion.Name!.Trim(),
                    isEnglish ? champion.Name.Trim() : null,
                    string.IsNullOrWhiteSpace(champion.Alias) ? null : champion.Alias.Trim()))
                .OrderBy(champion => champion.Key)
                .ToList();

            if (champions.Count == 0)
                throw new InvalidOperationException("League Client returned no champions from its local game-data catalog.");

            return new ChampionCatalogRemoteData(LocalCatalogVersion, locale, champions);
        }

        private sealed class LeagueClientChampionSummary
        {
            public int Id { get; set; }

            public string? Name { get; set; }

            public string? Alias { get; set; }
        }
    }
}
