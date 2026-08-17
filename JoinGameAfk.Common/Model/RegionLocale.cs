namespace JoinGameAfk.Model
{
    public static class RegionLocale
    {
        public const string DefaultPlatformId = "GLOBAL";
        public const string DefaultLocale = "en_US";

        private static readonly IReadOnlyDictionary<string, string> DataDragonRealmsByPlatformId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NA1"] = "na",
                ["BR1"] = "br",
                ["EUN1"] = "eune",
                ["EUW1"] = "euw",
                ["JP1"] = "jp",
                ["KR"] = "kr",
                ["LA1"] = "lan",
                ["LA2"] = "las",
                ["ME1"] = "me",
                ["OC1"] = "oce",
                ["PBE1"] = "pbe",
                ["PH2"] = "ph",
                ["RU"] = "ru",
                ["SG2"] = "sg",
                ["TH2"] = "th",
                ["TR1"] = "tr",
                ["TW2"] = "tw",
                ["VN2"] = "vn"
            };

        /// <summary>
        /// Korea is ignored because LCU usage is not allowed in Korea.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> LeagueClientPlatformsByRegion =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NA"] = "NA1",
                ["BR"] = "BR1",
                ["EUNE"] = "EUN1",
                ["EUW"] = "EUW1",
                ["JP"] = "JP1",
                ["LAN"] = "LA1",
                ["LAS"] = "LA2",
                ["ME"] = "ME1",
                ["OCE"] = "OC1",
                ["PBE"] = "PBE1",
                ["PH"] = "PH2",
                ["RU"] = "RU",
                ["SG"] = "SG2",
                ["TH"] = "TH2",
                ["TR"] = "TR1",
                ["TW"] = "TW2",
                ["VN"] = "VN2"
            };

        public static string NormalizePlatformId(string? platformId)
        {
            return TryNormalizePlatformId(platformId, out string normalized)
                ? normalized
                : DefaultPlatformId;
        }

        public static bool TryNormalizePlatformId(string? platformId, out string normalized)
        {
            normalized = DefaultPlatformId;
            if (string.IsNullOrWhiteSpace(platformId))
                return false;

            string candidate = platformId.Trim().ToUpperInvariant();
            if (candidate.Length > 32
                || !candidate.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
            {
                return false;
            }

            normalized = candidate;
            return true;
        }

        public static bool TryGetDataDragonRealm(string? platformId, out string realm)
        {
            realm = string.Empty;
            if (!TryNormalizePlatformId(platformId, out string normalizedPlatformId)
                || !DataDragonRealmsByPlatformId.TryGetValue(normalizedPlatformId, out string? mappedRealm))
            {
                return false;
            }

            realm = mappedRealm;
            return true;
        }

        public static bool TryNormalizeLeagueClientRegion(
            string? region,
            out string normalizedRegion,
            out string platformId)
        {
            normalizedRegion = string.Empty;
            platformId = DefaultPlatformId;
            if (!TryNormalizePlatformId(region, out string candidate))
                return false;

            normalizedRegion = candidate;
            platformId = LeagueClientPlatformsByRegion.TryGetValue(candidate, out string? mappedPlatformId)
                ? mappedPlatformId
                : candidate;
            return true;
        }

        public static string NormalizeLocale(string? locale)
        {
            return TryNormalizeLocale(locale, out string normalized)
                ? normalized
                : DefaultLocale;
        }

        public static bool TryNormalizeLocale(string? locale, out string normalized)
        {
            normalized = DefaultLocale;
            if (string.IsNullOrWhiteSpace(locale))
                return false;

            string candidate = locale.Trim().Replace('-', '_');
            string[] parts = candidate.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 2 or > 3
                || parts.Any(part => part.Length is < 2 or > 8 || !part.All(char.IsLetterOrDigit)))
            {
                return false;
            }

            parts[0] = parts[0].ToLowerInvariant();
            parts[^1] = parts[^1].ToUpperInvariant();
            if (parts.Length == 3)
            {
                string script = parts[1].ToLowerInvariant();
                parts[1] = char.ToUpperInvariant(script[0]) + script[1..];
            }

            normalized = string.Join('_', parts);
            return true;
        }
    }
}
