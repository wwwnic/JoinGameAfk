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

        public event Action? Saved;

        public PositionPreference GetPreference(Position position)
        {
            position = NormalizePreferencePosition(position);

            if (Preferences.TryGetValue(position, out var pref)
                && (pref.PickChampionIds.Count > 0 || pref.BanChampionIds.Count > 0))
            {
                return pref;
            }

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

        public void Save()
        {
            JsonSettingsStore.Save(AppStorage.RolePlanSettingsFilePath, this, NormalizeSettings);
            Saved?.Invoke();
        }

        public static RolePlanSettings Load()
        {
            return JsonSettingsStore.Load(AppStorage.RolePlanSettingsFilePath, () => new RolePlanSettings(), NormalizeSettings);
        }

        private List<int> GetMergedChampionIds(Position position, Func<PositionPreference, List<int>> selector)
        {
            position = NormalizePreferencePosition(position);

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
            settings.Preferences.Remove(Position.None);

            foreach (Position position in Enum.GetValues<Position>().Where(position => position != Position.None))
            {
                if (!settings.Preferences.TryGetValue(position, out var preference) || preference is null)
                    settings.Preferences[position] = new PositionPreference();
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
