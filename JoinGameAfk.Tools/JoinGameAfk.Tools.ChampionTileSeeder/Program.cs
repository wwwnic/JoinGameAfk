using System.IO;
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
    string archiveDirectoryPath = Path.GetFullPath(arguments.GetValue("--archive-directory")
        ?? Path.Combine(Path.GetTempPath(), "JoinGameAfkChampionTileArchives"));
    string? dataDragonVersion = arguments.GetValue("--version");
    int? maxTileIndex = arguments.GetOptionalNonNegativeInt32("--max-tile-index");
    int? resizeWidth = arguments.GetOptionalPositiveInt32("--resize-width");
    int? jpegQuality = arguments.GetOptionalRangeInt32("--jpeg-quality", 1, 100);
    bool keepArchive = arguments.HasFlag("--keep-archive");
    bool reuseArchive = arguments.HasFlag("--reuse-archive");

    var installOptions = new ChampionTileArchiveInstallOptions(
        tileDirectoryPath,
        archiveDirectoryPath,
        cacheFilePath,
        maxTileIndex,
        DeleteArchiveAfterExtraction: !keepArchive,
        DeleteExistingArchivesBeforeDownload: !reuseArchive);

    Console.WriteLine("Generating bundled champion tile seed cache.");
    Console.WriteLine($"Tile directory: {tileDirectoryPath}");
    Console.WriteLine($"Cache metadata: {cacheFilePath}");
    Console.WriteLine($"Archive directory: {archiveDirectoryPath}");
    if (maxTileIndex is int maximumTileIndex)
        Console.WriteLine($"Maximum bundled tile index: {maximumTileIndex}");
    if (resizeWidth is int targetResizeWidth)
        Console.WriteLine($"Resize extracted seed tiles to width: {targetResizeWidth}px");
    if (jpegQuality is int targetJpegQuality)
        Console.WriteLine($"Re-encode extracted seed tiles with JPEG quality: {targetJpegQuality}");
    Console.WriteLine($"Keep archive after extraction: {keepArchive}");
    Console.WriteLine($"Reuse existing archive before download: {reuseArchive}");

    var progress = new ConsoleArchiveProgress();
    ChampionTileArchiveInstallResult result = string.IsNullOrWhiteSpace(dataDragonVersion)
        ? await ChampionTileArchiveInstaller.InstallLatestDataDragonArchiveAsync(installOptions, progress)
        : await ChampionTileArchiveInstaller.InstallDataDragonArchiveAsync(dataDragonVersion, installOptions, progress);

    if (!File.Exists(cacheFilePath))
        throw new InvalidOperationException($"Champion tile cache metadata was not written to '{cacheFilePath}'.");

    if (resizeWidth is not null || jpegQuality is not null)
    {
        var optimizationResult = ChampionTileSeedOptimizer.Optimize(
            tileDirectoryPath,
            resizeWidth,
            jpegQuality ?? ChampionTileSeedOptimizer.DefaultJpegQuality);

        Console.WriteLine(
            $"Champion tile seed optimization complete. Checked {optimizationResult.CheckedFileCount}; optimized {optimizationResult.OptimizedFileCount}; kept {optimizationResult.KeptFileCount}; failed {optimizationResult.FailedFileCount}; size {FormatMegabytes(optimizationResult.BeforeBytes)} -> {FormatMegabytes(optimizationResult.AfterBytes)} ({FormatPercentReduction(optimizationResult.BeforeBytes, optimizationResult.AfterBytes)} smaller).");
    }

    Console.WriteLine(
        $"Champion tile seed cache ready. Version: {result.DataDragonVersion}; checked {result.CheckedTileCount}; updated {result.UpdatedTileCount}; unchanged {result.UnchangedTileCount}; cached {result.CachedTileCount}; archive {FormatMegabytes(result.ArchiveSizeBytes)}.");

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
          dotnet run --project JoinGameAfk.Tools/JoinGameAfk.Tools.ChampionTileSeeder -- --tile-directory <path> [--cache-file <path>] [--archive-directory <path>] [--version <data-dragon-version>] [--max-tile-index <number>] [--keep-archive] [--reuse-archive]

        Options:
          --tile-directory      Directory where extracted champion tile JPGs are written.
          --cache-file          Cache metadata JSON path. Defaults to champion-tile-cache.json next to the tile directory.
          --archive-directory   Temporary Data Dragon archive directory. Defaults to the OS temp folder.
          --version             Data Dragon version to install. Defaults to the latest version.
          --max-tile-index      Highest champion tile index to extract. Defaults to all tiles.
          --resize-width        Re-encode extracted seed JPGs to this pixel width, preserving aspect ratio.
          --jpeg-quality        Re-encode extracted seed JPGs at this JPEG quality (1-100). Defaults to 100 when resizing is enabled.
          --keep-archive        Leave the downloaded Data Dragon archive in place after extraction.
          --reuse-archive       Reuse an existing archive from the archive directory instead of deleting it before download.
        """);
}

static string FormatMegabytes(long bytes)
{
    return $"{bytes / 1024d / 1024d:0.0} MB";
}

static string FormatPercentReduction(long beforeBytes, long afterBytes)
{
    if (beforeBytes <= 0 || afterBytes >= beforeBytes)
        return "0.0%";

    double reduction = (beforeBytes - afterBytes) / (double)beforeBytes;
    return $"{reduction:P1}";
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

    public static ChampionTileSeedOptimizationResult Optimize(
        string tileDirectoryPath,
        int? resizeWidth,
        int jpegQuality)
    {
        if (!Directory.Exists(tileDirectoryPath))
            throw new DirectoryNotFoundException($"Champion tile directory was not found: {tileDirectoryPath}");

        if (resizeWidth is <= 0)
            throw new ArgumentOutOfRangeException(nameof(resizeWidth), "Resize width must be greater than zero.");

        jpegQuality = Math.Clamp(jpegQuality, 1, 100);

        int checkedFileCount = 0;
        int optimizedFileCount = 0;
        int keptFileCount = 0;
        int failedFileCount = 0;
        long beforeBytes = 0;

        foreach (string filePath in Directory.EnumerateFiles(tileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            checkedFileCount++;
            long originalLength = new FileInfo(filePath).Length;
            beforeBytes += originalLength;

            try
            {
                if (TryOptimizeJpeg(filePath, resizeWidth, jpegQuality, originalLength))
                    optimizedFileCount++;
                else
                    keptFileCount++;
            }
            catch (Exception ex)
            {
                failedFileCount++;
                Console.Error.WriteLine($"Unable to optimize champion tile '{Path.GetFileName(filePath)}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        long afterBytes = Directory.EnumerateFiles(tileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly)
            .Sum(filePath => new FileInfo(filePath).Length);

        return new ChampionTileSeedOptimizationResult(
            checkedFileCount,
            optimizedFileCount,
            keptFileCount,
            failedFileCount,
            beforeBytes,
            afterBytes);
    }

    private static bool TryOptimizeJpeg(
        string filePath,
        int? resizeWidth,
        int jpegQuality,
        long originalLength)
    {
        string temporaryFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            BitmapSource source = LoadBitmap(filePath, resizeWidth);
            SaveJpeg(source, temporaryFilePath, jpegQuality);

            long optimizedLength = new FileInfo(temporaryFilePath).Length;
            if (optimizedLength >= originalLength)
                return false;

            File.Move(temporaryFilePath, filePath, overwrite: true);
            return true;
        }
        finally
        {
            TryDeleteFile(temporaryFilePath);
        }
    }

    private static BitmapSource LoadBitmap(string filePath, int? resizeWidth)
    {
        using var input = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        BitmapSource source = decoder.Frames[0];
        if (resizeWidth is not int targetWidth || source.PixelWidth <= targetWidth)
            return source;

        double scale = targetWidth / (double)source.PixelWidth;
        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        resized.Freeze();
        return resized;
    }

    private static void SaveJpeg(BitmapSource source, string filePath, int jpegQuality)
    {
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = jpegQuality
        };

        encoder.Frames.Add(BitmapFrame.Create(source));

        using var output = File.Create(filePath);
        encoder.Save(output);
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

file sealed class ConsoleArchiveProgress : IProgress<ChampionTileArchiveProgress>
{
    private const long DownloadLogIntervalBytes = 50L * 1024L * 1024L;

    private string? _lastMessage;
    private long _lastDownloadLogBytes;

    public void Report(ChampionTileArchiveProgress value)
    {
        if (string.IsNullOrWhiteSpace(value.Message))
            return;

        bool isDownloadProgress = value.Message.StartsWith("Downloading Data Dragon archive:", StringComparison.OrdinalIgnoreCase);
        if (isDownloadProgress
            && value.TotalBytes is not null
            && value.BytesCompleted < value.TotalBytes
            && value.BytesCompleted - _lastDownloadLogBytes < DownloadLogIntervalBytes)
        {
            return;
        }

        if (isDownloadProgress)
            _lastDownloadLogBytes = value.BytesCompleted;

        if (string.Equals(value.Message, _lastMessage, StringComparison.Ordinal))
            return;

        _lastMessage = value.Message;
        Console.WriteLine(value.Message);
    }
}

file sealed class CommandLineArguments
{
    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _flags;

    private CommandLineArguments(Dictionary<string, string> values, HashSet<string> flags, bool showHelp)
    {
        _values = values;
        _flags = flags;
        ShowHelp = showHelp;
    }

    public bool ShowHelp { get; }

    public static CommandLineArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            if (string.Equals(name, "--keep-archive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "--reuse-archive", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add(name);
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for argument '{name}'.");

            values[name] = args[++index];
        }

        return new CommandLineArguments(values, flags, showHelp);
    }

    public string? GetValue(string name)
    {
        return _values.TryGetValue(name, out string? value)
            ? value
            : null;
    }

    public bool HasFlag(string name)
    {
        return _flags.Contains(name);
    }

    public string GetRequired(string name)
    {
        return GetValue(name) ?? throw new ArgumentException($"Missing required argument '{name}'.");
    }

    public int? GetOptionalNonNegativeInt32(string name)
    {
        string? value = GetValue(name);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!int.TryParse(value, out int number) || number < 0)
            throw new ArgumentException($"Argument '{name}' must be a non-negative integer.");

        return number;
    }

    public int? GetOptionalPositiveInt32(string name)
    {
        string? value = GetValue(name);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!int.TryParse(value, out int number) || number <= 0)
            throw new ArgumentException($"Argument '{name}' must be a positive integer.");

        return number;
    }

    public int? GetOptionalRangeInt32(string name, int minValue, int maxValue)
    {
        string? value = GetValue(name);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!int.TryParse(value, out int number) || number < minValue || number > maxValue)
            throw new ArgumentException($"Argument '{name}' must be an integer from {minValue} to {maxValue}.");

        return number;
    }
}
