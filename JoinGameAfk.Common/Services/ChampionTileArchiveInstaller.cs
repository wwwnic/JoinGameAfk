using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using JoinGameAfk.Constant;

namespace JoinGameAfk.Services
{
    public sealed record ChampionTileArchiveProgress(long BytesCompleted, long? TotalBytes, string Message);

    public sealed record ChampionTileArchiveInstallResult(
        string DataDragonVersion,
        string? ArchiveFilePath,
        long ArchiveSizeBytes,
        int CheckedTileCount,
        int UpdatedTileCount,
        int UnchangedTileCount,
        int CachedTileCount,
        string CacheDirectory,
        DateTime LastSyncedAtUtc,
        bool ArchiveDeleted = true,
        string? ArchiveDeleteError = null);

    public sealed record ChampionTileCacheSyncInfo(
        string? DataDragonVersion,
        int CachedTileCount,
        string CacheDirectory,
        string? ArchiveFilePath,
        long ArchiveSizeBytes,
        DateTime? LastSyncedAtUtc);

    public sealed record ChampionTileArchiveCleanupFailure(string FilePath, string ErrorMessage);

    public sealed record ChampionTileArchiveCleanupResult(
        int DeletedFileCount,
        IReadOnlyList<ChampionTileArchiveCleanupFailure> Failures)
    {
        public int FailedFileCount => Failures.Count;
    }

    public sealed record ChampionTileArchiveExtractionResult(
        int CheckedTileCount,
        int UpdatedTileCount,
        int UnchangedTileCount);

    public sealed record ChampionTileArchiveInstallOptions(
        string TileDirectoryPath,
        string ArchiveDirectoryPath,
        string CacheFilePath,
        int? MaxTileIndex = null,
        bool DeleteArchiveAfterExtraction = true)
    {
        public static ChampionTileArchiveInstallOptions Default => new(
            AppStorage.ChampionTileDirectoryPath,
            AppStorage.ChampionTileArchiveDirectoryPath,
            AppStorage.ChampionTileCacheFilePath);
    }

    public static class ChampionTileArchiveInstaller
    {
        public const int ChampionTileCacheFileVersion = 1;

        private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
        private const string DragontailArchiveUrlFormat = "https://ddragon.leagueoflegends.com/cdn/dragontail-{0}.tgz";
        private const string ChampionTileArchivePathSegment = "img/champion/tiles/";
        private static readonly TimeSpan ArchiveDownloadTimeout = TimeSpan.FromMinutes(45);

        private static readonly JsonSerializerOptions CacheSerializerOptions = new()
        {
            WriteIndented = true
        };

        public static int GetTileFileCount()
        {
            return GetTileFileCount(AppStorage.ChampionTileDirectoryPath);
        }

        public static int GetTileFileCount(string tileDirectoryPath)
        {
            return Directory.Exists(tileDirectoryPath)
                ? Directory.EnumerateFiles(tileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly).Count()
                : 0;
        }

        public static ChampionTileCacheSyncInfo GetCacheSyncInfo()
        {
            return GetCacheSyncInfo(AppStorage.ChampionTileDirectoryPath, AppStorage.ChampionTileCacheFilePath);
        }

        public static ChampionTileCacheSyncInfo GetCacheSyncInfo(string tileDirectoryPath, string cacheFilePath)
        {
            try
            {
                if (!File.Exists(cacheFilePath))
                    return new ChampionTileCacheSyncInfo(null, GetTileFileCount(tileDirectoryPath), tileDirectoryPath, null, 0, null);

                var cacheFile = JsonSerializer.Deserialize<ChampionTileCacheFile>(
                    File.ReadAllText(cacheFilePath),
                    CacheSerializerOptions);

                if (cacheFile is null)
                    return new ChampionTileCacheSyncInfo(null, GetTileFileCount(tileDirectoryPath), tileDirectoryPath, null, 0, null);

                string? archiveFilePath = !string.IsNullOrWhiteSpace(cacheFile.ArchiveFilePath)
                    && File.Exists(cacheFile.ArchiveFilePath)
                        ? cacheFile.ArchiveFilePath
                        : null;

                long archiveSizeBytes = archiveFilePath is not null
                    ? new FileInfo(archiveFilePath).Length
                    : cacheFile.ArchiveSizeBytes;

                return new ChampionTileCacheSyncInfo(
                    cacheFile.DataDragonVersion,
                    GetTileFileCount(tileDirectoryPath),
                    tileDirectoryPath,
                    archiveFilePath,
                    archiveSizeBytes,
                    cacheFile.LastSyncedAtUtc);
            }
            catch
            {
                return new ChampionTileCacheSyncInfo(null, GetTileFileCount(tileDirectoryPath), tileDirectoryPath, null, 0, null);
            }
        }

