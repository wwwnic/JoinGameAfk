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
        public static ChampionDataSourceMode Resolve(ChampionDataSourceMode configuredMode)
        {
            return configuredMode == ChampionDataSourceMode.DataDragon
                ? ChampionDataSourceMode.DataDragon
                : ChampionDataSourceMode.LeagueClient;
        }

        public static readonly TimeSpan LeagueClientRefreshInterval = TimeSpan.FromHours(12);

        public static bool IsLeagueClientRefreshDue(
            ChampionCatalogSyncInfo syncInfo,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(syncInfo);
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
