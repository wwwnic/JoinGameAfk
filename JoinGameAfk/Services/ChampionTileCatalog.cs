using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;

namespace JoinGameAfk.Services
{
    public sealed record ChampionTileOption(string FileName, string DisplayName)
    {
        public ImageSource? ImageSource => ChampionTileCatalog.GetImageSource(FileName);

        public string ShortDisplayName
        {
            get
            {
                int detailsIndex = DisplayName.IndexOf(" (", StringComparison.Ordinal);
                return detailsIndex > 0
                    ? DisplayName[..detailsIndex]
                    : DisplayName;
            }
        }
    }

    public sealed record ChampionTileImportResult(int ImportedCount, int SkippedCount, int FailedCount, string SourceDirectory, string CacheDirectory);

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

    public static class ChampionTileCatalog
    {
        private const int ChampionTileCacheFileVersion = 1;
        private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
        private const string DragontailArchiveUrlFormat = "https://ddragon.leagueoflegends.com/cdn/dragontail-{0}.tgz";
        private static readonly TimeSpan ArchiveDownloadTimeout = TimeSpan.FromMinutes(45);

        private static readonly JsonSerializerOptions CacheSerializerOptions = new()
        {
            WriteIndented = true
        };

        private static readonly IReadOnlyDictionary<string, string> ChampionKeyAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NUNUWILLUMP"] = "NUNU",
                ["RENATAGLASC"] = "RENATA",
                ["WUKONG"] = "MONKEYKING"
            };

        private static readonly object ImageSourceLock = new();
        private static readonly Dictionary<string, ImageSource?> ImageSourceCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CatalogLock = new();
        private static IReadOnlyDictionary<string, IReadOnlyList<ChampionTileOption>>? _optionsByChampionKey;

        public static event EventHandler? TileCatalogChanged;

        public static string TileDirectoryPath => AppStorage.ChampionTileDirectoryPath;

        public static IReadOnlyList<ChampionTileOption> GetOptions(ChampionInfo champion)
        {
            string championKey = GetChampionTileKey(champion.Name);
            return OptionsByChampionKey.TryGetValue(championKey, out var options)
                ? options
                : [];
        }

        public static int GetTileFileCount()
        {
            return Directory.Exists(TileDirectoryPath)
                ? Directory.EnumerateFiles(TileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly).Count()
                : 0;
        }

        public static ChampionTileCacheSyncInfo GetCacheSyncInfo()
        {
            try
            {
                if (!File.Exists(AppStorage.ChampionTileCacheFilePath))
                    return new ChampionTileCacheSyncInfo(null, GetTileFileCount(), TileDirectoryPath, null, 0, null);

                var cacheFile = JsonSerializer.Deserialize<ChampionTileCacheFile>(
                    File.ReadAllText(AppStorage.ChampionTileCacheFilePath),
                    CacheSerializerOptions);

                if (cacheFile is null)
                    return new ChampionTileCacheSyncInfo(null, GetTileFileCount(), TileDirectoryPath, null, 0, null);

                string? archiveFilePath = !string.IsNullOrWhiteSpace(cacheFile.ArchiveFilePath)
                    && File.Exists(cacheFile.ArchiveFilePath)
                        ? cacheFile.ArchiveFilePath
                        : null;

                long archiveSizeBytes = archiveFilePath is not null
                    ? new FileInfo(archiveFilePath).Length
                    : cacheFile.ArchiveSizeBytes;

                return new ChampionTileCacheSyncInfo(
                    cacheFile.DataDragonVersion,
                    GetTileFileCount(),
                    TileDirectoryPath,
                    archiveFilePath,
                    archiveSizeBytes,
                    cacheFile.LastSyncedAtUtc);
            }
            catch
            {
                return new ChampionTileCacheSyncInfo(null, GetTileFileCount(), TileDirectoryPath, null, 0, null);
            }
        }

        public static ChampionTileImportResult ImportFromDirectory(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException("Champion picture source folder was not found.");

            AppStorage.EnsureChampionTileDirectoryExists();

            int importedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            foreach (string sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*.jpg", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (!TryGetSafeFileName(Path.GetFileName(sourceFilePath), out string fileName))
                    {
                        skippedCount++;
                        continue;
                    }

                    string destinationFilePath = Path.Combine(TileDirectoryPath, fileName);
                    if (string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(destinationFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        skippedCount++;
                        continue;
                    }

                    File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
                    importedCount++;
                }
                catch
                {
                    failedCount++;
                }
            }

            Reload();

            return new ChampionTileImportResult(importedCount, skippedCount, failedCount, sourceDirectory, TileDirectoryPath);
        }

