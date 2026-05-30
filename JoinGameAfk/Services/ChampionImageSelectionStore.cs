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
        private static bool _showChampionPictureDownloadWarning = true;

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

        public static bool ShowChampionPictureDownloadWarning
        {
            get
            {
                lock (SyncRoot)
                {
                    EnsureLoaded();
                    return _showChampionPictureDownloadWarning;
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
                SaveLoadedFile();
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

                SaveLoadedFile();
            }

            RaiseSelectionsChanged();
        }

        public static void SetShowChampionPictureDownloadWarning(bool showWarning)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();
                if (_showChampionPictureDownloadWarning == showWarning)
                    return;

                _showChampionPictureDownloadWarning = showWarning;
                SaveLoadedFile();
            }
        }

        public static void Reload()
        {
            lock (SyncRoot)
            {
                _selections = null;
                _showChampionPictureDownloadWarning = true;
                EnsureLoaded();
            }

            RaiseSelectionsChanged();
        }

        private static void EnsureLoaded()
        {
            if (_selections is not null)
                return;

            var file = LoadFile();
            _selections = NormalizeSelections(file.ChampionImageFileNames);
            _showChampionPictureDownloadWarning = file.ShowChampionPictureDownloadWarning;
        }

        private static ChampionImageSelectionFile LoadFile()
        {
            if (!File.Exists(AppStorage.ChampionImageSelectionFilePath))
            {
                var defaults = CreateDefaultFile();
                SaveFile(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(AppStorage.ChampionImageSelectionFilePath);
                var file = JsonSerializer.Deserialize<ChampionImageSelectionFile>(json, SerializerOptions);
                if (file is not null)
                    return NormalizeFile(file);

                QuarantineInvalidFile();
            }
            catch
            {
                QuarantineInvalidFile();
            }

            var defaultFile = CreateDefaultFile();
            SaveFile(defaultFile);
            return defaultFile;
        }

        private static void SaveLoadedFile()
        {
            SaveFile(new ChampionImageSelectionFile
            {
                Version = AppStorage.ChampionImageSelectionFileVersion,
                ShowChampionPictureDownloadWarning = _showChampionPictureDownloadWarning,
                ChampionImageFileNames = NormalizeSelections(_selections)
            });
        }

        private static void SaveFile(ChampionImageSelectionFile file)
        {
            AppStorage.EnsureRolePlansDirectoryExists();
            file = NormalizeFile(file);
            File.WriteAllText(AppStorage.ChampionImageSelectionFilePath, JsonSerializer.Serialize(file, SerializerOptions));
        }

        private static ChampionImageSelectionFile NormalizeFile(ChampionImageSelectionFile file)
        {
            file.Version = AppStorage.ChampionImageSelectionFileVersion;
            file.ChampionImageFileNames = NormalizeSelections(file.ChampionImageFileNames);
            return file;
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

        private static ChampionImageSelectionFile CreateDefaultFile()
        {
            return new ChampionImageSelectionFile
            {
                Version = AppStorage.ChampionImageSelectionFileVersion,
                ShowChampionPictureDownloadWarning = true,
                ChampionImageFileNames = []
            };
        }

        private static void QuarantineInvalidFile()
        {
            try
            {
                if (!File.Exists(AppStorage.ChampionImageSelectionFilePath))
                    return;

                string directoryPath = Path.GetDirectoryName(AppStorage.ChampionImageSelectionFilePath) ?? string.Empty;
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string invalidPath = Path.Combine(directoryPath, $"champion-image-selections.invalid-{timestamp}.json");
                int suffix = 2;
                while (File.Exists(invalidPath))
                {
                    invalidPath = Path.Combine(directoryPath, $"champion-image-selections.invalid-{timestamp}-{suffix}.json");
                    suffix++;
                }

                File.Move(AppStorage.ChampionImageSelectionFilePath, invalidPath);
            }
            catch
            {
            }
        }

        private static void RaiseSelectionsChanged()
        {
            SelectionsChanged?.Invoke(null, EventArgs.Empty);
        }

        private sealed class ChampionImageSelectionFile
        {
            public int Version { get; set; } = AppStorage.ChampionImageSelectionFileVersion;

            public bool ShowChampionPictureDownloadWarning { get; set; } = true;

            public Dictionary<int, string> ChampionImageFileNames { get; set; } = [];
        }
    }
}
