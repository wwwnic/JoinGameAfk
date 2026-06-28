using System.Text.Json;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;

try
{
    var arguments = CommandLineArguments.Parse(args);
    if (arguments.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    string catalogFilePath = Path.GetFullPath(arguments.GetRequired("--catalog-file"));
    string platformId = RegionLocale.NormalizePlatformId(arguments.GetValue("--platform"));
    string locale = RegionLocale.NormalizeLocale(arguments.GetValue("--locale"));
    string? dataDragonVersion = arguments.GetValue("--version");

    Console.WriteLine("Updating bundled champion catalog.");
    Console.WriteLine($"Catalog file: {catalogFilePath}");
    Console.WriteLine($"Platform: {platformId}");
    Console.WriteLine($"Locale: {locale}");
    if (!string.IsNullOrWhiteSpace(dataDragonVersion))
        Console.WriteLine($"Data Dragon version: {dataDragonVersion.Trim()}");

    var existingChampionKeys = LoadChampionKeys(catalogFilePath);
    var remoteService = new DataDragonChampionCatalogService(() => locale, () => platformId);
    ChampionCatalogRemoteData remoteCatalog = string.IsNullOrWhiteSpace(dataDragonVersion)
        ? await remoteService.FetchLatestChampionCatalogAsync()
        : await remoteService.FetchChampionCatalogAsync(dataDragonVersion);

    var result = ChampionCatalog.RefreshFileFromDataDragon(remoteCatalog, catalogFilePath);
    var updatedChampionKeys = LoadChampionKeys(catalogFilePath);
    int newChampionCount = updatedChampionKeys.Except(existingChampionKeys).Count();

    Console.WriteLine(
        $"Champion catalog ready. Version: {result.DataDragonVersion}; locale: {result.Locale}; champions: {result.ChampionCount}; new champions: {newChampionCount}.");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Champion catalog update failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dotnet run --project JoinGameAfk.Tools/JoinGameAfk.Tools.ChampionCatalogSeeder -- --catalog-file <path> [--version <data-dragon-version>] [--platform <platform-id>] [--locale <locale>]

        Options:
          --catalog-file        champions.json path to write.
          --version             Data Dragon version to install. Defaults to the latest version for the selected platform.
          --platform            League platform used for latest-version checks. Defaults to GLOBAL.
          --locale              Riot locale used for champion names. Defaults to en_US.
        """);
}

static HashSet<int> LoadChampionKeys(string catalogFilePath)
{
    if (!File.Exists(catalogFilePath))
        return [];

    using var document = JsonDocument.Parse(File.ReadAllText(catalogFilePath));
    JsonElement championsElement = document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty("Champions", out var championsProperty)
            ? championsProperty
            : document.RootElement;

    if (championsElement.ValueKind != JsonValueKind.Array)
        return [];

    var championKeys = new HashSet<int>();
    foreach (JsonElement championElement in championsElement.EnumerateArray())
    {
        if (championElement.ValueKind != JsonValueKind.Object)
            continue;

        if (championElement.TryGetProperty("Key", out var keyElement)
            && keyElement.ValueKind == JsonValueKind.Number
            && keyElement.TryGetInt32(out int key)
            && key > 0)
        {
            championKeys.Add(key);
        }
    }

    return championKeys;
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
