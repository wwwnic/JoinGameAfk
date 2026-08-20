using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using System.Windows.Media;
using System.Windows.Media.Imaging;

try
{
    var arguments = CommandLineArguments.Parse(args);
    if (arguments.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    string tileDirectoryPath = Path.GetFullPath(arguments.GetRequired("--tile-directory"));
    string cacheFilePath = Path.GetFullPath(arguments.GetValue("--cache-file")
        ?? Path.Combine(Path.GetDirectoryName(tileDirectoryPath) ?? tileDirectoryPath, "champion-tile-cache.json"));
    string? dataDragonVersion = arguments.GetValue("--version");
    string locale = RegionLocale.NormalizeLocale(arguments.GetValue("--locale"));
    int? resizeWidth = arguments.GetOptionalPositiveInt32("--resize-width");
    int? jpegQuality = arguments.GetOptionalRangeInt32("--jpeg-quality", 1, 100);

    Console.WriteLine("Generating bundled LoL and League Classic default champion tile seed cache.");
    Console.WriteLine($"Tile directory: {tileDirectoryPath}");
    Console.WriteLine($"Cache metadata: {cacheFilePath}");
    Console.WriteLine($"Locale: {locale}");
    if (!string.IsNullOrWhiteSpace(dataDragonVersion))
        Console.WriteLine($"Data Dragon version: {dataDragonVersion.Trim()}");
    if (resizeWidth is int targetResizeWidth)
        Console.WriteLine($"Resize downloaded seed tiles to width: {targetResizeWidth}px");
    if (jpegQuality is int targetJpegQuality)
        Console.WriteLine($"Re-encode downloaded seed tiles with JPEG quality: {targetJpegQuality}");

    ChampionDefaultTileSeedResult result = await DataDragonDefaultTileSeeder.DownloadAsync(
        dataDragonVersion,
        locale,
        tileDirectoryPath,
        new ConsoleDefaultTileProgress());

    if (result.FailedTileCount > 0)
    {
        throw new InvalidOperationException(
            $"Unable to generate a complete seed cache: {result.FailedTileCount} default champion pictures failed.");
    }

    if (resizeWidth is not null || jpegQuality is not null)
    {
        ChampionTileSeedOptimizationResult optimizationResult = ChampionTileSeedOptimizer.Optimize(
            tileDirectoryPath,
            resizeWidth,
            jpegQuality ?? ChampionTileSeedOptimizer.DefaultJpegQuality);
        if (optimizationResult.FailedFileCount > 0)
            throw new InvalidOperationException($"Unable to optimize {optimizationResult.FailedFileCount} bundled champion pictures.");

        Console.WriteLine(
            $"Champion tile seed optimization complete. Checked {optimizationResult.CheckedFileCount}; optimized {optimizationResult.OptimizedFileCount}; kept {optimizationResult.KeptFileCount}; size {FormatMegabytes(optimizationResult.BeforeBytes)} -> {FormatMegabytes(optimizationResult.AfterBytes)} ({FormatPercentReduction(optimizationResult.BeforeBytes, optimizationResult.AfterBytes)} smaller).");
    }

    var installResult = new ChampionTileArchiveInstallResult(
        result.DataDragonVersion,
        ArchiveFilePath: null,
        ArchiveSizeBytes: 0,
        result.CheckedTileCount,
        result.DownloadedTileCount,
        result.UnchangedTileCount,
        ChampionTileArchiveInstaller.GetTileFileCount(tileDirectoryPath),
        tileDirectoryPath,
        DateTime.UtcNow);
    ChampionTileArchiveInstaller.SaveCacheFile(installResult, cacheFilePath);

    if (!File.Exists(cacheFilePath))
        throw new InvalidOperationException($"Champion tile cache metadata was not written to '{cacheFilePath}'.");

    Console.WriteLine(
        $"Champion tile seed cache ready. Version: {result.DataDragonVersion}; checked {result.CheckedTileCount}; updated {result.DownloadedTileCount}; unchanged {result.UnchangedTileCount}; cached {installResult.CachedTileCount}; no Data Dragon archive downloaded.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Champion tile seed cache generation failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dotnet run --project JoinGameAfk.Tools/JoinGameAfk.Tools.ChampionTileSeeder -- --tile-directory <path> [--cache-file <path>] [--version <data-dragon-version>] [--locale <locale>] [--resize-width <pixels>] [--jpeg-quality <1-100>]

        Options:
          --tile-directory      Directory where default champion tile JPGs are written.
          --cache-file          Cache metadata JSON path. Defaults to champion-tile-cache.json next to the tile directory.
          --version             Data Dragon version to use. Defaults to the latest published version.
          --locale              Riot locale used for the champion catalog. Defaults to en_US.
          --resize-width        Re-encode downloaded seed JPGs to this pixel width, preserving aspect ratio.
          --jpeg-quality        Re-encode downloaded seed JPGs at this JPEG quality (1-100). Defaults to 100 when resizing is enabled.
        """);
}

static string FormatMegabytes(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";

static string FormatPercentReduction(long beforeBytes, long afterBytes)
{
    if (beforeBytes <= 0 || afterBytes >= beforeBytes)
        return "0.0%";

    return $"{(beforeBytes - afterBytes) / (double)beforeBytes:P1}";
}

file sealed record ChampionDefaultTileSeedProgress(
    int CheckedTileCount,
    int DownloadedTileCount,
    int UnchangedTileCount,
    int FailedTileCount,
    int TotalTileCount,
    string Message);

file sealed record ChampionDefaultTileSeedResult(
    string DataDragonVersion,
    int CheckedTileCount,
    int DownloadedTileCount,
    int UnchangedTileCount,
    int FailedTileCount);

file static class DataDragonDefaultTileSeeder
{
    private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
    private const string ChampionCatalogUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/champion.json";
    private const string ClassicChampionCatalogUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/data/{1}/mode/classic/champion.json";
    private const string ChampionTileUrlFormat = "https://ddragon.leagueoflegends.com/cdn/img/champion/tiles/{0}_0.jpg";
    private const string ClassicChampionTileUrlFormat = "https://ddragon.leagueoflegends.com/cdn/{0}/img/mode/classic/champion/{1}";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ChampionDefaultTileSeedResult> DownloadAsync(
        string? preferredDataDragonVersion,
        string locale,
        string tileDirectoryPath,
        IProgress<ChampionDefaultTileSeedProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(tileDirectoryPath);
        using var httpClient = new HttpClient { Timeout = RequestTimeout };
        string dataDragonVersion = await ResolveVersionAsync(httpClient, preferredDataDragonVersion, cancellationToken)
            .ConfigureAwait(false);
        List<DataDragonSeedChampion> champions = await FetchCatalogAsync(
                httpClient,
                dataDragonVersion,
                locale,
                cancellationToken)
            .ConfigureAwait(false);
        List<DataDragonClassicSeedChampion> classicChampions = await FetchClassicCatalogAsync(
                httpClient,
                dataDragonVersion,
                locale,
                champions,
                cancellationToken)
            .ConfigureAwait(false);

        int downloaded = 0;
        int unchanged = 0;
        int failed = 0;
        int totalTileCount = champions.Count + classicChampions.Count;
        int checkedCount = 0;
        for (int index = 0; index < champions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataDragonSeedChampion champion = champions[index];
            checkedCount++;
            try
            {
                ChampionSeedTileOutcome outcome = await DownloadTileAsync(
                        httpClient,
                        champion.Id!,
                        tileDirectoryPath,
                        CreateTileFileName(champion.Id!),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (outcome == ChampionSeedTileOutcome.Downloaded)
                    downloaded++;
                else
                    unchanged++;

                progress?.Report(new ChampionDefaultTileSeedProgress(
                    checkedCount,
                    downloaded,
                    unchanged,
                    failed,
                    totalTileCount,
                    $"Downloading LoL default champion pictures {checkedCount}/{totalTileCount}..."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                progress?.Report(new ChampionDefaultTileSeedProgress(
                    checkedCount,
                    downloaded,
                    unchanged,
                    failed,
                    totalTileCount,
                    $"Unable to download the LoL default picture for {champion.Id}: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        foreach (DataDragonClassicSeedChampion champion in classicChampions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkedCount++;
            try
            {
                ChampionSeedTileOutcome outcome = await DownloadClassicTileAsync(
                        httpClient,
                        dataDragonVersion,
                        champion.ImageFileName,
                        tileDirectoryPath,
                        CreateClassicTileFileName(champion.CanonicalChampionId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (outcome == ChampionSeedTileOutcome.Downloaded)
                    downloaded++;
                else
                    unchanged++;

                progress?.Report(new ChampionDefaultTileSeedProgress(
                    checkedCount,
                    downloaded,
                    unchanged,
                    failed,
                    totalTileCount,
                    $"Downloading League Classic default champion pictures {checkedCount}/{totalTileCount}..."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                progress?.Report(new ChampionDefaultTileSeedProgress(
                    checkedCount,
                    downloaded,
                    unchanged,
                    failed,
                    totalTileCount,
                    $"Unable to download the League Classic default picture for {champion.CanonicalChampionId}: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        return new ChampionDefaultTileSeedResult(dataDragonVersion, totalTileCount, downloaded, unchanged, failed);
    }

    private static async Task<string> ResolveVersionAsync(
        HttpClient httpClient,
        string? preferredVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preferredVersion))
            return preferredVersion.Trim();

        using HttpResponseMessage response = await httpClient.GetAsync(VersionsUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        List<string>? versions = await JsonSerializer.DeserializeAsync<List<string>>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);
        return versions?.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))?.Trim()
            ?? throw new InvalidOperationException("Riot Data Dragon returned no versions.");
    }

    private static async Task<List<DataDragonSeedChampion>> FetchCatalogAsync(
        HttpClient httpClient,
        string version,
        string locale,
        CancellationToken cancellationToken)
    {
        string catalogUrl = string.Format(CultureInfo.InvariantCulture, ChampionCatalogUrlFormat, version, locale);
        using HttpResponseMessage response = await httpClient.GetAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        DataDragonSeedCatalog? catalog = await JsonSerializer.DeserializeAsync<DataDragonSeedCatalog>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);
        List<DataDragonSeedChampion> champions = catalog?.Data.Values
            .Where(champion => !string.IsNullOrWhiteSpace(champion.Id))
            .GroupBy(champion => champion.Id!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Id = group.Key })
            .OrderBy(champion => ParseChampionKey(champion.Key))
            .ThenBy(champion => champion.Id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];
        return champions.Count > 0
            ? champions
            : throw new InvalidOperationException("Riot Data Dragon returned an empty champion catalog.");
    }

    private static async Task<List<DataDragonClassicSeedChampion>> FetchClassicCatalogAsync(
        HttpClient httpClient,
        string version,
        string locale,
        IReadOnlyList<DataDragonSeedChampion> champions,
        CancellationToken cancellationToken)
    {
        string catalogUrl = string.Format(
            CultureInfo.InvariantCulture,
            ClassicChampionCatalogUrlFormat,
            version,
            locale);
        using HttpResponseMessage response = await httpClient.GetAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        DataDragonClassicSeedCatalog? catalog = await JsonSerializer.DeserializeAsync<DataDragonClassicSeedCatalog>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<int, DataDragonSeedChampion> championsByKey = champions
            .Where(champion => ParseChampionKey(champion.Key) != int.MaxValue)
            .GroupBy(champion => ParseChampionKey(champion.Key))
            .ToDictionary(group => group.Key, group => group.First());
        var classicChampions = new List<DataDragonClassicSeedChampion>();
        IEnumerable<DataDragonClassicSeedChampion> candidates = catalog?.Data.Values
            ?? Enumerable.Empty<DataDragonClassicSeedChampion>();
        foreach (DataDragonClassicSeedChampion candidate in candidates)
        {
            int modeChampionId = ParseChampionKey(candidate.Key);
            if (!LeagueChampionId.IsClassicVariant(modeChampionId))
                continue;

            int canonicalChampionId = LeagueChampionId.ToCanonical(modeChampionId);
            if (!championsByKey.TryGetValue(canonicalChampionId, out DataDragonSeedChampion? canonicalChampion)
                || string.IsNullOrWhiteSpace(canonicalChampion.Id))
            {
                throw new InvalidOperationException(
                    $"League Classic champion {modeChampionId} has no matching LoL champion in Riot Data Dragon.");
            }

            string imageFileName = candidate.Image?.Full?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(imageFileName)
                || !string.Equals(imageFileName, Path.GetFileName(imageFileName), StringComparison.Ordinal)
                || !imageFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Riot Data Dragon returned an unsafe League Classic image name for champion {modeChampionId}.");
            }

            classicChampions.Add(candidate with
            {
                CanonicalChampionId = canonicalChampion.Id.Trim(),
                ImageFileName = imageFileName
            });
        }

        return classicChampions.Count > 0
            ? classicChampions
                .OrderBy(champion => LeagueChampionId.ToCanonical(ParseChampionKey(champion.Key)))
                .ToList()
            : throw new InvalidOperationException("Riot Data Dragon returned an empty League Classic champion catalog.");
    }

    private static async Task<ChampionSeedTileOutcome> DownloadTileAsync(
        HttpClient httpClient,
        string championId,
        string tileDirectoryPath,
        string fileName,
        CancellationToken cancellationToken)
    {
        string tileChampionId = NormalizeTileChampionId(championId);
        string tileUrl = string.Format(
            CultureInfo.InvariantCulture,
            ChampionTileUrlFormat,
            Uri.EscapeDataString(tileChampionId));
        string destinationPath = Path.Combine(tileDirectoryPath, fileName);
        string temporaryPath = Path.Combine(tileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                    tileUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream destination = File.Create(temporaryPath))
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            ValidateJpeg(temporaryPath, championId);
            if (File.Exists(destinationPath) && FilesHaveSameSha256(destinationPath, temporaryPath))
                return ChampionSeedTileOutcome.Unchanged;

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return ChampionSeedTileOutcome.Downloaded;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task<ChampionSeedTileOutcome> DownloadClassicTileAsync(
        HttpClient httpClient,
        string version,
        string imageFileName,
        string tileDirectoryPath,
        string fileName,
        CancellationToken cancellationToken)
    {
        string tileUrl = string.Format(
            CultureInfo.InvariantCulture,
            ClassicChampionTileUrlFormat,
            Uri.EscapeDataString(version),
            Uri.EscapeDataString(imageFileName));
        string destinationPath = Path.Combine(tileDirectoryPath, fileName);
        string temporaryPath = Path.Combine(tileDirectoryPath, $"{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                    tileUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            using var source = new MemoryStream(imageBytes, writable: false);
            BitmapSource bitmap = BitmapDecoder.Create(
                source,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames[0];
            cancellationToken.ThrowIfCancellationRequested();

            var encoder = new JpegBitmapEncoder { QualityLevel = ChampionTileSeedOptimizer.DefaultJpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream destination = File.Create(temporaryPath))
                encoder.Save(destination);

            ValidateJpeg(temporaryPath, imageFileName);
            if (File.Exists(destinationPath) && FilesHaveSameSha256(destinationPath, temporaryPath))
                return ChampionSeedTileOutcome.Unchanged;

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return ChampionSeedTileOutcome.Downloaded;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static string CreateTileFileName(string championId)
    {
        string fileName = $"{NormalizeTileChampionId(championId)}_0.jpg";
        string safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal)
            || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Riot Data Dragon returned an unsafe champion id '{championId}'.");
        }

        return safeFileName;
    }

    private static string CreateClassicTileFileName(string championId)
    {
        string fileName = $"{NormalizeTileChampionId(championId)}_classic.jpg";
        string safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal)
            || !safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Riot Data Dragon returned an unsafe champion id '{championId}'.");
        }

        return safeFileName;
    }

    private static string NormalizeTileChampionId(string championId)
    {
        return string.Equals(championId, "Fiddlesticks", StringComparison.OrdinalIgnoreCase)
            ? "FiddleSticks"
            : championId;
    }

    private static int ParseChampionKey(string? key) =>
        int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int championKey)
            ? championKey
            : int.MaxValue;

    private static void ValidateJpeg(string filePath, string championId)
    {
        using FileStream stream = File.OpenRead(filePath);
        if (stream.Length < 4 || stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
            throw new InvalidOperationException($"Riot Data Dragon returned an invalid JPG for {championId}.");
    }

    private static bool FilesHaveSameSha256(string firstPath, string secondPath)
    {
        using FileStream first = File.OpenRead(firstPath);
        using FileStream second = File.OpenRead(secondPath);
        return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
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

    private enum ChampionSeedTileOutcome { Downloaded, Unchanged }

    private sealed class DataDragonSeedCatalog
    {
        public Dictionary<string, DataDragonSeedChampion> Data { get; set; } = [];
    }

    private sealed record DataDragonSeedChampion
    {
        public string? Id { get; init; }
        public string? Key { get; init; }
    }

    private sealed class DataDragonClassicSeedCatalog
    {
        public Dictionary<string, DataDragonClassicSeedChampion> Data { get; set; } = [];
    }

    private sealed record DataDragonClassicSeedChampion
    {
        public string? Key { get; init; }
        public DataDragonClassicSeedImage? Image { get; init; }
        public string CanonicalChampionId { get; init; } = string.Empty;
        public string ImageFileName { get; init; } = string.Empty;
    }

    private sealed record DataDragonClassicSeedImage
    {
        public string? Full { get; init; }
    }
}

file sealed record ChampionTileSeedOptimizationResult(
    int CheckedFileCount,
    int OptimizedFileCount,
    int KeptFileCount,
    int FailedFileCount,
    long BeforeBytes,
    long AfterBytes);

file static class ChampionTileSeedOptimizer
{
    public const int DefaultJpegQuality = 100;

    public static ChampionTileSeedOptimizationResult Optimize(string tileDirectoryPath, int? resizeWidth, int jpegQuality)
    {
        if (!Directory.Exists(tileDirectoryPath))
            throw new DirectoryNotFoundException($"Champion tile directory was not found: {tileDirectoryPath}");
        if (resizeWidth is <= 0)
            throw new ArgumentOutOfRangeException(nameof(resizeWidth), "Resize width must be greater than zero.");

        jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        int checkedCount = 0;
        int optimizedCount = 0;
        int keptCount = 0;
        int failedCount = 0;
        long beforeBytes = 0;
        foreach (string filePath in Directory.EnumerateFiles(tileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            checkedCount++;
            long originalLength = new FileInfo(filePath).Length;
            beforeBytes += originalLength;
            try
            {
                if (TryOptimizeJpeg(filePath, resizeWidth, jpegQuality, originalLength))
                    optimizedCount++;
                else
                    keptCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                Console.Error.WriteLine($"Unable to optimize champion tile '{Path.GetFileName(filePath)}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        long afterBytes = Directory.EnumerateFiles(tileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly)
            .Sum(filePath => new FileInfo(filePath).Length);
        return new ChampionTileSeedOptimizationResult(
            checkedCount,
            optimizedCount,
            keptCount,
            failedCount,
            beforeBytes,
            afterBytes);
    }

    private static bool TryOptimizeJpeg(string filePath, int? resizeWidth, int jpegQuality, long originalLength)
    {
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            BitmapSource source = LoadBitmap(filePath, resizeWidth);
            var encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var output = File.Create(temporaryPath))
                encoder.Save(output);

            if (new FileInfo(temporaryPath).Length >= originalLength)
                return false;

            File.Move(temporaryPath, filePath, overwrite: true);
            return true;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static BitmapSource LoadBitmap(string filePath, int? resizeWidth)
    {
        using var input = File.OpenRead(filePath);
        BitmapSource source = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        if (resizeWidth is not int targetWidth || source.PixelWidth <= targetWidth)
            return source;

        double scale = targetWidth / (double)source.PixelWidth;
        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        resized.Freeze();
        return resized;
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
}

file sealed class ConsoleDefaultTileProgress : IProgress<ChampionDefaultTileSeedProgress>
{
    public void Report(ChampionDefaultTileSeedProgress value)
    {
        bool isFailure = value.Message.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase);
        bool isMilestone = value.CheckedTileCount == value.TotalTileCount || value.CheckedTileCount % 10 == 0;
        if (isFailure || isMilestone)
            Console.WriteLine(value.Message);
    }
}

file sealed class CommandLineArguments
{
    private readonly Dictionary<string, string> _values;

    private CommandLineArguments(Dictionary<string, string> values, bool showHelp)
    {
        _values = values;
        ShowHelp = showHelp;
    }

    public bool ShowHelp { get; }

    public static CommandLineArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool showHelp = false;
        for (int index = 0; index < args.Length; index++)
        {
            string name = args[index];
            if (string.Equals(name, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (!name.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{name}'.");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for argument '{name}'.");
            values[name] = args[++index];
        }

        return new CommandLineArguments(values, showHelp);
    }

    public string? GetValue(string name) => _values.TryGetValue(name, out string? value) ? value : null;

    public string GetRequired(string name) =>
        GetValue(name) ?? throw new ArgumentException($"Missing required argument '{name}'.");

    public int? GetOptionalPositiveInt32(string name)
    {
        string? value = GetValue(name);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, out int number) && number > 0
            ? number
            : throw new ArgumentException($"Argument '{name}' must be a positive integer.");
    }

    public int? GetOptionalRangeInt32(string name, int minValue, int maxValue)
    {
        string? value = GetValue(name);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, out int number) && number >= minValue && number <= maxValue
            ? number
            : throw new ArgumentException($"Argument '{name}' must be an integer from {minValue} to {maxValue}.");
    }
}
