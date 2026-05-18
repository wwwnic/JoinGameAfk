using System.IO;
using System.Text.Json;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;

namespace JoinGameAfk.Services
{
    public static class ChampionImageSelectionStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static readonly object SyncRoot = new();
        private static Dictionary<int, string>? _selections;

        public static event EventHandler? SelectionsChanged;

        public static IReadOnlyDictionary<int, string> Selections
        {
            get
            {
                lock (SyncRoot)
                {
                    EnsureLoaded();
                    return new Dictionary<int, string>(_selections!);
                }
            }
        }

        public static bool HasSelection(int championId)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();
                return _selections!.ContainsKey(championId);
            }
        }

        public static string? GetSelection(int championId)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();
                return _selections!.TryGetValue(championId, out string? fileName)
                    ? fileName
                    : null;
            }
        }

        public static void SetSelection(int championId, string fileName)
        {
            if (!IsValidChampionId(championId) || !TryGetSafeFileName(fileName, out string safeFileName))
                return;

            bool changed;
            lock (SyncRoot)
            {
                EnsureLoaded();
                changed = !_selections!.TryGetValue(championId, out string? existingFileName)
                    || !string.Equals(existingFileName, safeFileName, StringComparison.OrdinalIgnoreCase);

                if (!changed)
                    return;

                _selections![championId] = safeFileName;
                SaveLoadedSelections();
            }

            RaiseSelectionsChanged();
        }

        public static void ClearSelection(int championId)
        {
            bool changed;
            lock (SyncRoot)
            {
                EnsureLoaded();
                changed = _selections!.Remove(championId);
                if (!changed)
                    return;

                SaveLoadedSelections();
            }

            RaiseSelectionsChanged();
        }

        public static void Reload()
        {
            lock (SyncRoot)
            {
                _selections = null;
                EnsureLoaded();
            }

            RaiseSelectionsChanged();
        }

        private static void EnsureLoaded()
        {
            if (_selections is not null)
                return;

            _selections = LoadSelections();
        }

        private static Dictionary<int, string> LoadSelections()
        {
            if (!File.Exists(AppStorage.ChampionImageSelectionFilePath))
            {
                var migratedSelections = LoadLegacySelections();
                SaveSelections(migratedSelections);
                return migratedSelections;
            }

            try
            {
                string json = File.ReadAllText(AppStorage.ChampionImageSelectionFilePath);
                var file = JsonSerializer.Deserialize<ChampionImageSelectionFile>(json, SerializerOptions);
                return NormalizeSelections(file?.ChampionImageFileNames);
            }
            catch
            {
                return [];
            }
        }

        private static Dictionary<int, string> LoadLegacySelections()
        {
            try
            {
                if (!File.Exists(AppStorage.SettingsFilePath))
                    return [];

                string json = File.ReadAllText(AppStorage.SettingsFilePath);
                var legacyFile = JsonSerializer.Deserialize<LegacySettingsFile>(json, SerializerOptions);
                return NormalizeSelections(legacyFile?.ChampionImageFileNames);
            }
            catch
            {
                return [];
            }
        }

        private static void SaveLoadedSelections()
        {
            SaveSelections(_selections ?? []);
        }

        private static void SaveSelections(IReadOnlyDictionary<int, string> selections)
        {
            AppStorage.EnsureDirectoryExists();
            var file = new ChampionImageSelectionFile
            {
                Version = AppStorage.ChampionImageSelectionFileVersion,
                ChampionImageFileNames = NormalizeSelections(selections)
            };

            File.WriteAllText(AppStorage.ChampionImageSelectionFilePath, JsonSerializer.Serialize(file, SerializerOptions));
        }

        private static Dictionary<int, string> NormalizeSelections(IReadOnlyDictionary<int, string>? selections)
        {
            return selections?
                .Where(entry => IsValidChampionId(entry.Key) && TryGetSafeFileName(entry.Value, out _))
                .ToDictionary(
                    entry => entry.Key,
                    entry =>
                    {
                        TryGetSafeFileName(entry.Value, out string safeFileName);
                        return safeFileName;
                    },
                    EqualityComparer<int>.Default)
                ?? [];
        }

        private static bool IsValidChampionId(int championId)
        {
            return championId > 0 && ChampionCatalog.TryGetById(championId, out _);
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

        private static void RaiseSelectionsChanged()
        {
            SelectionsChanged?.Invoke(null, EventArgs.Empty);
        }

        private sealed class ChampionImageSelectionFile
        {
            public int Version { get; set; } = AppStorage.ChampionImageSelectionFileVersion;

            public Dictionary<int, string> ChampionImageFileNames { get; set; } = [];
        }

        private sealed class LegacySettingsFile
        {
            public Dictionary<int, string> ChampionImageFileNames { get; set; } = [];
        }
    }
}
