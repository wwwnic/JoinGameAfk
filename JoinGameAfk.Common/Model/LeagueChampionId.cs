namespace JoinGameAfk.Model
{
    public static class LeagueChampionId
    {
        public const int ClassicVariantOffset = 60000;
        private const int MaximumCanonicalChampionId = 9999;

        public static bool IsClassicVariant(int championId)
        {
            return championId > ClassicVariantOffset
                && championId <= ClassicVariantOffset + MaximumCanonicalChampionId;
        }

        public static int ToCanonical(int championId)
        {
            return IsClassicVariant(championId)
                ? championId - ClassicVariantOffset
                : championId;
        }

        public static int ToClassicVariant(int championId)
        {
            int canonicalChampionId = ToCanonical(championId);
            return canonicalChampionId is > 0 and <= MaximumCanonicalChampionId
                ? canonicalChampionId + ClassicVariantOffset
                : championId;
        }
    }
}