        public static Task<ChampionTileArchiveInstallResult> InstallLatestDataDragonArchiveAsync(
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return InstallLatestDataDragonArchiveAsync(ChampionTileArchiveInstallOptions.Default, progress, cancellationToken);
        }

        public static async Task<ChampionTileArchiveInstallResult> InstallLatestDataDragonArchiveAsync(
            ChampionTileArchiveInstallOptions options,
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ChampionTileArchiveProgress(0, null, "Checking latest Riot Data Dragon version..."));
            string dataDragonVersion = await FetchLatestVersionAsync(cancellationToken).ConfigureAwait(false);

            return await InstallDataDragonArchiveAsync(dataDragonVersion, options, progress, cancellationToken).ConfigureAwait(false);
        }

        public static Task<ChampionTileArchiveInstallResult> InstallDataDragonArchiveAsync(
            string dataDragonVersion,
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return InstallDataDragonArchiveAsync(dataDragonVersion, ChampionTileArchiveInstallOptions.Default, progress, cancellationToken);
        }

        public static async Task<ChampionTileArchiveInstallResult> InstallDataDragonArchiveAsync(
            string dataDragonVersion,
            ChampionTileArchiveInstallOptions options,
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options.MaxTileIndex is < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum champion tile index must be zero or greater.");

            Directory.CreateDirectory(options.TileDirectoryPath);
            Directory.CreateDirectory(options.ArchiveDirectoryPath);
            ReportArchiveCleanup(DeleteDownloadedArchives(options.ArchiveDirectoryPath), progress);

            dataDragonVersion = NormalizeDataDragonVersion(dataDragonVersion);
            string archiveFilePath = Path.Combine(options.ArchiveDirectoryPath, $"dragontail-{dataDragonVersion}.tgz");

            long archiveSizeBytes = await EnsureArchiveDownloadedAsync(
                    dataDragonVersion,
                    archiveFilePath,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new ChampionTileArchiveProgress(0, null, "Checking champion pictures from local Data Dragon archive..."));
            ChampionTileArchiveExtractionResult extractionResult;
            bool archiveDeleted = false;
            Exception? archiveDeleteException = null;
            try
            {
                extractionResult = ExtractChampionTilesFromTarGz(
                    archiveFilePath,
                    options.TileDirectoryPath,
                    options.MaxTileIndex,
                    cancellationToken);
                if (extractionResult.CheckedTileCount == 0)
                    throw new InvalidOperationException("No champion tile jpg files were found in the Riot Data Dragon archive.");

                progress?.Report(new ChampionTileArchiveProgress(
                    archiveSizeBytes,
                    archiveSizeBytes,
                    $"Champion picture hash check complete. Checked {extractionResult.CheckedTileCount} tiles; updated {extractionResult.UpdatedTileCount}; unchanged {extractionResult.UnchangedTileCount}."));
            }
            finally
            {
                if (options.DeleteArchiveAfterExtraction)
                {
                    progress?.Report(new ChampionTileArchiveProgress(archiveSizeBytes, archiveSizeBytes, "Removing Data Dragon archive after extraction..."));
                    archiveDeleted = TryDeleteFile(archiveFilePath, out archiveDeleteException);
                    if (archiveDeleted)
                    {
                        progress?.Report(new ChampionTileArchiveProgress(archiveSizeBytes, archiveSizeBytes, "Data Dragon archive removed after extraction."));
                    }
                    else
                    {
                        progress?.Report(new ChampionTileArchiveProgress(
                            archiveSizeBytes,
                            archiveSizeBytes,
                            $"Unable to remove Data Dragon archive after extraction: {FormatException(archiveDeleteException)}"));
                    }
                }
                else
                {
                    archiveDeleted = false;
                    progress?.Report(new ChampionTileArchiveProgress(
                        archiveSizeBytes,
                        archiveSizeBytes,
                        $"Keeping Data Dragon archive {Path.GetFileName(archiveFilePath)} for reuse."));
                }
            }

            DateTime lastSyncedAtUtc = DateTime.UtcNow;
            var result = new ChampionTileArchiveInstallResult(
                dataDragonVersion,
                archiveDeleted ? null : archiveFilePath,
                archiveSizeBytes,
                extractionResult.CheckedTileCount,
                extractionResult.UpdatedTileCount,
                extractionResult.UnchangedTileCount,
                GetTileFileCount(options.TileDirectoryPath),
                options.TileDirectoryPath,
                lastSyncedAtUtc,
                archiveDeleted,
                archiveDeleteException is null ? null : FormatException(archiveDeleteException));

            SaveCacheFile(result, options.CacheFilePath, progress);

            return result;
        }

