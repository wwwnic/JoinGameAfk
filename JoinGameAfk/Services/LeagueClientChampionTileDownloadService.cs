using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
using LcuClient;

namespace JoinGameAfk.Services
{
    internal static class LeagueClientChampionTileDownloadService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<ChampionDefaultTileDownloadResult> DownloadDefaultChampionTilesAsync(
            Lcu.LeagueClientHttp http,
            IReadOnlyList<ChampionInfo> champions,
            string tileDirectoryPath,
            IProgress<ChampionDefaultTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(champions);
            Directory.CreateDirectory(tileDirectoryPath);

            int downloadedCount = 0;
            int unchangedCount = 0;
            int failedCount = 0;
            for (int index = 0; index < champions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChampionInfo champion = champions[index];
                int checkedCount = index + 1;
                try
                {
                    if (champion.Key <= 0 || string.IsNullOrWhiteSpace(champion.Name))
                        throw new InvalidOperationException("Champion key and name are required.");

                    string detailsJson = await http.GetChampionDetailsAsync(champion.Key, cancellationToken)
                        .ConfigureAwait(false);
                    var details = JsonSerializer.Deserialize<LeagueClientChampionDetails>(
                        detailsJson,
                        SerializerOptions);
                    LeagueClientChampionSkin? baseSkin = details?.Skins
                        .Where(skin => skin.Id > 0)
                        .OrderBy(skin => GetSkinNumber(skin.Id) == 0 ? 0 : 1)
                        .ThenBy(skin => GetSkinNumber(skin.Id))
                        .FirstOrDefault();
                    if (baseSkin is null)
                    {
                        throw new InvalidOperationException(
                            $"League Client returned no base picture metadata for {champion.Name}.");
                    }

                    string fileName = CreateFileName(champion, skinNumber: 0);
                    string assetPath = ResolveTilePath(champion.Key, baseSkin);
                    byte[] bytes = await http.GetGameDataAssetAsync(assetPath, cancellationToken)
                        .ConfigureAwait(false);
                    ValidateJpeg(bytes, champion.Name);
                    ChampionTileDownloadOutcome outcome = SaveTile(
                        tileDirectoryPath,
                        fileName,
                        bytes,
                        optimizeForLocalCache,
                        cancellationToken);
                    if (outcome == ChampionTileDownloadOutcome.Downloaded)
                        downloadedCount++;
                    else
                        unchangedCount++;

                    progress?.Report(new ChampionDefaultTileDownloadProgress(
                        LeagueClientChampionCatalogService.LocalCatalogVersion,
                        checkedCount,
                        downloadedCount,
                        unchangedCount,
                        failedCount,
                        champions.Count,
                        $"Reading default champion pictures from League Client {checkedCount}/{champions.Count}..."));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    progress?.Report(new ChampionDefaultTileDownloadProgress(
                        LeagueClientChampionCatalogService.LocalCatalogVersion,
                        checkedCount,
                        downloadedCount,
                        unchangedCount,
                        failedCount,
                        champions.Count,
                        $"Unable to read the default picture for {champion.Name}: {ex.GetType().Name}: {ex.Message}"));
                }
            }

            return new ChampionDefaultTileDownloadResult(
                LeagueClientChampionCatalogService.LocalCatalogVersion,
                champions.Count,
                downloadedCount,
                unchangedCount,
                failedCount,
                tileDirectoryPath,
                DateTime.UtcNow);
        }

