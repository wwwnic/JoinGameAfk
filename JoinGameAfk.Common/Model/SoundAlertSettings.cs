using System.Text.Json.Serialization;

namespace JoinGameAfk.Model
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SoundAlertProfile
    {
        Off,
        Minimal,
        Custom
    }

    public sealed class SoundAlertSetting
    {
        public bool Enabled { get; set; }
        public string SoundKey { get; set; } = SoundAlertDefaults.DefaultSoundKey;
        public int? VolumePercent { get; set; } = ChampSelectSettings.DefaultSoundAlertVolumePercent;
        public int? ThresholdSeconds { get; set; }
    }

    public sealed record SoundAlertDefinition(
        string Id,
        string GroupName,
        string DisplayName,
        string Description,
        string DefaultSoundKey,
        bool EnabledInMinimal,
        int? DefaultThresholdSeconds = null);

    public static class SoundAlertIds
    {
        public const string ReadyCheck = "ready-check";
        public const string ChampSelectStart = "champ-select-start";
        public const string PlanningStart = "planning-start";
        public const string BanActionStart = "ban-action-start";
        public const string PickActionStart = "pick-action-start";
        public const string PickLockSoon = "pick-lock-soon";
        public const string BanLockSoon = "ban-lock-soon";
    }

    public static class SoundAlertDefaults
    {
        public const string DefaultSoundKey = "metallic-lock";
        public const int DefaultLockSoonThresholdSeconds = 3;

        public static IReadOnlyList<SoundAlertDefinition> Definitions { get; } =
        [
            new(
                SoundAlertIds.ReadyCheck,
                "Queue",
                "Ready check appears",
                "Plays as soon as the ready check popup is detected.",
                DefaultSoundKey,
                EnabledInMinimal: true),
            new(
                SoundAlertIds.ChampSelectStart,
                "Phase changes",
                "Champion select starts",
                "Plays when the League Client enters champion select.",
                "draft-surge",
                EnabledInMinimal: false),
            new(
                SoundAlertIds.PlanningStart,
                "Phase changes",
                "Planning starts",
                "Plays when the draft planning phase starts.",
                "celestial-sweep",
                EnabledInMinimal: false),
            new(
                SoundAlertIds.BanActionStart,
                "Champion select",
                "Your ban action starts",
                "Plays when your active ban action begins.",
                "sword",
                EnabledInMinimal: false),
            new(
                SoundAlertIds.PickActionStart,
                "Champion select",
                "Your pick action starts",
                "Plays when your active pick action begins.",
                "action-pulse",
                EnabledInMinimal: false),
            new(
                SoundAlertIds.PickLockSoon,
                "Champion select",
                "Pick auto-lock is close",
                "Plays before the app auto-locks your pick.",
                "lock-in-impact",
                EnabledInMinimal: false,
                DefaultLockSoonThresholdSeconds),
            new(
                SoundAlertIds.BanLockSoon,
                "Champion select",
                "Ban auto-lock is close",
                "Plays before the app auto-locks your ban.",
                "lock-in-impact",
                EnabledInMinimal: false,
                DefaultLockSoonThresholdSeconds),
        ];

        public static IReadOnlyList<string> AlertIds { get; } =
            Definitions.Select(definition => definition.Id).ToList();

        public static Dictionary<string, SoundAlertSetting> CreateDefaultSettings()
        {
            return Definitions.ToDictionary(
                definition => definition.Id,
                CreateDefaultSetting,
                StringComparer.Ordinal);
        }

        public static SoundAlertSetting CreateDefaultSetting(string alertId)
        {
            var definition = GetDefinition(alertId);
            return CreateDefaultSetting(definition);
        }

        public static SoundAlertDefinition GetDefinition(string alertId)
        {
            return Definitions.FirstOrDefault(definition => string.Equals(definition.Id, alertId, StringComparison.Ordinal))
                ?? Definitions[0];
        }

        public static bool IsEnabledInMinimal(string alertId)
        {
            return GetDefinition(alertId).EnabledInMinimal;
        }

        private static SoundAlertSetting CreateDefaultSetting(SoundAlertDefinition definition)
        {
            return new SoundAlertSetting
            {
                Enabled = definition.EnabledInMinimal,
                SoundKey = definition.DefaultSoundKey,
                VolumePercent = ChampSelectSettings.DefaultSoundAlertVolumePercent,
                ThresholdSeconds = definition.DefaultThresholdSeconds
            };
        }
    }
}