        public static async Task<ChampionTileArchiveInstallResult> InstallLatestDataDragonArchiveAsync(
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ChampionTileArchiveProgress(0, null, "Checking latest Riot Data Dragon version..."));
            string dataDragonVersion = await FetchLatestVersionAsync(cancellationToken).ConfigureAwait(false);

            return await InstallDataDragonArchiveAsync(dataDragonVersion, progress, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ChampionTileArchiveInstallResult> InstallDataDragonArchiveAsync(
            string dataDragonVersion,
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            AppStorage.EnsureChampionTileDirectoryExists();
            AppStorage.EnsureChampionTileArchiveDirectoryExists();
            ReportArchiveCleanup(DeleteDownloadedArchives(), progress);

            dataDragonVersion = NormalizeDataDragonVersion(dataDragonVersion);
            string archiveFilePath = Path.Combine(AppStorage.ChampionTileArchiveDirectoryPath, $"dragontail-{dataDragonVersion}.tgz");

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
                extractionResult = ExtractChampionTilesFromTarGz(archiveFilePath, cancellationToken);
                progress?.Report(new ChampionTileArchiveProgress(
                    archiveSizeBytes,
                    archiveSizeBytes,
                    $"Champion picture hash check complete. Checked {extractionResult.CheckedTileCount} tiles; updated {extractionResult.UpdatedTileCount}; unchanged {extractionResult.UnchangedTileCount}."));
            }
            finally
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

            DateTime lastSyncedAtUtc = DateTime.UtcNow;
            var result = new ChampionTileArchiveInstallResult(
                dataDragonVersion,
                archiveDeleted ? null : archiveFilePath,
                archiveSizeBytes,
                extractionResult.CheckedTileCount,
                extractionResult.UpdatedTileCount,
                extractionResult.UnchangedTileCount,
                GetTileFileCount(),
                TileDirectoryPath,
                lastSyncedAtUtc,
                archiveDeleted,
                archiveDeleteException is null ? null : FormatException(archiveDeleteException));

            SaveCacheFile(result, progress);
            Reload();

            return result;
        }

        public static ChampionTileArchiveCleanupResult DeleteDownloadedArchives()
        {
            var failures = new List<ChampionTileArchiveCleanupFailure>();
            int deletedFileCount = 0;

            try
            {
                if (!Directory.Exists(AppStorage.ChampionTileArchiveDirectoryPath))
                    return new ChampionTileArchiveCleanupResult(deletedFileCount, failures);

                foreach (string archiveFilePath in Directory.EnumerateFiles(AppStorage.ChampionTileArchiveDirectoryPath, "dragontail-*.tgz", SearchOption.TopDirectoryOnly))
                {
                    if (TryDeleteFile(archiveFilePath, out Exception? exception))
                        deletedFileCount++;
                    else
                        failures.Add(new ChampionTileArchiveCleanupFailure(archiveFilePath, FormatException(exception)));
                }

                foreach (string temporaryArchiveFilePath in Directory.EnumerateFiles(AppStorage.ChampionTileArchiveDirectoryPath, "dragontail-*.tgz.download", SearchOption.TopDirectoryOnly))
                {
                    if (TryDeleteFile(temporaryArchiveFilePath, out Exception? exception))
                        deletedFileCount++;
                    else
                        failures.Add(new ChampionTileArchiveCleanupFailure(temporaryArchiveFilePath, FormatException(exception)));
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ChampionTileArchiveCleanupFailure(AppStorage.ChampionTileArchiveDirectoryPath, FormatException(ex)));
            }

            return new ChampionTileArchiveCleanupResult(deletedFileCount, failures);
        }

        public static void Reload()
        {
            lock (CatalogLock)
            {
                _optionsByChampionKey = null;
            }

            lock (ImageSourceLock)
            {
                ImageSourceCache.Clear();
            }

            TileCatalogChanged?.Invoke(null, EventArgs.Empty);
        }

        public static ChampionTileOption? GetSelectedOption(ChampionInfo champion, ChampSelectSettings settings)
        {
            var options = GetOptions(champion);
            if (options.Count == 0)
                return null;

            if (settings.ChampionImageFileNames.TryGetValue(champion.Id, out string? selectedFileName)
                && TryGetSafeFileName(selectedFileName, out selectedFileName))
            {
                var selectedOption = options.FirstOrDefault(option =>
                    string.Equals(option.FileName, selectedFileName, StringComparison.OrdinalIgnoreCase));

                if (selectedOption is not null)
                    return selectedOption;
            }

            return options.FirstOrDefault(option => IsDefaultTile(option.FileName)) ?? options[0];
        }