        public static async Task<ChampionTileDownloadResult> DownloadChampionTilesAsync(
            Lcu.LeagueClientHttp http,
            ChampionInfo champion,
            string tileDirectoryPath,
            IProgress<ChampionTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(champion);
            if (champion.Key <= 0 || string.IsNullOrWhiteSpace(champion.Name))
                throw new ArgumentException("Champion key and name are required.", nameof(champion));

            Directory.CreateDirectory(tileDirectoryPath);
            Report(progress, champion.Name, 0, 0, 0, 0, null,
                $"Reading {champion.Name} pictures from the local League Client...");

            string detailsJson = await http.GetChampionDetailsAsync(champion.Key, cancellationToken)
                .ConfigureAwait(false);
            var details = JsonSerializer.Deserialize<LeagueClientChampionDetails>(detailsJson, SerializerOptions);
            var skins = details?.Skins
                .Where(skin => skin.Id > 0)
                .GroupBy(skin => skin.Id)
                .Select(group => group.First())
                .OrderBy(skin => GetSkinNumber(skin.Id))
                .ToList()
                ?? [];
            if (skins.Count == 0)
                throw new InvalidOperationException($"League Client returned no local champion pictures for {champion.Name}.");

            int checkedCount = 0;
            int downloadedCount = 0;
            int unchangedCount = 0;
            int failedCount = 0;
            foreach (LeagueClientChampionSkin skin in skins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;
                int skinNumber = GetSkinNumber(skin.Id);
                string fileName = CreateFileName(champion, skinNumber);
                string assetPath = ResolveTilePath(champion.Key, skin);
                Report(progress, champion.Name, checkedCount, downloadedCount, unchangedCount, failedCount, skins.Count,
                    $"Copying {champion.Name} pictures from League Client {checkedCount}/{skins.Count}...");

                try
                {
                    byte[] bytes = await http.GetGameDataAssetAsync(assetPath, cancellationToken).ConfigureAwait(false);
                    ValidateJpeg(bytes, champion.Name);
                    ChampionTileDownloadOutcome outcome = SaveTile(
                        tileDirectoryPath,
                        fileName,
                        bytes,
                        optimizeForLocalCache,
                        cancellationToken);
                    if (outcome == ChampionTileDownloadOutcome.Downloaded)
                        downloadedCount++;
                    else
                        unchangedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Report(progress, champion.Name, checkedCount, downloadedCount, unchangedCount, failedCount, skins.Count,
                        $"Unable to copy {fileName} from League Client: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Report(progress, champion.Name, checkedCount, downloadedCount, unchangedCount, failedCount, skins.Count,
                $"Finished copying {champion.Name} pictures from League Client. Updated {downloadedCount}; unchanged {unchangedCount}; failed {failedCount}.");
            return new ChampionTileDownloadResult(
                LeagueClientChampionCatalogService.LocalCatalogVersion,
                champion.Name,
                checkedCount,
                downloadedCount,
                unchangedCount,
                failedCount,
                tileDirectoryPath,
                DateTime.UtcNow);
        }

        private static string ResolveTilePath(int championId, LeagueClientChampionSkin skin)
        {
            string path = skin.TilePath?.Trim() ?? string.Empty;
            if (path.StartsWith("/lol-game-data/assets/", StringComparison.OrdinalIgnoreCase))
                return path;

            return $"/lol-game-data/assets/v1/champion-tiles/{championId}/{skin.Id}.jpg";
        }

        private static int GetSkinNumber(int skinId)
        {
            return Math.Max(0, skinId % 1000);
        }

        private static string CreateFileName(ChampionInfo champion, int skinNumber)
        {
            string id = !string.IsNullOrWhiteSpace(champion.Id)
                ? champion.Id
                : !string.IsNullOrWhiteSpace(champion.EnglishName)
                    ? champion.EnglishName
                    : champion.Name;
            string safeId = new(id.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(safeId))
                throw new InvalidOperationException($"Champion {champion.Name} has no safe image identifier.");

            return $"{safeId}_{skinNumber.ToString(CultureInfo.InvariantCulture)}.jpg";
        }

        private static ChampionTileDownloadOutcome SaveTile(
            string tileDirectoryPath,
            string fileName,
            byte[] bytes,
            bool optimizeForLocalCache,
            CancellationToken cancellationToken)
        {
            string destinationPath = Path.Combine(tileDirectoryPath, fileName);
            string temporaryPath = Path.Combine(tileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                if (optimizeForLocalCache)
                    ChampionTileCacheImageOptimizer.TryOptimizeJpegInPlace(temporaryPath, cancellationToken);

                if (File.Exists(destinationPath) && FilesHaveSameSha256(destinationPath, temporaryPath))
                    return ChampionTileDownloadOutcome.Unchanged;

                File.Move(temporaryPath, destinationPath, overwrite: true);
                return ChampionTileDownloadOutcome.Downloaded;
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private static bool FilesHaveSameSha256(string firstPath, string secondPath)
        {
            using var first = File.OpenRead(firstPath);
            using var second = File.OpenRead(secondPath);
            return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
        }

        private static void ValidateJpeg(byte[] bytes, string championName)
        {
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
                throw new InvalidOperationException($"League Client returned an invalid JPG for {championName}.");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void Report(
            IProgress<ChampionTileDownloadProgress>? progress,
            string championName,
            int checkedCount,
            int downloadedCount,
            int unchangedCount,
            int failedCount,
            int? totalCount,
            string message)
        {
            progress?.Report(new ChampionTileDownloadProgress(
                LeagueClientChampionCatalogService.LocalCatalogVersion,
                championName,
                checkedCount,
                downloadedCount,
                unchangedCount,
                failedCount,
                totalCount,
                message));
        }

        private sealed class LeagueClientChampionDetails
        {
            public List<LeagueClientChampionSkin> Skins { get; set; } = [];
        }

        private sealed class LeagueClientChampionSkin
        {
            public int Id { get; set; }

            public string? TilePath { get; set; }
        }

        private enum ChampionTileDownloadOutcome
        {
            Downloaded,
            Unchanged
        }
    }
}