        public static ChampionTileArchiveCleanupResult DeleteDownloadedArchives()
        {
            return DeleteDownloadedArchives(AppStorage.ChampionTileArchiveDirectoryPath);
        }

        public static ChampionTileArchiveCleanupResult DeleteDownloadedArchives(string archiveDirectoryPath)
        {
            var failures = new List<ChampionTileArchiveCleanupFailure>();
            int deletedFileCount = 0;

            try
            {
                if (!Directory.Exists(archiveDirectoryPath))
                    return new ChampionTileArchiveCleanupResult(deletedFileCount, failures);

                foreach (string archiveFilePath in Directory.EnumerateFiles(archiveDirectoryPath, "dragontail-*.tgz", SearchOption.TopDirectoryOnly))
                {
                    if (TryDeleteFile(archiveFilePath, out Exception? exception))
                        deletedFileCount++;
                    else
                        failures.Add(new ChampionTileArchiveCleanupFailure(archiveFilePath, FormatException(exception)));
                }

                foreach (string temporaryArchiveFilePath in Directory.EnumerateFiles(archiveDirectoryPath, "dragontail-*.tgz.download", SearchOption.TopDirectoryOnly))
                {
                    if (TryDeleteFile(temporaryArchiveFilePath, out Exception? exception))
                        deletedFileCount++;
                    else
                        failures.Add(new ChampionTileArchiveCleanupFailure(temporaryArchiveFilePath, FormatException(exception)));
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ChampionTileArchiveCleanupFailure(archiveDirectoryPath, FormatException(ex)));
            }

            return new ChampionTileArchiveCleanupResult(deletedFileCount, failures);
        }

        public static void SaveCacheFile(
            ChampionTileArchiveInstallResult result,
            string cacheFilePath,
            IProgress<ChampionTileArchiveProgress>? progress = null)
        {
            try
            {
                string? cacheDirectoryPath = Path.GetDirectoryName(cacheFilePath);
                if (!string.IsNullOrWhiteSpace(cacheDirectoryPath))
                    Directory.CreateDirectory(cacheDirectoryPath);

                var cacheFile = new ChampionTileCacheFile
                {
                    Version = ChampionTileCacheFileVersion,
                    DataDragonVersion = result.DataDragonVersion,
                    CachedTileCount = result.CachedTileCount,
                    ArchiveFilePath = result.ArchiveFilePath,
                    ArchiveSizeBytes = result.ArchiveSizeBytes,
                    LastSyncedAtUtc = result.LastSyncedAtUtc
                };

                File.WriteAllText(
                    cacheFilePath,
                    JsonSerializer.Serialize(cacheFile, CacheSerializerOptions));
            }
            catch (Exception ex)
            {
                progress?.Report(new ChampionTileArchiveProgress(
                    0,
                    null,
                    $"Unable to save champion picture cache metadata: {FormatException(ex)}"));
            }
        }