        public static ImageSource? GetSelectedImageSource(int championId, ChampSelectSettings settings)
        {
            return ChampionCatalog.TryGetById(championId, out var champion)
                ? GetSelectedOption(champion!, settings)?.ImageSource
                : null;
        }

        public static ImageSource? GetImageSource(string? fileName)
        {
            if (!TryGetSafeFileName(fileName, out string safeFileName))
                return null;

            lock (ImageSourceLock)
            {
                if (ImageSourceCache.TryGetValue(safeFileName, out var cachedImageSource))
                    return cachedImageSource;
            }

            ImageSource? imageSource = LoadImageSource(safeFileName);

            lock (ImageSourceLock)
            {
                ImageSourceCache[safeFileName] = imageSource;
            }

            return imageSource;
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
                if (!normalizedEntryName.Contains("/img/champion/tiles/", StringComparison.OrdinalIgnoreCase)
                    || !normalizedEntryName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || entry.DataStream is null)
                {
                    continue;
                }

                string fileName = Path.GetFileName(normalizedEntryName);
                if (!TryGetSafeFileName(fileName, out fileName))
                    continue;

                string destinationFilePath = Path.Combine(TileDirectoryPath, fileName);
                string temporaryTileFilePath = Path.Combine(TileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

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

        private static void SaveCacheFile(
            ChampionTileArchiveInstallResult result,
            IProgress<ChampionTileArchiveProgress>? progress)
        {
            try
            {
                AppStorage.EnsureDirectoryExists();
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
                    AppStorage.ChampionTileCacheFilePath,
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

        private static IReadOnlyDictionary<string, IReadOnlyList<ChampionTileOption>> OptionsByChampionKey
        {
            get
            {
                lock (CatalogLock)
                {
                    _optionsByChampionKey ??= LoadOptionsByChampionKey();
                    return _optionsByChampionKey;
                }
            }
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<ChampionTileOption>> LoadOptionsByChampionKey()
        {
            if (!Directory.Exists(TileDirectoryPath))
                return new Dictionary<string, IReadOnlyList<ChampionTileOption>>(StringComparer.OrdinalIgnoreCase);

            return Directory
                .EnumerateFiles(TileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    FileName = Path.GetFileName(path),
                    Prefix = GetTilePrefix(Path.GetFileNameWithoutExtension(path))
                })
                .Where(tile => !string.IsNullOrWhiteSpace(tile.FileName) && !string.IsNullOrWhiteSpace(tile.Prefix))
                .GroupBy(tile => NormalizeKey(tile.Prefix), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ChampionTileOption>)group
                        .Select(tile => new ChampionTileOption(tile.FileName, CreateDisplayName(tile.FileName)))
                        .OrderBy(option => GetTileNumber(option.FileName))
                        .ThenBy(option => option.FileName, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static ImageSource? LoadImageSource(string fileName)
        {
            string filePath = Path.Combine(TileDirectoryPath, fileName);
            if (!File.Exists(filePath))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
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

        private static bool FilesHaveSameSha256(string firstFilePath, string secondFilePath)
        {
            using var firstStream = File.OpenRead(firstFilePath);
            using var secondStream = File.OpenRead(secondFilePath);

            byte[] firstHash = SHA256.HashData(firstStream);
            byte[] secondHash = SHA256.HashData(secondStream);
            return firstHash.AsSpan().SequenceEqual(secondHash);
        }

        private static string GetChampionTileKey(string championName)
        {
            string normalizedKey = NormalizeKey(championName);
            return ChampionKeyAliases.TryGetValue(normalizedKey, out string? alias)
                ? alias
                : normalizedKey;
        }

        private static string NormalizeKey(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        private static string GetTilePrefix(string fileNameWithoutExtension)
        {
            int separatorIndex = fileNameWithoutExtension.LastIndexOf('_');
            return separatorIndex > 0
                ? fileNameWithoutExtension[..separatorIndex]
                : fileNameWithoutExtension;
        }

        private static string CreateDisplayName(string fileName)
        {
            int tileNumber = GetTileNumber(fileName);
            return tileNumber == 0
                ? $"Default ({fileName})"
                : $"Variant {tileNumber} ({fileName})";
        }

        private static bool IsDefaultTile(string fileName)
        {
            return GetTileNumber(fileName) == 0;
        }

        private static int GetTileNumber(string fileName)
        {
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            int separatorIndex = nameWithoutExtension.LastIndexOf('_');
            return separatorIndex >= 0
                && int.TryParse(nameWithoutExtension[(separatorIndex + 1)..], out int number)
                    ? number
                    : int.MaxValue;
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
