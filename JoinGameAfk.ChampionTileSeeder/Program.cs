using JoinGameAfk.Services;

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
    Console.WriteLine($"Keep archive after extraction: {keepArchive}");
    Console.WriteLine($"Reuse existing archive before download: {reuseArchive}");

    var progress = new ConsoleArchiveProgress();
    ChampionTileArchiveInstallResult result = string.IsNullOrWhiteSpace(dataDragonVersion)
        ? await ChampionTileArchiveInstaller.InstallLatestDataDragonArchiveAsync(installOptions, progress)
        : await ChampionTileArchiveInstaller.InstallDataDragonArchiveAsync(dataDragonVersion, installOptions, progress);

    if (!File.Exists(cacheFilePath))
        throw new InvalidOperationException($"Champion tile cache metadata was not written to '{cacheFilePath}'.");

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
          dotnet run --project JoinGameAfk.ChampionTileSeeder -- --tile-directory <path> [--cache-file <path>] [--archive-directory <path>] [--version <data-dragon-version>] [--max-tile-index <number>] [--keep-archive] [--reuse-archive]

        Options:
          --tile-directory      Directory where extracted champion tile JPGs are written.
          --cache-file          Cache metadata JSON path. Defaults to champion-tile-cache.json next to the tile directory.
          --archive-directory   Temporary Data Dragon archive directory. Defaults to the OS temp folder.
          --version             Data Dragon version to install. Defaults to the latest version.
          --max-tile-index      Highest champion tile index to extract. Defaults to all tiles.
          --keep-archive        Leave the downloaded Data Dragon archive in place after extraction.
          --reuse-archive       Reuse an existing archive from the archive directory instead of deleting it before download.
        """);
}

static string FormatMegabytes(long bytes)
{
    return $"{bytes / 1024d / 1024d:0.0} MB";
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
}
