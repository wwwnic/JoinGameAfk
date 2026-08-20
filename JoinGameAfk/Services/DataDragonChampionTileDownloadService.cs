using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using JoinGameAfk.Model;
using JoinGameAfk.Enums;

namespace JoinGameAfk.Services
{
    public sealed record ChampionTileDownloadProgress(
        string DataDragonVersion,
        string ChampionName,
        int CheckedTileCount,
        int DownloadedTileCount,
        int UnchangedTileCount,
        int FailedTileCount,
        int? TotalTileCount,
        string Message);

    public sealed record ChampionTileDownloadResult(
        string DataDragonVersion,
        string ChampionName,
        int CheckedTileCount,
        int DownloadedTileCount,
        int UnchangedTileCount,
        int FailedTileCount,
        string CacheDirectory,
        DateTime LastDownloadedAtUtc);

    internal static class DataDragonChampionTileDownloadService
    {
        private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
        private const string ChampionCatalogUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/champion.json";
        private const string ClassicChampionCatalogUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/mode/classic/champion.json";
        private const string ChampionDetailUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/champion/{2}.json";
        private const string ClassicChampionDetailUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/mode/classic/champion/{2}.json";
        private const string ChampionTileUrlFormat = "https://ddragon.leagueoflegends.com/cdn/img/champion/tiles/{0}_{1}.jpg";
        private const string ClassicChampionTileUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/img/mode/classic/champion/{1}";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<ChampionTileDownloadResult> DownloadChampionTilesAsync(
            ChampionInfo champion,
            LeagueGameMode gameMode,
            string? preferredDataDragonVersion,
            string tileDirectoryPath,
            IProgress<ChampionTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true,
            string? preferredLocale = null)
        {
            ArgumentNullException.ThrowIfNull(champion);

            if (champion.Key <= 0 || string.IsNullOrWhiteSpace(champion.Name))
                throw new ArgumentException("Champion key and name are required.", nameof(champion));

            Directory.CreateDirectory(tileDirectoryPath);

            using var httpClient = new HttpClient
            {
                Timeout = RequestTimeout
            };

            string dataDragonVersion = await ResolveDataDragonVersionAsync(
                    httpClient,
                    preferredDataDragonVersion,
                    champion.Name,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            string locale = RegionLocale.NormalizeLocale(preferredLocale);

            Report(
                progress,
                dataDragonVersion,
                champion.Name,
                0,
                0,
                0,
                0,
                null,
                $"Checking Riot Data Dragon pictures for {champion.Name}...");

            var catalogChampion = await FetchChampionCatalogEntryAsync(
                    httpClient,
                    dataDragonVersion,
                    locale,
                    champion,
                    cancellationToken)
                .ConfigureAwait(false);

            DataDragonClassicChampion? classicChampion = null;
            string tileChampionId = catalogChampion.Id!;
            DataDragonChampionDetail championDetail;
            if (gameMode == LeagueGameMode.Classic)
            {
                classicChampion = await FetchClassicChampionAsync(
                        httpClient,
                        dataDragonVersion,
                        locale,
                        champion,
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"Riot Data Dragon does not list {champion.Name} in League Classic.");
                if (string.IsNullOrWhiteSpace(classicChampion.Id))
                    throw new InvalidOperationException($"Riot Data Dragon returned no League Classic id for {champion.Name}.");

                tileChampionId = classicChampion.Id.Trim();
                championDetail = await FetchChampionDetailAsync(
                        httpClient,
                        dataDragonVersion,
                        locale,
                        tileChampionId,
                        ClassicChampionDetailUrlFormat,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                championDetail = await FetchChampionDetailAsync(
                        httpClient,
                        dataDragonVersion,
                        locale,
                        tileChampionId,
                        ChampionDetailUrlFormat,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var skinNumbers = championDetail.Skins
                .Where(skin => skin.ParentSkin is null)
                .Select(skin => skin.Num)
                .Where(number => number >= 0)
                .Distinct()
                .OrderBy(number => number)
                .ToList();

            if (skinNumbers.Count == 0)
                throw new InvalidOperationException($"Riot Data Dragon returned no champion pictures for {champion.Name}.");

            int totalTileCount = skinNumbers.Count;

            int checkedTileCount = 0;
            int downloadedTileCount = 0;
            int unchangedTileCount = 0;
            int failedTileCount = 0;

            foreach (int skinNumber in skinNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                checkedTileCount++;
                string fileName = gameMode == LeagueGameMode.Classic
                    ? CreateClassicChampionTileFileName(catalogChampion.Id!, skinNumber)
                    : CreateChampionTileFileName(catalogChampion.Id!, skinNumber);
                Report(
                    progress,
                    dataDragonVersion,
                    champion.Name,
                    checkedTileCount,
                    downloadedTileCount,
                    unchangedTileCount,
                    failedTileCount,
                    totalTileCount,
                    $"Downloading {champion.Name} pictures {checkedTileCount}/{totalTileCount}...");

                try
                {
                    ChampionTileDownloadOutcome outcome;
                    if (gameMode == LeagueGameMode.Classic && skinNumber == 0)
                    {
                        outcome = await DownloadClassicChampionTileAsync(
                                httpClient,
                                dataDragonVersion,
                                classicChampion!.ImageFileName,
                                tileDirectoryPath,
                                fileName,
                                optimizeForLocalCache,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        outcome = await DownloadChampionTileAsync(
                                httpClient,
                                tileChampionId,
                                skinNumber,
                                tileDirectoryPath,
                                fileName,
                                optimizeForLocalCache,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (outcome == ChampionTileDownloadOutcome.Downloaded)
                        downloadedTileCount++;
                    else
                        unchangedTileCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedTileCount++;
                    Report(
                        progress,
                        dataDragonVersion,
                        champion.Name,
                        checkedTileCount,
                        downloadedTileCount,
                        unchangedTileCount,
                        failedTileCount,
                        totalTileCount,
                        $"Unable to download {fileName}: {FormatException(ex)}");
                }
            }

            Report(
                progress,
                dataDragonVersion,
                champion.Name,
                checkedTileCount,
                downloadedTileCount,
                unchangedTileCount,
                failedTileCount,
                totalTileCount,
                $"Finished downloading {champion.Name} pictures. Downloaded {downloadedTileCount}; unchanged {unchangedTileCount}; failed {failedTileCount}.");

            return new ChampionTileDownloadResult(
                dataDragonVersion,
                champion.Name,
                checkedTileCount,
                downloadedTileCount,
                unchangedTileCount,
                failedTileCount,
                tileDirectoryPath,
                DateTime.UtcNow);
        }

        public static async Task<ChampionDefaultTileDownloadResult> DownloadDefaultChampionTilesAsync(
            IReadOnlyList<ChampionInfo> champions,
            string? preferredDataDragonVersion,
            string tileDirectoryPath,
            IProgress<ChampionDefaultTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true,
            string? preferredLocale = null)
        {
            ArgumentNullException.ThrowIfNull(champions);
            Directory.CreateDirectory(tileDirectoryPath);

            using var httpClient = new HttpClient
            {
                Timeout = RequestTimeout
            };
            string dataDragonVersion = await ResolveDataDragonVersionAsync(
                    httpClient,
                    preferredDataDragonVersion,
                    "default champion pictures",
                    progress: null,
                    cancellationToken)
                .ConfigureAwait(false);
            string locale = RegionLocale.NormalizeLocale(preferredLocale);
            DataDragonChampionCatalog catalog = await FetchChampionCatalogAsync(
                    httpClient,
                    dataDragonVersion,
                    locale,
                    cancellationToken)
                .ConfigureAwait(false);
            var catalogByKey = catalog.Data.Values
                .Where(champion => !string.IsNullOrWhiteSpace(champion.Key))
                .GroupBy(champion => champion.Key!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            DataDragonClassicChampionCatalog classicCatalog = await FetchClassicChampionCatalogForDefaultsAsync(
                    httpClient,
                    dataDragonVersion,
                    locale,
                    cancellationToken)
                .ConfigureAwait(false);
            Dictionary<int, ChampionInfo> requestedChampionsByKey = champions
                .Where(champion => champion.Key > 0)
                .GroupBy(champion => champion.Key)
                .ToDictionary(group => group.Key, group => group.First());
            var classicDefaultTiles = new List<(ChampionInfo Champion, string ChampionId, string ImageFileName)>();
            foreach (DataDragonClassicChampion classicChampion in classicCatalog.Data.Values)
            {
                if (!int.TryParse(
                        classicChampion.Key,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int modeChampionId)
                    || !LeagueChampionId.IsClassicVariant(modeChampionId))
                {
                    continue;
                }

                int canonicalChampionId = LeagueChampionId.ToCanonical(modeChampionId);
                if (!requestedChampionsByKey.TryGetValue(canonicalChampionId, out ChampionInfo? champion)
                    || !catalogByKey.TryGetValue(
                        canonicalChampionId.ToString(CultureInfo.InvariantCulture),
                        out DataDragonChampion? catalogChampion)
                    || string.IsNullOrWhiteSpace(catalogChampion.Id))
                {
                    continue;
                }

                string imageFileName = classicChampion.Image?.Full?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageFileName)
                    || !string.Equals(imageFileName, Path.GetFileName(imageFileName), StringComparison.Ordinal)
                    || !imageFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Riot Data Dragon returned an unsafe League Classic image name for {champion.Name}.");
                }

                classicDefaultTiles.Add((champion, catalogChampion.Id.Trim(), imageFileName));
            }

            int downloadedCount = 0;
            int unchangedCount = 0;
            int failedCount = 0;
            int checkedCount = 0;
            int totalTileCount = champions.Count + classicDefaultTiles.Count;
            for (int index = 0; index < champions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChampionInfo champion = champions[index];
                checkedCount++;
                try
                {
                    string championKey = champion.Key.ToString(CultureInfo.InvariantCulture);
                    if (!catalogByKey.TryGetValue(championKey, out DataDragonChampion? catalogChampion)
                        || string.IsNullOrWhiteSpace(catalogChampion.Id))
                    {
                        throw new InvalidOperationException(
                            $"Riot Data Dragon {dataDragonVersion} does not list {champion.Name} ({champion.Key}).");
                    }

                    string championId = catalogChampion.Id.Trim();
                    string fileName = CreateChampionTileFileName(championId, skinNumber: 0);
                    ChampionTileDownloadOutcome outcome = await DownloadChampionTileAsync(
                            httpClient,
                            championId,
                            skinNumber: 0,
                            tileDirectoryPath,
                            fileName,
                            optimizeForLocalCache,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (outcome == ChampionTileDownloadOutcome.Downloaded)
                        downloadedCount++;
                    else
                        unchangedCount++;

                    progress?.Report(new ChampionDefaultTileDownloadProgress(
                        dataDragonVersion,
                        checkedCount,
                        downloadedCount,
                        unchangedCount,
                        failedCount,
                        totalTileCount,
                        $"Downloading LoL default pictures {checkedCount}/{totalTileCount}..."));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    progress?.Report(new ChampionDefaultTileDownloadProgress(
                        dataDragonVersion,
                        checkedCount,
                        downloadedCount,
                        unchangedCount,
                        failedCount,
                        totalTileCount,
                        $"Unable to download the LoL default picture for {champion.Name}: {FormatException(ex)}"));
                }
            }

            foreach (var classicTile in classicDefaultTiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;
                try
                {
                    ChampionTileDownloadOutcome outcome = await DownloadClassicChampionTileAsync(
                            httpClient,
                            dataDragonVersion,
                            classicTile.ImageFileName,
                            tileDirectoryPath,
                            CreateClassicChampionTileFileName(classicTile.ChampionId, skinNumber: 0),
                            optimizeForLocalCache,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (outcome == ChampionTileDownloadOutcome.Downloaded)
                        downloadedCount++;
                    else
                        unchangedCount++;

                    progress?.Report(new ChampionDefaultTileDownloadProgress(
                        dataDragonVersion,
                        checkedCount,
                        downloadedCount,
                        unchangedCount,
                        failedCount,
                        totalTileCount,
                        $"Downloading League Classic default pictures {checkedCount}/{totalTileCount}..."));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    progress?.Report(new ChampionDefaultTileDownloadProgress(
                        dataDragonVersion,
                        checkedCount,
                        downloadedCount,
                        unchangedCount,
                        failedCount,
                        totalTileCount,
                        $"Unable to download the League Classic default picture for {classicTile.Champion.Name}: {FormatException(ex)}"));
                }
            }

            return new ChampionDefaultTileDownloadResult(
                dataDragonVersion,
                totalTileCount,
                downloadedCount,
                unchangedCount,
                failedCount,
                tileDirectoryPath,
                DateTime.UtcNow);
        }

        private static async Task<DataDragonClassicChampionCatalog> FetchClassicChampionCatalogForDefaultsAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string locale,
            CancellationToken cancellationToken)
        {
            string[] locales =
            [
                RegionLocale.NormalizeLocale(locale),
                RegionLocale.DefaultLocale
            ];

            foreach (string catalogLocale in locales.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string catalogUrl = string.Format(
                    CultureInfo.InvariantCulture,
                    ClassicChampionCatalogUrlFormat,
                    dataDragonVersion,
                    catalogLocale);
                using var response = await httpClient.GetAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                var catalog = await JsonSerializer.DeserializeAsync<DataDragonClassicChampionCatalog>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (catalog?.Data.Count > 0)
                    return catalog;
            }

            throw new InvalidOperationException(
                $"Riot Data Dragon {dataDragonVersion} returned no League Classic champion catalog.");
        }

        private static async Task<string> ResolveDataDragonVersionAsync(
            HttpClient httpClient,
            string? preferredDataDragonVersion,
            string championName,
            IProgress<ChampionTileDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(preferredDataDragonVersion)
                && !string.Equals(preferredDataDragonVersion.Trim(), "bundled", StringComparison.OrdinalIgnoreCase))
            {
                return preferredDataDragonVersion.Trim();
            }

            Report(progress, string.Empty, championName, 0, 0, 0, 0, null, "Checking latest Riot Data Dragon version...");

            using var response = await httpClient.GetAsync(VersionsUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var versions = await JsonSerializer.DeserializeAsync<List<string>>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            string? latestVersion = versions?.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
            if (latestVersion is null)
                throw new InvalidOperationException("Riot Data Dragon returned no versions.");

            return latestVersion.Trim();
        }

        private static async Task<DataDragonChampion> FetchChampionCatalogEntryAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string locale,
            ChampionInfo champion,
            CancellationToken cancellationToken)
        {
            DataDragonChampionCatalog catalog = await FetchChampionCatalogAsync(
                    httpClient,
                    dataDragonVersion,
                    locale,
                    cancellationToken)
                .ConfigureAwait(false);
            return FindCatalogChampion(catalog, champion, dataDragonVersion);
        }

        private static async Task<DataDragonChampionCatalog> FetchChampionCatalogAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string locale,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(locale))
                locale = RegionLocale.DefaultLocale;

            string championCatalogUrl = string.Format(
                CultureInfo.InvariantCulture,
                ChampionCatalogUrlFormat,
                dataDragonVersion,
                locale);
            using var response = await httpClient.GetAsync(championCatalogUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                championCatalogUrl = string.Format(
                    CultureInfo.InvariantCulture,
                    ChampionCatalogUrlFormat,
                    dataDragonVersion,
                    RegionLocale.DefaultLocale);
                using var fallback = await httpClient.GetAsync(championCatalogUrl, cancellationToken).ConfigureAwait(false);
                fallback.EnsureSuccessStatusCode();
                await using var fallbackStream = await fallback.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await DeserializeChampionCatalogAsync(fallbackStream, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await DeserializeChampionCatalogAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<DataDragonClassicChampion?> FetchClassicChampionAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string locale,
            ChampionInfo champion,
            CancellationToken cancellationToken)
        {
            string[] locales =
            [
                RegionLocale.NormalizeLocale(locale),
                RegionLocale.DefaultLocale
            ];

            foreach (string catalogLocale in locales.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string catalogUrl = string.Format(
                    CultureInfo.InvariantCulture,
                    ClassicChampionCatalogUrlFormat,
                    dataDragonVersion,
                    catalogLocale);
                using var response = await httpClient.GetAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    continue;

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                var catalog = await JsonSerializer.DeserializeAsync<DataDragonClassicChampionCatalog>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);

                DataDragonClassicChampion? match = catalog?.Data.Values.FirstOrDefault(candidate =>
                    int.TryParse(candidate.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int modeChampionId)
                    && LeagueChampionId.IsClassicVariant(modeChampionId)
                    && LeagueChampionId.ToCanonical(modeChampionId) == champion.Key);
                if (match is null)
                    return null;

                string imageFileName = match.Image?.Full?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageFileName)
                    || !string.Equals(imageFileName, Path.GetFileName(imageFileName), StringComparison.Ordinal)
                    || !imageFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Riot Data Dragon returned an unsafe League Classic image name for {champion.Name}.");
                }

                return match with { ImageFileName = imageFileName };
            }

            return null;
        }

        private static async Task<DataDragonChampionDetail> FetchChampionDetailAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string locale,
            string dataDragonChampionId,
            string detailUrlFormat,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(locale))
                locale = "en_US";

            string championDetailUrl = string.Format(
                CultureInfo.InvariantCulture,
                detailUrlFormat,
                dataDragonVersion,
                locale,
                Uri.EscapeDataString(dataDragonChampionId));

            using var response = await httpClient.GetAsync(championDetailUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                championDetailUrl = string.Format(
                    CultureInfo.InvariantCulture,
                    detailUrlFormat,
                    dataDragonVersion,
                    "en_US",
                    Uri.EscapeDataString(dataDragonChampionId));
                using var fallback = await httpClient.GetAsync(championDetailUrl, cancellationToken).ConfigureAwait(false);
                fallback.EnsureSuccessStatusCode();
                await using var fbStream = await fallback.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await DeserializeChampionDetailAsync(fbStream, dataDragonVersion, dataDragonChampionId, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await DeserializeChampionDetailAsync(stream, dataDragonVersion, dataDragonChampionId, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<DataDragonChampionCatalog> DeserializeChampionCatalogAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            var catalog = await JsonSerializer.DeserializeAsync<DataDragonChampionCatalog>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (catalog is null || catalog.Data.Count == 0)
                throw new InvalidOperationException("Riot Data Dragon returned an empty champion catalog.");

            return catalog;
        }

        private static DataDragonChampion FindCatalogChampion(
            DataDragonChampionCatalog catalog,
            ChampionInfo champion,
            string dataDragonVersion)
        {
            string championKey = champion.Key.ToString(CultureInfo.InvariantCulture);
            var match = catalog.Data.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, championKey, StringComparison.Ordinal));

            if (match is null)
                throw new InvalidOperationException($"Riot Data Dragon {dataDragonVersion} does not list {champion.Name} ({champion.Key}).");

            if (string.IsNullOrWhiteSpace(match.Id))
                throw new InvalidOperationException($"Riot Data Dragon {dataDragonVersion} did not return a canonical id for {champion.Name}.");

            match.Id = match.Id.Trim();
            return match;
        }

        private static async Task<DataDragonChampionDetail> DeserializeChampionDetailAsync(Stream stream, string dataDragonVersion, string dataDragonChampionId, CancellationToken cancellationToken)
        {
            var detailCatalog = await JsonSerializer.DeserializeAsync<DataDragonChampionDetailCatalog>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            var championDetail = detailCatalog?.Data.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, dataDragonChampionId, StringComparison.OrdinalIgnoreCase))
                ?? detailCatalog?.Data.Values.FirstOrDefault();

            if (championDetail is null)
                throw new InvalidOperationException($"Riot Data Dragon {dataDragonVersion} returned no detail data for {dataDragonChampionId}.");

            return championDetail;
        }

