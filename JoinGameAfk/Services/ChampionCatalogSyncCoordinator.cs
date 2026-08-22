using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;

namespace JoinGameAfk.Services
{
    public sealed record LeagueClientChampionCatalogAutoSyncResult(
        bool Refreshed,
        ChampionCatalogRefreshResult? RefreshResult,
        DateTime? NextRefreshAtUtc);

    public sealed class ChampionCatalogSyncCoordinator
    {
        private readonly ChampionDataSettings _settings;
        private readonly LeagueClientConnectionContext _leagueClientConnection;
        private readonly Action<string>? _log;
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        public ChampionCatalogSyncCoordinator(
            ChampionDataSettings settings,
            LeagueClientConnectionContext leagueClientConnection,
            Action<string>? log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _leagueClientConnection = leagueClientConnection
                ?? throw new ArgumentNullException(nameof(leagueClientConnection));
            _log = log;
        }

        public async Task<ChampionCatalogRefreshResult> RefreshAsync(
            ChampionDataSourceMode source,
            CancellationToken cancellationToken = default)
        {
            await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RefreshCoreAsync(source, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _syncGate.Release();
            }
        }

        public async Task<LeagueClientChampionCatalogAutoSyncResult?>
            RefreshLeagueClientIfDueAsync(CancellationToken cancellationToken = default)
        {
            if (!_leagueClientConnection.IsConnected)
            {
                return null;
            }

            await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ChampionCatalogSyncInfo syncInfo = ChampionCatalog.GetLocalSyncInfo();
                string? requiredLocale = _leagueClientConnection.TryGetRegionLocale(
                    out LeagueClientRegionLocaleInfo? regionLocale)
                    ? regionLocale?.Locale
                    : null;
                if (!ChampionDataSourcePolicy.IsLeagueClientRefreshDue(
                        syncInfo,
                        DateTime.UtcNow,
                        requiredLocale))
                {
                    return new LeagueClientChampionCatalogAutoSyncResult(
                        false,
                        null,
                        syncInfo.LastSyncedAtUtc?.ToUniversalTime()
                            + ChampionDataSourcePolicy.LeagueClientRefreshInterval);
                }

                ChampionCatalogRefreshResult result = await RefreshCoreAsync(
                        ChampionDataSourceMode.LeagueClient,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new LeagueClientChampionCatalogAutoSyncResult(true, result, null);
            }
            finally
            {
                _syncGate.Release();
            }
        }

        private async Task<ChampionCatalogRefreshResult> RefreshCoreAsync(
            ChampionDataSourceMode source,
            CancellationToken cancellationToken)
        {
            ChampionCatalogRemoteData remoteCatalog;
            if (source == ChampionDataSourceMode.DataDragon)
            {
                var remoteService = new DataDragonChampionCatalogService(
                    () => _settings.Locale,
                    () => _settings.PlatformId);
                remoteCatalog = await remoteService
                    .FetchLatestChampionCatalogAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                using var http = _leagueClientConnection.CreateHttpClient(_log);
                // Read this for every forced LCU refresh so a client-language change
                // also becomes the authoritative Data Dragon fallback language.
                LeagueClientRegionLocaleInfo regionLocale = await LeagueClientRegionLocaleService
                    .FetchAsync(http, cancellationToken)
                    .ConfigureAwait(false);
                _leagueClientConnection.UpdateRegionLocale(regionLocale);
                var remoteService = new LeagueClientChampionCatalogService(http, regionLocale);
                remoteCatalog = await remoteService
                    .FetchLatestChampionCatalogAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return ChampionCatalog.RefreshFromDataDragon(remoteCatalog);
        }
    }
}
