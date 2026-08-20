using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JoinGameAfk.Enums;
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

            string summaryJson = await http.GetChampionSummaryAsync(cancellationToken).ConfigureAwait(false);
            var summaries = JsonSerializer.Deserialize<List<LeagueClientChampionSummary>>(
                    summaryJson,
                    SerializerOptions)
                ?? [];
            Dictionary<int, LeagueClientChampionSummary> classicSummariesByCanonicalId = summaries
                .Where(summary =>
                    LeagueChampionId.IsClassicVariant(summary.Id)
                    && summary.Alias?.StartsWith("Jade_", StringComparison.OrdinalIgnoreCase) == true)
                .GroupBy(summary => LeagueChampionId.ToCanonical(summary.Id))
                .ToDictionary(group => group.Key, group => group.First());
            IReadOnlyList<ChampionInfo> classicChampions = champions
                .Where(champion => champion.SupportsLeagueClassic == true)
                .ToList();
            int downloadedCount = 0;
            int unchangedCount = 0;
            int failedCount = 0;
            int checkedCount = 0;
            int totalTileCount = champions.Count + classicChampions.Count;
            for (int index = 0; index < champions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ChampionInfo champion = champions[index];
                checkedCount++;
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
                        totalTileCount,
                        $"Reading LoL default pictures from League Client {checkedCount}/{totalTileCount}..."));
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
                        totalTileCount,
                        $"Unable to read the LoL default picture for {champion.Name}: {ex.GetType().Name}: {ex.Message}"));
                }
            }

            foreach (ChampionInfo champion in classicChampions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;
                try
                {
                    if (!classicSummariesByCanonicalId.TryGetValue(
                            champion.Key,
                            out LeagueClientChampionSummary? classicChampion))
                    {
                        throw new InvalidOperationException(
                            $"League Client does not list {champion.Name} in League Classic.");
                    }

                    string iconPath = $"/lol-game-data/assets/v1/champion-icons/{classicChampion.Id}.png";
                    byte[] bytes = await http.GetGameDataAssetAsync(iconPath, cancellationToken)
                        .ConfigureAwait(false);
                    ChampionTileDownloadOutcome outcome = SaveImageTile(
                        tileDirectoryPath,
                        CreateClassicFileName(champion, skinNumber: 0),
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
                        totalTileCount,
                        $"Reading League Classic default pictures from League Client {checkedCount}/{totalTileCount}..."));
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
                        totalTileCount,
                        $"Unable to read the League Classic default picture for {champion.Name}: {ex.GetType().Name}: {ex.Message}"));
                }
            }

            return new ChampionDefaultTileDownloadResult(
                LeagueClientChampionCatalogService.LocalCatalogVersion,
                totalTileCount,
                downloadedCount,
                unchangedCount,
                failedCount,
                tileDirectoryPath,
                DateTime.UtcNow);
        }

        public static async Task<ChampionTileDownloadResult> DownloadChampionTilesAsync(
            Lcu.LeagueClientHttp http,
            ChampionInfo champion,
            LeagueGameMode gameMode,
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

            LeagueClientChampionSummary? classicChampion = gameMode == LeagueGameMode.Classic
                ? await TryGetClassicChampionAsync(http, champion, cancellationToken).ConfigureAwait(false)
                : null;
            if (gameMode == LeagueGameMode.Classic && classicChampion is null)
            {
                throw new InvalidOperationException(
                    $"League Client does not list {champion.Name} in League Classic.");
            }

            int detailsChampionId = classicChampion?.Id ?? champion.Key;
            string detailsJson = await http.GetChampionDetailsAsync(detailsChampionId, cancellationToken)
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

            int totalTileCount = skins.Count;

            int checkedCount = 0;
            int downloadedCount = 0;
            int unchangedCount = 0;
            int failedCount = 0;
            foreach (LeagueClientChampionSkin skin in skins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;
                int skinNumber = GetSkinNumber(skin.Id);
                string fileName = gameMode == LeagueGameMode.Classic
                    ? CreateClassicFileName(champion, skinNumber)
                    : CreateFileName(champion, skinNumber);
                Report(progress, champion.Name, checkedCount, downloadedCount, unchangedCount, failedCount, totalTileCount,
                    $"Copying {champion.Name} pictures from League Client {checkedCount}/{totalTileCount}...");

                try
                {
                    ChampionTileDownloadOutcome outcome;
                    if (gameMode == LeagueGameMode.Classic && skinNumber == 0)
                    {
                        string iconPath = $"/lol-game-data/assets/v1/champion-icons/{detailsChampionId}.png";
                        byte[] bytes = await http.GetGameDataAssetAsync(iconPath, cancellationToken).ConfigureAwait(false);
                        outcome = SaveImageTile(
                            tileDirectoryPath,
                            fileName,
                            bytes,
                            optimizeForLocalCache,
                            cancellationToken);
                    }
                    else
                    {
                        string assetPath = ResolveTilePath(detailsChampionId, skin);
                        byte[] bytes = await http.GetGameDataAssetAsync(assetPath, cancellationToken).ConfigureAwait(false);
                        ValidateJpeg(bytes, champion.Name);
                        outcome = SaveTile(
                            tileDirectoryPath,
                            fileName,
                            bytes,
                            optimizeForLocalCache,
                            cancellationToken);
                    }

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
                    Report(progress, champion.Name, checkedCount, downloadedCount, unchangedCount, failedCount, totalTileCount,
                        $"Unable to copy {fileName} from League Client: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Report(progress, champion.Name, checkedCount, downloadedCount, unchangedCount, failedCount, totalTileCount,
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

        private static async Task<LeagueClientChampionSummary?> TryGetClassicChampionAsync(
            Lcu.LeagueClientHttp http,
            ChampionInfo champion,
            CancellationToken cancellationToken)
        {
            string summaryJson = await http.GetChampionSummaryAsync(cancellationToken).ConfigureAwait(false);
            var summaries = JsonSerializer.Deserialize<List<LeagueClientChampionSummary>>(
                    summaryJson,
                    SerializerOptions)
                ?? [];

            return summaries.FirstOrDefault(candidate =>
                LeagueChampionId.IsClassicVariant(candidate.Id)
                && LeagueChampionId.ToCanonical(candidate.Id) == champion.Key
                && candidate.Alias?.StartsWith("Jade_", StringComparison.OrdinalIgnoreCase) == true);
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
            string safeId = GetSafeChampionImageId(champion);
            return $"{safeId}_{skinNumber.ToString(CultureInfo.InvariantCulture)}.jpg";
        }

        private static string CreateClassicFileName(ChampionInfo champion, int skinNumber)
        {
            string safeId = GetSafeChampionImageId(champion);
            return skinNumber == 0
                ? $"{safeId}_classic.jpg"
                : $"{safeId}_classic_{skinNumber.ToString(CultureInfo.InvariantCulture)}.jpg";
        }

        private static string GetSafeChampionImageId(ChampionInfo champion)
        {
            string id = !string.IsNullOrWhiteSpace(champion.Id)
                ? champion.Id
                : !string.IsNullOrWhiteSpace(champion.EnglishName)
                    ? champion.EnglishName
                    : champion.Name;
            string safeId = new(id.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(safeId))
                throw new InvalidOperationException($"Champion {champion.Name} has no safe image identifier.");

            return safeId;
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

        private static ChampionTileDownloadOutcome SaveImageTile(
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
                ChampionTileCacheImageOptimizer.SaveImageBytesAsJpeg(
                    bytes,
                    temporaryPath,
                    optimizeForLocalCache,
                    cancellationToken);

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

        private sealed class LeagueClientChampionSummary
        {
            public int Id { get; set; }

            public string? Alias { get; set; }
        }

        private enum ChampionTileDownloadOutcome
        {
            Downloaded,
            Unchanged
        }
    }
}
