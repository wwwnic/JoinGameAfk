using System.IO;
using System.Reflection;
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

    public sealed record ChampionTileSeedCacheResult(
        bool Installed,
        int ImportedCount,
        string? DataDragonVersion,
        string CacheDirectory);

    public static class ChampionTileCatalog
    {
        private const int ChampionTileDecodePixelWidth = 96;
        private const string BundledChampionTileResourcePrefix = "JoinGameAfk.Assets.ChampionTiles.";
        private const string BundledChampionTileCacheResourceName = "JoinGameAfk.Assets.champion-tile-cache.json";

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
            return ChampionTileArchiveInstaller.GetTileFileCount(TileDirectoryPath);
        }

        public static ChampionTileCacheSyncInfo GetCacheSyncInfo()
        {
            return ChampionTileArchiveInstaller.GetCacheSyncInfo(TileDirectoryPath, AppStorage.ChampionTileCacheFilePath);
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
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true)
        {
            var result = await ChampionTileArchiveInstaller.InstallLatestDataDragonArchiveAsync(
                    CreateArchiveInstallOptions(optimizeForLocalCache),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            Reload();
            return result;
        }

        public static async Task<ChampionTileArchiveInstallResult> InstallDataDragonArchiveAsync(
            string dataDragonVersion,
            IProgress<ChampionTileArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true)
        {
            var result = await ChampionTileArchiveInstaller.InstallDataDragonArchiveAsync(
                    dataDragonVersion,
                    CreateArchiveInstallOptions(optimizeForLocalCache),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            Reload();
            return result;
        }

        public static async Task<ChampionTileDownloadResult> DownloadAllImagesForChampionAsync(
            ChampionInfo champion,
            IProgress<ChampionTileDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool optimizeForLocalCache = true)
        {
            var syncInfo = GetCacheSyncInfo();
            var result = await DataDragonChampionTileDownloadService.DownloadChampionTilesAsync(
                    champion,
                    syncInfo.DataDragonVersion,
                    TileDirectoryPath,
                    progress,
                    cancellationToken,
                    optimizeForLocalCache)
                .ConfigureAwait(false);
            Reload();
            return result;
        }

        public static ChampionTileArchiveCleanupResult DeleteDownloadedArchives()
        {
            return ChampionTileArchiveInstaller.DeleteDownloadedArchives(AppStorage.ChampionTileArchiveDirectoryPath);
        }

        private static ChampionTileArchiveInstallOptions CreateArchiveInstallOptions(bool optimizeForLocalCache)
        {
            return ChampionTileArchiveInstallOptions.Default with
            {
                OptimizeTileFile = optimizeForLocalCache
                    ? ChampionTileCacheImageOptimizer.TryOptimizeJpegInPlace
                    : null
            };
        }

        public static ChampionTileSeedCacheResult InstallBundledSeedCacheIfNeeded()
        {
            var syncInfo = GetCacheSyncInfo();
            if (syncInfo.CachedTileCount > 0)
                return new ChampionTileSeedCacheResult(false, 0, syncInfo.DataDragonVersion, TileDirectoryPath);

            Assembly assembly = typeof(ChampionTileCatalog).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(BundledChampionTileResourcePrefix, StringComparison.Ordinal)
                    && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (resourceNames.Count == 0)
                return new ChampionTileSeedCacheResult(false, 0, null, TileDirectoryPath);

            AppStorage.EnsureChampionTileDirectoryExists();

            int importedCount = 0;
            foreach (string resourceName in resourceNames)
            {
                string fileName = resourceName[BundledChampionTileResourcePrefix.Length..];
                if (!TryGetSafeFileName(fileName, out fileName))
                    continue;

                using Stream? sourceStream = assembly.GetManifestResourceStream(resourceName);
                if (sourceStream is null)
                    continue;

                string destinationFilePath = Path.Combine(TileDirectoryPath, fileName);
                string temporaryTileFilePath = Path.Combine(TileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

                try
                {
                    using (var outputStream = File.Create(temporaryTileFilePath))
                    {
                        sourceStream.CopyTo(outputStream);
                    }

                    File.Move(temporaryTileFilePath, destinationFilePath, overwrite: true);
                    importedCount++;
                }
                finally
                {
                    TryDeleteFile(temporaryTileFilePath);
                }
            }

            if (importedCount <= 0)
                return new ChampionTileSeedCacheResult(false, 0, null, TileDirectoryPath);

            var seedMetadata = LoadBundledSeedCacheMetadata(assembly);
            var installResult = new ChampionTileArchiveInstallResult(
                seedMetadata?.DataDragonVersion ?? "bundled",
                null,
                seedMetadata?.ArchiveSizeBytes ?? 0,
                importedCount,
                importedCount,
                0,
                GetTileFileCount(),
                TileDirectoryPath,
                seedMetadata?.LastSyncedAtUtc ?? DateTime.UtcNow);

            ChampionTileArchiveInstaller.SaveCacheFile(installResult, AppStorage.ChampionTileCacheFilePath);
            Reload();

            return new ChampionTileSeedCacheResult(true, importedCount, installResult.DataDragonVersion, TileDirectoryPath);
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

        public static ChampionTileOption? GetDefaultOption(ChampionInfo champion)
        {
            var options = GetOptions(champion);
            if (options.Count == 0)
                return null;

            return options.FirstOrDefault(option => IsDefaultTile(option.FileName)) ?? options[0];
        }

        public static ChampionTileOption? GetSelectedOption(ChampionInfo champion)
        {
            return GetSelectedOption(champion, ChampionImageSelectionStore.Selections);
        }

        public static ChampionTileOption? GetSelectedOption(ChampionInfo champion, IReadOnlyDictionary<int, string> selections)
        {
            var options = GetOptions(champion);
            if (options.Count == 0)
                return null;

            if (selections.TryGetValue(champion.Id, out string? selectedFileName)
                && TryGetSafeFileName(selectedFileName, out selectedFileName))
            {
                var selectedOption = options.FirstOrDefault(option =>
                    string.Equals(option.FileName, selectedFileName, StringComparison.OrdinalIgnoreCase));

                if (selectedOption is not null)
                    return selectedOption;
            }

            return GetDefaultOption(champion);
        }

        public static ImageSource? GetSelectedImageSource(int championId)
        {
            return ChampionCatalog.TryGetById(championId, out var champion)
                ? GetSelectedOption(champion!)?.ImageSource
                : null;
        }

        public static Task<int> PreloadSelectedImageSourcesAsync(
            IEnumerable<ChampionInfo> champions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(champions);

            var championSnapshot = champions.ToList();
            return Task.Run(() => PreloadSelectedImageSources(championSnapshot, cancellationToken), cancellationToken);
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

        private static int PreloadSelectedImageSources(
            IEnumerable<ChampionInfo> champions,
            CancellationToken cancellationToken)
        {
            int loadedCount = 0;
            foreach (var champion in champions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GetSelectedOption(champion)?.ImageSource is not null)
                    loadedCount++;
            }

            return loadedCount;
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
                        .OrderBy(option => GetTileNumber(option.FileName))
                        .ThenBy(option => option.FileName, StringComparer.OrdinalIgnoreCase)
                        .Select((tile, index) => new ChampionTileOption(tile.FileName, CreateDisplayName(tile.FileName, index)))
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
                bitmap.DecodePixelWidth = ChampionTileDecodePixelWidth;
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

        private static BundledChampionTileCacheFile? LoadBundledSeedCacheMetadata(Assembly assembly)
        {
            using Stream? stream = assembly.GetManifestResourceStream(BundledChampionTileCacheResourceName);
            if (stream is null)
                return null;

            return JsonSerializer.Deserialize<BundledChampionTileCacheFile>(stream, CacheSerializerOptions);
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

        private static string CreateDisplayName(string fileName, int sortedIndex)
        {
            int tileNumber = GetTileNumber(fileName);
            return tileNumber == 0
                ? $"Default ({fileName})"
                : $"Variant {Math.Max(1, sortedIndex)} ({fileName})";
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

        private sealed class BundledChampionTileCacheFile
        {
            public int Version { get; set; } = ChampionTileArchiveInstaller.ChampionTileCacheFileVersion;

            public string? DataDragonVersion { get; set; }

            public int CachedTileCount { get; set; }

            public string? ArchiveFilePath { get; set; }

            public long ArchiveSizeBytes { get; set; }

            public DateTime? LastSyncedAtUtc { get; set; }
        }
    }
}
