namespace JoinGameAfk.Model
{
    public enum ChampionDataSourceMode
    {
        LeagueClient,
        DataDragon
    }

    public static class ChampionCatalogSourceIds
    {
        public const string LeagueClient = "LeagueClient";
    }

    public static class ChampionDataSourcePolicy
    {
        public static ChampionDataSourceMode Resolve(bool isLeagueClientConnected)
        {
            return isLeagueClientConnected
                ? ChampionDataSourceMode.LeagueClient
                : ChampionDataSourceMode.DataDragon;
        }

        public static readonly TimeSpan LeagueClientRefreshInterval = TimeSpan.FromHours(12);

        public static bool IsLeagueClientRefreshDue(
            ChampionCatalogSyncInfo syncInfo,
            DateTime utcNow,
            string? requiredLocale = null)
        {
            ArgumentNullException.ThrowIfNull(syncInfo);
            if (!string.IsNullOrWhiteSpace(requiredLocale)
                && (string.IsNullOrWhiteSpace(syncInfo.Locale)
                    || !string.Equals(
                        RegionLocale.NormalizeLocale(syncInfo.Locale),
                        RegionLocale.NormalizeLocale(requiredLocale),
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            utcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
            if (!string.Equals(
                    syncInfo.DataDragonVersion,
                    ChampionCatalogSourceIds.LeagueClient,
                    StringComparison.OrdinalIgnoreCase)
                || syncInfo.LastSyncedAtUtc is null)
            {
                return true;
            }

            DateTime lastSyncUtc = syncInfo.LastSyncedAtUtc.Value.Kind == DateTimeKind.Utc
                ? syncInfo.LastSyncedAtUtc.Value
                : syncInfo.LastSyncedAtUtc.Value.ToUniversalTime();
            return utcNow - lastSyncUtc >= LeagueClientRefreshInterval;
        }
    }
}
