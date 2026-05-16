using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
    }

    public sealed record ChampionTileImportResult(int ImportedCount, int SkippedCount, int FailedCount, string SourceDirectory, string CacheDirectory);

    public sealed record ChampionTileArchiveProgress(long BytesCompleted, long? TotalBytes, string Message);

    public sealed record ChampionTileArchiveInstallResult(
        string DataDragonVersion,
        string ArchiveFilePath,
        long ArchiveSizeBytes,
        int ExtractedTileCount,
        int CachedTileCount,
        string CacheDirectory,
        DateTime LastSyncedAtUtc);

    public sealed record ChampionTileCacheSyncInfo(
        string? DataDragonVersion,
        int CachedTileCount,
        string CacheDirectory,
        string? ArchiveFilePath,
        long ArchiveSizeBytes,
        DateTime? LastSyncedAtUtc);

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

                long archiveSizeBytes = !string.IsNullOrWhiteSpace(cacheFile.ArchiveFilePath)
                    && File.Exists(cacheFile.ArchiveFilePath)
                        ? new FileInfo(cacheFile.ArchiveFilePath).Length
                        : cacheFile.ArchiveSizeBytes;

                return new ChampionTileCacheSyncInfo(
                    cacheFile.DataDragonVersion,
                    GetTileFileCount(),
                    TileDirectoryPath,
                    cacheFile.ArchiveFilePath,
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
            AppStorage.EnsureChampionTileDirectoryExists();
            AppStorage.EnsureChampionTileArchiveDirectoryExists();

            progress?.Report(new ChampionTileArchiveProgress(0, null, "Checking latest Riot Data Dragon version..."));
            string dataDragonVersion = await FetchLatestVersionAsync(cancellationToken).ConfigureAwait(false);
            string archiveFilePath = Path.Combine(AppStorage.ChampionTileArchiveDirectoryPath, $"dragontail-{dataDragonVersion}.tgz");

            long archiveSizeBytes = await EnsureArchiveDownloadedAsync(
                    dataDragonVersion,
                    archiveFilePath,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new ChampionTileArchiveProgress(0, null, "Extracting champion pictures from local Data Dragon archive..."));
            int extractedTileCount = ExtractChampionTilesFromTarGz(archiveFilePath, cancellationToken);

            DateTime lastSyncedAtUtc = DateTime.UtcNow;
            var result = new ChampionTileArchiveInstallResult(
                dataDragonVersion,
                archiveFilePath,
                archiveSizeBytes,
                extractedTileCount,
                GetTileFileCount(),
                TileDirectoryPath,
                lastSyncedAtUtc);

            SaveCacheFile(result);
            Reload();

            return result;
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
                    $"Using cached Data Dragon archive {Path.GetFileName(archiveFilePath)} ({FormatMegabytes(existingSize)})."));
                return existingSize;
            }

            string archiveUrl = string.Format(CultureInfo.InvariantCulture, DragontailArchiveUrlFormat, dataDragonVersion);
            string temporaryArchiveFilePath = $"{archiveFilePath}.download";

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

        private static int ExtractChampionTilesFromTarGz(string archiveFilePath, CancellationToken cancellationToken)
        {
            int extractedTileCount = 0;

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
                using var outputStream = File.Create(destinationFilePath);
                entry.DataStream.CopyTo(outputStream);
                extractedTileCount++;
            }

            return extractedTileCount;
        }

        private static void SaveCacheFile(ChampionTileArchiveInstallResult result)
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
            catch
            {
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
