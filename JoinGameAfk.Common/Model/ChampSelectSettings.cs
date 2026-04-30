using System.Text.Json;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;

namespace JoinGameAfk.Model
{
    public class PositionPreference
    {
        public List<int> PickChampionIds { get; set; } = [];
        public List<int> BanChampionIds { get; set; } = [];
    }

    public class ChampSelectSettings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            AppStorage.DirectoryPath,
            "champselectsettings.json");

        private static readonly string LegacySettingsFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "champselectsettings.json");

        /// <summary>
        /// Number of seconds to wait before automatically accepting a ready check.
        /// This gives the player time to manually accept or decline first.
        /// </summary>
        public int ReadyCheckAcceptDelaySeconds { get; set; } = 5;

        /// <summary>
        /// Lock the pick when this many seconds (or fewer) remain on the timer. 0 = lock immediately.
        /// </summary>
        public int PickLockDelaySeconds { get; set; } = 11;

        /// <summary>
        /// Number of seconds to wait before automatically hovering a configured champion during pick or ban.
        /// </summary>
        public int ChampionHoverDelaySeconds { get; set; } = 2;

        /// <summary>
        /// Lock the ban when this many seconds (or fewer) remain on the timer. 0 = lock immediately.
        /// </summary>
        public int BanLockDelaySeconds { get; set; } = 11;

        /// <summary>
        /// Whether the app should automatically lock the currently selected pick or ban before the timer reaches 0.
        /// </summary>
        public bool AutoLockSelectionEnabled { get; set; } = true;

        /// <summary>
        /// App-wide polling interval in milliseconds.
        /// </summary>
        public int ChampSelectPollIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Visual theme selected for the WPF application.
        /// </summary>
        public string ThemeKey { get; set; } = "draft-desk";

        public Dictionary<Position, PositionPreference> Preferences { get; set; } = new()
        {
            { Position.Default, new PositionPreference() },
            { Position.Top, new PositionPreference() },
            { Position.Jungle, new PositionPreference() },
            { Position.Mid, new PositionPreference() },
            { Position.Adc, new PositionPreference() },
            { Position.Support, new PositionPreference() },
        };

        public PositionPreference GetPreference(Position position)
        {
            if (Preferences.TryGetValue(position, out var pref) && (pref.PickChampionIds.Count > 0 || pref.BanChampionIds.Count > 0))
                return pref;

            return Preferences.GetValueOrDefault(Position.Default) ?? new PositionPreference();
        }

        public List<int> GetMergedPickChampionIds(Position position)
        {
            return GetMergedChampionIds(position, pref => pref.PickChampionIds);
        }

        public List<int> GetMergedBanChampionIds(Position position)
        {
            return GetMergedChampionIds(position, pref => pref.BanChampionIds);
        }

        private List<int> GetMergedChampionIds(Position position, Func<PositionPreference, List<int>> selector)
        {
            var rolePref = position != Position.Default
                && Preferences.TryGetValue(position, out var rp)
                ? selector(rp)
                : [];

            var defaultPref = Preferences.TryGetValue(Position.Default, out var dp)
                ? selector(dp)
                : [];

            if (rolePref.Count == 0)
                return [.. defaultPref];

            var seen = new HashSet<int>(rolePref);
            var merged = new List<int>(rolePref);
            foreach (var id in defaultPref)
            {
                if (seen.Add(id))
                    merged.Add(id);
            }

            return merged;
        }

        public event Action? Saved;

        public void Save()
        {
            AppStorage.EnsureDirectoryExists();

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
            Saved?.Invoke();
        }

        public static ChampSelectSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<ChampSelectSettings>(json) ?? new ChampSelectSettings();
                }

                if (File.Exists(LegacySettingsFilePath))
                {
                    var json = File.ReadAllText(LegacySettingsFilePath);
                    var settings = JsonSerializer.Deserialize<ChampSelectSettings>(json) ?? new ChampSelectSettings();
                    settings.Save();

                    try
                    {
                        File.Delete(LegacySettingsFilePath);
                    }
                    catch { }

                    return settings;
                }
            }
            catch { }

            return new ChampSelectSettings();
        }
    }
}