        private static async Task<ChampionTileDownloadOutcome> DownloadChampionTileAsync(
            HttpClient httpClient,
            string dataDragonChampionId,
            int skinNumber,
            string tileDirectoryPath,
            string fileName,
            bool optimizeForLocalCache,
            CancellationToken cancellationToken)
        {
            string tileChampionId = NormalizeTileChampionId(dataDragonChampionId);
            string tileUrl = string.Format(
                CultureInfo.InvariantCulture,
                ChampionTileUrlFormat,
                Uri.EscapeDataString(tileChampionId),
                skinNumber);
            string destinationFilePath = Path.Combine(tileDirectoryPath, fileName);
            string temporaryFilePath = Path.Combine(tileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                using var response = await httpClient.GetAsync(tileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var destinationStream = File.Create(temporaryFilePath))
                {
                    await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
                }

                if (!File.Exists(temporaryFilePath) || new FileInfo(temporaryFilePath).Length == 0)
                    throw new InvalidOperationException("Champion tile download produced an empty file.");

                if (optimizeForLocalCache)
                    ChampionTileCacheImageOptimizer.TryOptimizeJpegInPlace(temporaryFilePath, cancellationToken);

                if (File.Exists(destinationFilePath)
                    && FilesHaveSameSha256(destinationFilePath, temporaryFilePath))
                {
                    return ChampionTileDownloadOutcome.Unchanged;
                }

                File.Move(temporaryFilePath, destinationFilePath, overwrite: true);
                return ChampionTileDownloadOutcome.Downloaded;
            }
            finally
            {
                TryDeleteFile(temporaryFilePath);
            }
        }

