namespace JoinGameAfk.Model
{
    public static class LeagueClassicChampionRoster
    {
        // Offline migration snapshot from Riot Data Dragon 16.16.1. A successful
        // League Client or Data Dragon catalog refresh supplies authoritative flags.
        private static readonly HashSet<int> BundledChampionIds =
        [
            1, 2, 4, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
            23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 40,
            41, 42, 44, 45, 53, 54, 55, 59, 62, 63, 64, 67, 72, 74, 75, 76, 79,
            80, 81, 84, 85, 86, 89, 90, 96, 98, 99, 103, 117
        ];

        public static IReadOnlySet<int> Bundled => BundledChampionIds;

        public static bool Contains(int championId)
        {
            return BundledChampionIds.Contains(LeagueChampionId.ToCanonical(championId));
        }
    }
}
