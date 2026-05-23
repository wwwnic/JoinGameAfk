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
        public int? PlaybackDurationSeconds { get; set; }
        public bool? InfinitePlaybackEnabled { get; set; }
    }

    public sealed record SoundAlertDefinition(
        string Id,
        string GroupName,
        string DisplayName,
        string Description,
        string DefaultSoundKey,
        bool EnabledInMinimal,
        int? DefaultThresholdSeconds = null,
        int? DefaultPlaybackDurationSeconds = null,
        bool SupportsInfinitePlayback = false,
        bool DefaultInfinitePlaybackEnabled = false);

    public static class SoundAlertIds
    {
        public const string ReadyCheck = "ready-check";
        public const string ChampSelectStart = "champ-select-start";
        public const string ChampSelectEnded = "champ-select-ended";
        public const string BanActionStart = "ban-action-start";
        public const string PickActionStart = "pick-action-start";
        public const string ManualSelectionOverride = "manual-selection-override";
        public const string AllOptionsUnavailable = "all-options-unavailable";
        public const string PickLockCountdown = "pick-lock-countdown";
        public const string BanLockCountdown = "ban-lock-countdown";
        public const string PickLockSoon = "pick-lock-soon";
        public const string BanLockSoon = "ban-lock-soon";
        public const string PickLockComplete = "pick-lock-complete";
        public const string BanLockComplete = "ban-lock-complete";
    }

    public static class SoundAlertDefaults
    {
        public const string DefaultSoundKey = "metallic-lock";
        public const string DefaultLockCountdownSoundKey = "clock-slow";
        public const string DefaultLockSoonSoundKey = "clock-fast";
        public const string AssistantBeaconSoundKey = "assistant-beacon";
        public const string DraftSurgeSoundKey = "draft-surge";
        public const string SoftDigitalTimbreSoundKey = "soft-digital-timbre";
        public const int DefaultLockCountdownThresholdSeconds = 6;
        public const int DefaultLockSoonThresholdSeconds = 2;
        public const int DefaultLoopPlaybackDurationSeconds = 5;

        public static IReadOnlyList<SoundAlertDefinition> Definitions { get; } =
        [
            new(
                SoundAlertIds.ReadyCheck,
                "Queue",
                "Ready check appears",
                "Plays as soon as the ready check popup is detected.",
                AssistantBeaconSoundKey,
                EnabledInMinimal: true),
            new(
                SoundAlertIds.ChampSelectStart,
                "Phase changes",
                "Champion select starts",
                "Plays when the League Client enters champion select.",
                SoftDigitalTimbreSoundKey,
                EnabledInMinimal: true),
            new(
                SoundAlertIds.ChampSelectEnded,
                "Phase changes",
                "Champion select dodged",
                "Plays when champion select returns to lobby, matchmaking, or ready check instead of game.",
                "loud-building-notify",
                EnabledInMinimal: true),
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
                SoundAlertIds.ManualSelectionOverride,
                "Champion select",
                "Manual selection override detected",
                "Plays when you manually change the app-hovered pick or ban and auto-lock follows your current selection.",
                "short-digital-alert",
                EnabledInMinimal: true),
            new(
                SoundAlertIds.AllOptionsUnavailable,
                "Champion select",
                "All configured pick/ban options unavailable",
                "Plays when every configured champion for your active pick or ban is blocked, failed, or not owned.",
                "dopamine-notify",
                EnabledInMinimal: true),
            new(
                SoundAlertIds.PickLockCountdown,
                "Champion select",
                "Pick auto-lock countdown starts",
                "Plays Clock Slow before the final auto-lock countdown cue.",
                DefaultLockCountdownSoundKey,
                EnabledInMinimal: true,
                DefaultLockCountdownThresholdSeconds,
                SupportsInfinitePlayback: true,
                DefaultInfinitePlaybackEnabled: true),
            new(
                SoundAlertIds.PickLockSoon,
                "Champion select",
                "Pick auto-lock final countdown",
                "Plays Clock Fast until the pick auto-locks.",
                DefaultLockSoonSoundKey,
                EnabledInMinimal: true,
                DefaultLockSoonThresholdSeconds,
                SupportsInfinitePlayback: true,
                DefaultInfinitePlaybackEnabled: true),
            new(
                SoundAlertIds.PickLockComplete,
                "Champion select",
                "Pick auto-lock locks champion",
                "Plays when the app successfully locks your pick.",
                DefaultSoundKey,
                EnabledInMinimal: true),
            new(
                SoundAlertIds.BanLockCountdown,
                "Champion select",
                "Ban auto-lock countdown starts",
                "Plays Clock Slow before the final auto-lock countdown cue.",
                DefaultLockCountdownSoundKey,
                EnabledInMinimal: false,
                DefaultLockCountdownThresholdSeconds,
                SupportsInfinitePlayback: true,
                DefaultInfinitePlaybackEnabled: true),
            new(
                SoundAlertIds.BanLockSoon,
                "Champion select",
                "Ban auto-lock final countdown",
                "Plays Clock Fast until the ban auto-locks.",
                DefaultLockSoonSoundKey,
                EnabledInMinimal: false,
                DefaultLockSoonThresholdSeconds,
                SupportsInfinitePlayback: true,
                DefaultInfinitePlaybackEnabled: true),
            new(
                SoundAlertIds.BanLockComplete,
                "Champion select",
                "Ban auto-lock locks champion",
                "Plays when the app successfully locks your ban.",
                "lock-in-impact",
                EnabledInMinimal: false),
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
                ThresholdSeconds = definition.DefaultThresholdSeconds,
                PlaybackDurationSeconds = definition.DefaultPlaybackDurationSeconds,
                InfinitePlaybackEnabled = definition.SupportsInfinitePlayback
                    ? definition.DefaultInfinitePlaybackEnabled
                    : null
            };
        }
    }
}
