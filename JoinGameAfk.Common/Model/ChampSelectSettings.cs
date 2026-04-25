using System.Text.Json;
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
            AppContext.BaseDirectory,
            "champselectsettings.json");

        private static readonly string LegacySettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JoinGameAfk",
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

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
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
                    return settings;
                }
            }
            catch { }

            return new ChampSelectSettings();
        }
    }
}
