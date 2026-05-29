using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using JoinGameAfk.Model;

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
        private const string ChampionCatalogUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/en_US/champion.json";
        private const string ChampionDetailUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/en_US/champion/{1}.json";
        private const string ChampionTileUrlFormat = "https://ddragon.leagueoflegends.com/cdn/img/champion/tiles/{0}_{1}.jpg";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<ChampionTileDownloadResult> DownloadChampionTilesAsync(
            ChampionInfo champion,
            string? preferredDataDragonVersion,
            string tileDirectoryPath,
            IProgress<ChampionTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true)
        {
            ArgumentNullException.ThrowIfNull(champion);

            if (champion.Id <= 0 || string.IsNullOrWhiteSpace(champion.Name))
                throw new ArgumentException("Champion id and name are required.", nameof(champion));

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
                    champion,
                    cancellationToken)
                .ConfigureAwait(false);

            var championDetail = await FetchChampionDetailAsync(
                    httpClient,
                    dataDragonVersion,
                    catalogChampion.Id!,
                    cancellationToken)
                .ConfigureAwait(false);

            var skinNumbers = championDetail.Skins
                .Where(skin => skin.ParentSkin is null)
                .Select(skin => skin.Num)
                .Where(number => number >= 0)
                .Distinct()
                .OrderBy(number => number)
                .ToList();

            if (skinNumbers.Count == 0)
                throw new InvalidOperationException($"Riot Data Dragon returned no champion pictures for {champion.Name}.");

            int checkedTileCount = 0;
            int downloadedTileCount = 0;
            int unchangedTileCount = 0;
            int failedTileCount = 0;

            foreach (int skinNumber in skinNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                checkedTileCount++;
                string fileName = CreateChampionTileFileName(catalogChampion.Id!, skinNumber);
                Report(
                    progress,
                    dataDragonVersion,
                    champion.Name,
                    checkedTileCount,
                    downloadedTileCount,
                    unchangedTileCount,
                    failedTileCount,
                    skinNumbers.Count,
                    $"Downloading {champion.Name} pictures {checkedTileCount}/{skinNumbers.Count}...");

                try
                {
                    var outcome = await DownloadChampionTileAsync(
                            httpClient,
                            catalogChampion.Id!,
                            skinNumber,
                            tileDirectoryPath,
                            fileName,
                            optimizeForLocalCache,
                            cancellationToken)
                        .ConfigureAwait(false);

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
                        skinNumbers.Count,
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
                skinNumbers.Count,
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
            ChampionInfo champion,
            CancellationToken cancellationToken)
        {
            string championCatalogUrl = string.Format(CultureInfo.InvariantCulture, ChampionCatalogUrlFormat, dataDragonVersion);

            using var response = await httpClient.GetAsync(championCatalogUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var catalog = await JsonSerializer.DeserializeAsync<DataDragonChampionCatalog>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            string championKey = champion.Id.ToString(CultureInfo.InvariantCulture);
            var match = catalog?.Data.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, championKey, StringComparison.Ordinal));

            if (match is null)
                throw new InvalidOperationException($"Riot Data Dragon {dataDragonVersion} does not list {champion.Name} ({champion.Id}).");

            if (string.IsNullOrWhiteSpace(match.Id))
                throw new InvalidOperationException($"Riot Data Dragon {dataDragonVersion} did not return an image id for {champion.Name}.");

            match.Id = match.Id.Trim();
            return match;
        }

        private static async Task<DataDragonChampionDetail> FetchChampionDetailAsync(
            HttpClient httpClient,
            string dataDragonVersion,
            string dataDragonChampionId,
            CancellationToken cancellationToken)
        {
            string championDetailUrl = string.Format(
                CultureInfo.InvariantCulture,
                ChampionDetailUrlFormat,
                dataDragonVersion,
                Uri.EscapeDataString(dataDragonChampionId));

            using var response = await httpClient.GetAsync(championDetailUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
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
            string tileUrl = string.Format(
                CultureInfo.InvariantCulture,
                ChampionTileUrlFormat,
                Uri.EscapeDataString(dataDragonChampionId),
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

        private static string CreateChampionTileFileName(string dataDragonChampionId, int skinNumber)
        {
            string fileName = $"{dataDragonChampionId}_{skinNumber.ToString(CultureInfo.InvariantCulture)}.jpg";
            string safeFileName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal)
                || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Riot Data Dragon returned an unsafe champion tile name '{fileName}'.");
            }

            return safeFileName;
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
