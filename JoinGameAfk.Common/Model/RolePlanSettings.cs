using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Services;

namespace JoinGameAfk.Model
{
    public sealed class PositionPreference
    {
        public List<int> PickChampionIds { get; set; } = [];
        public List<int> BanChampionIds { get; set; } = [];
    }

    public sealed class RolePlanSettings
    {
        public int Version { get; set; } = AppStorage.RolePlanSettingsFileVersion;

        public Dictionary<Position, PositionPreference> Preferences { get; set; } = CreateDefaultPreferences();

        public Dictionary<Position, PositionPreference> ClassicPreferences { get; set; } = CreateDefaultPreferences();

        public event Action? Saved;

        public Dictionary<Position, PositionPreference> GetPreferences(LeagueGameMode gameMode)
        {
            return gameMode == LeagueGameMode.Classic ? ClassicPreferences : Preferences;
        }

        public PositionPreference GetPreference(Position position, LeagueGameMode gameMode = LeagueGameMode.Modern)
        {
            position = NormalizePreferencePosition(position);
            Dictionary<Position, PositionPreference> preferences = GetPreferences(gameMode);

            if (preferences.TryGetValue(position, out var pref)
                && (pref.PickChampionIds.Count > 0 || pref.BanChampionIds.Count > 0))
            {
                return pref;
            }

            return preferences.GetValueOrDefault(Position.Default) ?? new PositionPreference();
        }

        public List<int> GetMergedPickChampionIds(Position position, LeagueGameMode gameMode = LeagueGameMode.Modern)
        {
            return GetMergedChampionIds(position, gameMode, pref => pref.PickChampionIds);
        }

        public List<int> GetMergedBanChampionIds(Position position, LeagueGameMode gameMode = LeagueGameMode.Modern)
        {
            return GetMergedChampionIds(position, gameMode, pref => pref.BanChampionIds);
        }

        public void Save()
        {
            JsonSettingsStore.Save(AppStorage.RolePlanSettingsFilePath, this, NormalizeSettings);
            Saved?.Invoke();
        }

        public void ReplacePreferences(
            IReadOnlyDictionary<Position, PositionPreference> preferences,
            LeagueGameMode gameMode = LeagueGameMode.Modern)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            Dictionary<Position, PositionPreference> replacement = preferences.ToDictionary(
                entry => entry.Key,
                entry => new PositionPreference
                {
                    PickChampionIds = [.. entry.Value?.PickChampionIds ?? []],
                    BanChampionIds = [.. entry.Value?.BanChampionIds ?? []]
                });
            if (gameMode == LeagueGameMode.Classic)
                ClassicPreferences = replacement;
            else
                Preferences = replacement;
            NormalizeSettings(this);
        }

        public static RolePlanSettings Load()
        {
            return JsonSettingsStore.Load(AppStorage.RolePlanSettingsFilePath, () => new RolePlanSettings(), NormalizeSettings);
        }

        private List<int> GetMergedChampionIds(
            Position position,
            LeagueGameMode gameMode,
            Func<PositionPreference, List<int>> selector)
        {
            position = NormalizePreferencePosition(position);
            Dictionary<Position, PositionPreference> preferences = GetPreferences(gameMode);

            var rolePref = position != Position.Default
                && preferences.TryGetValue(position, out var rp)
                ? selector(rp)
                : [];

            var defaultPref = preferences.TryGetValue(Position.Default, out var dp)
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

        private static Position NormalizePreferencePosition(Position position)
        {
            return position == Position.None
                ? Position.Default
                : position;
        }

        private static void NormalizeSettings(RolePlanSettings settings)
        {
            settings.Version = AppStorage.RolePlanSettingsFileVersion;
            settings.Preferences ??= CreateDefaultPreferences();
            settings.ClassicPreferences ??= CreateDefaultPreferences();
            NormalizePreferences(settings.Preferences);
            NormalizePreferences(settings.ClassicPreferences);
        }

        private static void NormalizePreferences(Dictionary<Position, PositionPreference> preferences)
        {
            preferences.Remove(Position.None);

            foreach (Position position in Enum.GetValues<Position>().Where(position => position != Position.None))
            {
                if (!preferences.TryGetValue(position, out var preference) || preference is null)
                    preferences[position] = new PositionPreference();
                else
                {
                    preference.PickChampionIds ??= [];
                    preference.BanChampionIds ??= [];
                }
            }
        }

        private static Dictionary<Position, PositionPreference> CreateDefaultPreferences()
        {
            return new Dictionary<Position, PositionPreference>
            {
                { Position.Default, new PositionPreference() },
                { Position.Top, new PositionPreference() },
                { Position.Jungle, new PositionPreference() },
                { Position.Mid, new PositionPreference() },
                { Position.Adc, new PositionPreference() },
                { Position.Support, new PositionPreference() },
            };
        }
    }
}
