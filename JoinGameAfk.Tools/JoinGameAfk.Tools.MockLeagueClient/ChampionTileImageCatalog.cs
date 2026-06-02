using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JoinGameAfk.Constant;

namespace JoinGameAfk.Tools.MockLeagueClient;

internal static class ChampionTileImageCatalog
{
    private const int ChampionTileDecodePixelWidth = 96;

    private static readonly JsonSerializerOptions SelectionSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, string> ChampionKeyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NUNUWILLUMP"] = "NUNU",
            ["RENATAGLASC"] = "RENATA",
            ["WUKONG"] = "MONKEYKING"
        };

    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? _tileFilesByChampionKey;
    private static IReadOnlyDictionary<int, string>? _selectedTileFiles;
    private static readonly Dictionary<string, ImageSource?> ImageSourceCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetSelectedImageSource(int championId, string championName)
    {
        if (championId <= 0)
            return null;

        if (SelectedTileFiles.TryGetValue(championId, out string? selectedFileName)
            && TryGetSafeFileName(selectedFileName, out selectedFileName)
            && File.Exists(Path.Combine(AppStorage.ChampionTileDirectoryPath, selectedFileName)))
        {
            return GetImageSource(selectedFileName);
        }

        string championKey = GetChampionTileKey(championName);
        return TileFilesByChampionKey.TryGetValue(championKey, out var tileFiles)
            ? GetImageSource(tileFiles.FirstOrDefault())
            : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> TileFilesByChampionKey
    {
        get
        {
            lock (SyncRoot)
            {
                _tileFilesByChampionKey ??= LoadTileFilesByChampionKey();
                return _tileFilesByChampionKey;
            }
        }
    }

    private static IReadOnlyDictionary<int, string> SelectedTileFiles
    {
        get
        {
            lock (SyncRoot)
            {
                _selectedTileFiles ??= LoadSelectedTileFiles();
                return _selectedTileFiles;
            }
        }
    }

    private static ImageSource? GetImageSource(string? fileName)
    {
        if (!TryGetSafeFileName(fileName, out string safeFileName))
            return null;

        lock (SyncRoot)
        {
            if (ImageSourceCache.TryGetValue(safeFileName, out var cachedImageSource))
                return cachedImageSource;
        }

        ImageSource? imageSource = LoadImageSource(safeFileName);

        lock (SyncRoot)
        {
            ImageSourceCache[safeFileName] = imageSource;
        }

        return imageSource;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadTileFilesByChampionKey()
    {
        if (!Directory.Exists(AppStorage.ChampionTileDirectoryPath))
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        return Directory
            .EnumerateFiles(AppStorage.ChampionTileDirectoryPath, "*.jpg", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                FileName = Path.GetFileName(path),
                Prefix = GetTilePrefix(Path.GetFileNameWithoutExtension(path))
            })
            .Where(tile => !string.IsNullOrWhiteSpace(tile.FileName) && !string.IsNullOrWhiteSpace(tile.Prefix))
            .GroupBy(tile => NormalizeKey(tile.Prefix), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(tile => GetTileNumber(tile.FileName))
                    .ThenBy(tile => tile.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(tile => tile.FileName)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<int, string> LoadSelectedTileFiles()
    {
        if (!File.Exists(AppStorage.ChampionImageSelectionFilePath))
            return new Dictionary<int, string>();

        try
        {
            string json = File.ReadAllText(AppStorage.ChampionImageSelectionFilePath);
            var file = JsonSerializer.Deserialize<ChampionImageSelectionFile>(json, SelectionSerializerOptions);
            return file?.ChampionImageFileNames?
                .Where(selection => selection.Key > 0 && TryGetSafeFileName(selection.Value, out _))
                .ToDictionary(
                    selection => selection.Key,
                    selection =>
                    {
                        TryGetSafeFileName(selection.Value, out string safeFileName);
                        return safeFileName;
                    })
                ?? new Dictionary<int, string>();
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private static ImageSource? LoadImageSource(string fileName)
    {
        string filePath = Path.Combine(AppStorage.ChampionTileDirectoryPath, fileName);
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

    private static int GetTileNumber(string fileName)
    {
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        int separatorIndex = nameWithoutExtension.LastIndexOf('_');
        return separatorIndex >= 0
               && int.TryParse(nameWithoutExtension[(separatorIndex + 1)..], out int number)
            ? number
            : int.MaxValue;
    }

    private sealed class ChampionImageSelectionFile
    {
        public Dictionary<int, string> ChampionImageFileNames { get; set; } = [];
    }
}
