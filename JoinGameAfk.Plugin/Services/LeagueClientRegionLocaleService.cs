using System.Text.Json;
using JoinGameAfk.Model;
using LcuClient;

namespace JoinGameAfk.Plugin.Services
{
    public sealed record LeagueClientRegionLocaleInfo(
        string PlatformId,
        string Region,
        string Locale,
        string? WebRegion,
        string? WebLanguage);

    public static class LeagueClientRegionLocaleService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<LeagueClientRegionLocaleInfo> FetchAsync(
            Lcu.LeagueClientHttp http,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(http);
            string json = await http.GetRegionLocaleAsync(cancellationToken).ConfigureAwait(false);
            return Parse(json);
        }

        public static LeagueClientRegionLocaleInfo Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("League Client returned an empty region and language response.");

            RegionLocalePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<RegionLocalePayload>(json, SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    "League Client returned invalid region and language data.",
                    ex);
            }

            if (!RegionLocale.TryNormalizeLeagueClientRegion(
                    payload?.Region,
                    out string normalizedRegion,
                    out string platformId))
            {
                throw new InvalidOperationException("League Client did not report a valid region.");
            }

            if (!RegionLocale.TryNormalizeLocale(payload?.Locale, out string locale))
                throw new InvalidOperationException("League Client did not report a valid language.");

            return new LeagueClientRegionLocaleInfo(
                platformId,
                normalizedRegion,
                locale,
                NormalizeOptional(payload?.WebRegion),
                NormalizeOptional(payload?.WebLanguage));
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class RegionLocalePayload
        {
            public string? Locale { get; set; }

            public string? Region { get; set; }

            public string? WebLanguage { get; set; }

            public string? WebRegion { get; set; }
        }
    }
}
