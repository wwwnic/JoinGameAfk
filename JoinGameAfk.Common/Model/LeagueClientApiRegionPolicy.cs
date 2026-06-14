namespace JoinGameAfk.Model
{
    public static class LeagueClientApiRegionPolicy
    {
        public static bool IsRestricted(string? platformId)
        {
            if (!RegionLocale.TryNormalizePlatformId(platformId, out string normalizedPlatformId))
                return false;

            // Riot explicitly disallows League Client API applications in Korea:
            // https://www.riotgames.com/en/DevRel/changes-to-the-lcu-api-policy
            return string.Equals(normalizedPlatformId, "KR", StringComparison.Ordinal);
        }
    }
}