        private static async Task<ChampionTileDownloadOutcome> DownloadClassicChampionTileAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string imageFileName,
            string tileDirectoryPath,
            string fileName,
            bool optimizeForLocalCache,
            CancellationToken cancellationToken)
        {
            string tileUrl = string.Format(
                CultureInfo.InvariantCulture,
                ClassicChampionTileUrlFormat,
                Uri.EscapeDataString(dataDragonVersion),
                Uri.EscapeDataString(imageFileName));
            string destinationFilePath = Path.Combine(tileDirectoryPath, fileName);
            string temporaryFilePath = Path.Combine(tileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                using var response = await httpClient.GetAsync(tileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                ChampionTileCacheImageOptimizer.SaveImageBytesAsJpeg(
                    imageBytes,
                    temporaryFilePath,
                    optimizeForLocalCache,
                    cancellationToken);

                if (File.Exists(destinationFilePath)
                    && FilesHaveSameSha256(destinationFilePath, temporaryFilePath))
                {
                    return ChampionTileDownloadOutcome.Unchanged;
                }

                File.Move(temporaryFilePath, destinationFilePath, overwrite: true);
                return ChampionTileDownloadOutcome.Downloaded;
            }
            finally
            {
                TryDeleteFile(temporaryFilePath);
            }
        }

        private static string CreateChampionTileFileName(string dataDragonChampionId, int skinNumber)
        {
            string tileChampionId = NormalizeTileChampionId(dataDragonChampionId);
            string fileName = $"{tileChampionId}_{skinNumber.ToString(CultureInfo.InvariantCulture)}.jpg";
            string safeFileName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal)
                || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Riot Data Dragon returned an unsafe champion tile name '{fileName}'.");
            }

            return safeFileName;
        }

        private static string CreateClassicChampionTileFileName(
            string dataDragonChampionId,
            int skinNumber)
        {
            string tileChampionId = NormalizeTileChampionId(dataDragonChampionId);
            string fileName = skinNumber == 0
                ? $"{tileChampionId}_classic.jpg"
                : $"{tileChampionId}_classic_{skinNumber.ToString(CultureInfo.InvariantCulture)}.jpg";
            string safeFileName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal)
                || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Riot Data Dragon returned an unsafe League Classic tile name '{fileName}'.");
            }

            return safeFileName;
        }

        private static string NormalizeTileChampionId(string dataDragonChampionId)
        {
            return string.Equals(dataDragonChampionId, "Fiddlesticks", StringComparison.OrdinalIgnoreCase)
                ? "FiddleSticks"
                : dataDragonChampionId;
        }

        private static bool FilesHaveSameSha256(string firstFilePath, string secondFilePath)
        {
            using var firstStream = File.OpenRead(firstFilePath);
            using var secondStream = File.OpenRead(secondFilePath);

            byte[] firstHash = SHA256.HashData(firstStream);
            byte[] secondHash = SHA256.HashData(secondStream);
            return firstHash.AsSpan().SequenceEqual(secondHash);
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
            }
        }

        private static void Report(
            IProgress<ChampionTileDownloadProgress>? progress,
            string dataDragonVersion,
            string championName,
            int checkedTileCount,
            int downloadedTileCount,
            int unchangedTileCount,
            int failedTileCount,
            int? totalTileCount,
            string message)
        {
            progress?.Report(new ChampionTileDownloadProgress(
                dataDragonVersion,
                championName,
                checkedTileCount,
                downloadedTileCount,
                unchangedTileCount,
                failedTileCount,
                totalTileCount,
                message));
        }

        private static string FormatException(Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }

        private enum ChampionTileDownloadOutcome
        {
            Downloaded,
            Unchanged
        }

        private sealed class DataDragonChampionCatalog
        {
            public Dictionary<string, DataDragonChampion> Data { get; set; } = [];
        }

        private sealed class DataDragonChampion
        {
            public string? Id { get; set; }

            public string? Key { get; set; }
        }

        private sealed class DataDragonClassicChampionCatalog
        {
            public Dictionary<string, DataDragonClassicChampion> Data { get; set; } = [];
        }

        private sealed record DataDragonClassicChampion
        {
            public string? Id { get; init; }

            public string? Key { get; init; }

            public DataDragonChampionImage? Image { get; init; }

            public string ImageFileName { get; init; } = string.Empty;
        }

        private sealed class DataDragonChampionImage
        {
            public string? Full { get; set; }
        }

        private sealed class DataDragonChampionDetailCatalog
        {
            public Dictionary<string, DataDragonChampionDetail> Data { get; set; } = [];
        }

        private sealed class DataDragonChampionDetail
        {
            public string? Id { get; set; }

            public List<DataDragonChampionSkin> Skins { get; set; } = [];
        }

        private sealed class DataDragonChampionSkin
        {
            public int Num { get; set; }

            public int? ParentSkin { get; set; }
        }
    }
}
