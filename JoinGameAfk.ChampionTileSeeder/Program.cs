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

    var installOptions = new ChampionTileArchiveInstallOptions(
        tileDirectoryPath,
        archiveDirectoryPath,
        cacheFilePath);

    Console.WriteLine("Generating bundled champion tile seed cache.");
    Console.WriteLine($"Tile directory: {tileDirectoryPath}");
    Console.WriteLine($"Cache metadata: {cacheFilePath}");
    Console.WriteLine($"Archive directory: {archiveDirectoryPath}");

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
          dotnet run --project JoinGameAfk.ChampionTileSeeder -- --tile-directory <path> [--cache-file <path>] [--archive-directory <path>] [--version <data-dragon-version>]

        Options:
          --tile-directory      Directory where extracted champion tile JPGs are written.
          --cache-file          Cache metadata JSON path. Defaults to champion-tile-cache.json next to the tile directory.
          --archive-directory   Temporary Data Dragon archive directory. Defaults to the OS temp folder.
          --version             Data Dragon version to install. Defaults to the latest version.
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

    public string? GetValue(string name)
    {
        return _values.TryGetValue(name, out string? value)
            ? value
            : null;
    }

    public string GetRequired(string name)
    {
        return GetValue(name) ?? throw new ArgumentException($"Missing required argument '{name}'.");
    }
}