        private static async Task<string> FetchLatestVersionAsync(CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            using var response = await httpClient.GetAsync(VersionsUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var versions = await JsonSerializer.DeserializeAsync<List<string>>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string? latestVersion = versions?.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
            if (latestVersion is null)
                throw new InvalidOperationException("Riot Data Dragon returned no versions.");

            return latestVersion.Trim();
        }

        private static async Task<long> EnsureArchiveDownloadedAsync(
            string dataDragonVersion,
            string archiveFilePath,
            IProgress<ChampionTileArchiveProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (File.Exists(archiveFilePath) && new FileInfo(archiveFilePath).Length > 0)
            {
                long existingSize = new FileInfo(archiveFilePath).Length;
                progress?.Report(new ChampionTileArchiveProgress(
                    existingSize,
                    existingSize,
                    $"Using existing Data Dragon archive {Path.GetFileName(archiveFilePath)} ({FormatMegabytes(existingSize)}) for extraction."));
                return existingSize;
            }

            string archiveUrl = string.Format(CultureInfo.InvariantCulture, DragontailArchiveUrlFormat, dataDragonVersion);
            string temporaryArchiveFilePath = $"{archiveFilePath}.download";

            try
            {
                if (File.Exists(temporaryArchiveFilePath))
                    File.Delete(temporaryArchiveFilePath);

                using var httpClient = new HttpClient
                {
                    Timeout = ArchiveDownloadTimeout
                };

                using var response = await httpClient.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                long bytesDownloaded = 0;
                byte[] buffer = new byte[1024 * 128];

                await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var destinationStream = File.Create(temporaryArchiveFilePath))
                {
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        bytesDownloaded += bytesRead;

                        progress?.Report(new ChampionTileArchiveProgress(
                            bytesDownloaded,
                            totalBytes,
                            totalBytes is long total
                                ? $"Downloading Data Dragon archive: {FormatMegabytes(bytesDownloaded)} / {FormatMegabytes(total)}"
                                : $"Downloading Data Dragon archive: {FormatMegabytes(bytesDownloaded)}"));
                    }
                }

                if (!File.Exists(temporaryArchiveFilePath) || new FileInfo(temporaryArchiveFilePath).Length == 0)
                    throw new InvalidOperationException("Riot Data Dragon archive download produced an empty file.");

                File.Move(temporaryArchiveFilePath, archiveFilePath, overwrite: true);
                return new FileInfo(archiveFilePath).Length;
            }
            catch
            {
                if (!TryDeleteFile(temporaryArchiveFilePath, out Exception? cleanupException))
                {
                    progress?.Report(new ChampionTileArchiveProgress(
                        0,
                        null,
                        $"Unable to remove partial Data Dragon archive download: {FormatException(cleanupException)}"));
                }

                throw;
            }
        }

        private static ChampionTileArchiveExtractionResult ExtractChampionTilesFromTarGz(
            string archiveFilePath,
            string tileDirectoryPath,
            int? maxTileIndex,
            CancellationToken cancellationToken)
        {
            int checkedTileCount = 0;
            int updatedTileCount = 0;
            int unchangedTileCount = 0;

            using var fileStream = File.OpenRead(archiveFilePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var tarReader = new TarReader(gzipStream);

            TarEntry? entry;
            while ((entry = tarReader.GetNextEntry()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string normalizedEntryName = entry.Name.Replace('\\', '/');
                if (!IsChampionTileArchiveEntry(normalizedEntryName)
                    || entry.DataStream is null)
                {
                    continue;
                }

                string fileName = Path.GetFileName(normalizedEntryName);
                if (!TryGetSafeFileName(fileName, out fileName))
                    continue;

                if (maxTileIndex is int maximumTileIndex
                    && (!TryGetTileIndex(fileName, out int tileIndex) || tileIndex > maximumTileIndex))
                {
                    continue;
                }

                string destinationFilePath = Path.Combine(tileDirectoryPath, fileName);
                string temporaryTileFilePath = Path.Combine(tileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

                try
                {
                    using (var outputStream = File.Create(temporaryTileFilePath))
                    {
                        entry.DataStream.CopyTo(outputStream);
                    }

                    checkedTileCount++;
                    if (File.Exists(destinationFilePath)
                        && FilesHaveSameSha256(destinationFilePath, temporaryTileFilePath))
                    {
                        unchangedTileCount++;
                        continue;
                    }

                    File.Move(temporaryTileFilePath, destinationFilePath, overwrite: true);
                    updatedTileCount++;
                }
                finally
                {
                    TryDeleteFile(temporaryTileFilePath, out _);
                }
            }

            return new ChampionTileArchiveExtractionResult(
                checkedTileCount,
                updatedTileCount,
                unchangedTileCount);
        }

        private static bool IsChampionTileArchiveEntry(string normalizedEntryName)
        {
            return normalizedEntryName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                && (normalizedEntryName.StartsWith(ChampionTileArchivePathSegment, StringComparison.OrdinalIgnoreCase)
                    || normalizedEntryName.Contains($"/{ChampionTileArchivePathSegment}", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetSafeFileName(string? value, out string fileName)
        {
            fileName = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            string safeFileName = Path.GetFileName(trimmed);
            if (!string.Equals(trimmed, safeFileName, StringComparison.Ordinal)
                || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fileName = safeFileName;
            return true;
        }

        private static bool TryGetTileIndex(string fileName, out int tileIndex)
        {
            tileIndex = 0;
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            int separatorIndex = nameWithoutExtension.LastIndexOf('_');
            return separatorIndex >= 0
                && int.TryParse(
                    nameWithoutExtension[(separatorIndex + 1)..],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out tileIndex)
                && tileIndex >= 0;
        }

        private static bool FilesHaveSameSha256(string firstFilePath, string secondFilePath)
        {
            using var firstStream = File.OpenRead(firstFilePath);
            using var secondStream = File.OpenRead(secondFilePath);

            byte[] firstHash = SHA256.HashData(firstStream);
            byte[] secondHash = SHA256.HashData(secondStream);
            return firstHash.AsSpan().SequenceEqual(secondHash);
        }

        private static string FormatMegabytes(long bytes)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        private static string NormalizeDataDragonVersion(string dataDragonVersion)
        {
            if (string.IsNullOrWhiteSpace(dataDragonVersion))
                throw new ArgumentException("Data Dragon version is required.", nameof(dataDragonVersion));

            string trimmed = dataDragonVersion.Trim();
            if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal)
                || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Data Dragon version is not a valid file name segment.", nameof(dataDragonVersion));
            }

            return trimmed;
        }

        private static void ReportArchiveCleanup(
            ChampionTileArchiveCleanupResult cleanupResult,
            IProgress<ChampionTileArchiveProgress>? progress)
        {
            if (cleanupResult.DeletedFileCount > 0)
            {
                progress?.Report(new ChampionTileArchiveProgress(
                    0,
                    null,
                    $"Removed {cleanupResult.DeletedFileCount} stale Data Dragon archive file(s)."));
            }

            foreach (var failure in cleanupResult.Failures)
            {
                progress?.Report(new ChampionTileArchiveProgress(
                    0,
                    null,
                    $"Unable to remove stale Data Dragon archive file '{Path.GetFileName(failure.FilePath)}': {failure.ErrorMessage}"));
            }
        }

        private static bool TryDeleteFile(string filePath, out Exception? exception)
        {
            exception = null;

            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        private static string FormatException(Exception? exception)
        {
            return exception is null
                ? "Unknown error."
                : $"{exception.GetType().Name}: {exception.Message}";
        }

        private sealed class ChampionTileCacheFile
        {
            public int Version { get; set; } = ChampionTileCacheFileVersion;

            public string? DataDragonVersion { get; set; }

            public int CachedTileCount { get; set; }

            public string? ArchiveFilePath { get; set; }

            public long ArchiveSizeBytes { get; set; }

            public DateTime? LastSyncedAtUtc { get; set; }
        }
    }
}
