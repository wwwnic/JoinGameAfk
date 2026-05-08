using System.IO;
using System.Media;
using System.Windows;

namespace JoinGameAfk.Services
{
    internal sealed record NotificationSoundOption(string Key, string DisplayName, string FileName)
    {
        public Uri ResourceUri => new($"pack://application:,,,/Assets/Sounds/{FileName}", UriKind.Absolute);
    }

    internal sealed class NotificationSoundPlayer
    {
        public const string DefaultReadyCheckSoundKey = "metallic-lock";

        public static IReadOnlyList<NotificationSoundOption> ReadyCheckSoundOptions { get; } =
        [
            new(DefaultReadyCheckSoundKey, "Metallic Lock", "MetalicLock.wav"),
            new("numeric", "Accept Action", "Numeric.wav"),
            new("default-rising", "Rising Ready Cue", "ReadyCheckAlert.wav"),
            new("sword", "Sword Guard", "Sword.wav"),
            new("action-pulse", "Action Pulse", "ActionSound.wav"),
            new("queue-gong", "Queue Gong", "QueueGong.wav"),
            new("draft-surge", "Draft Surge", "DraftSurge.wav"),
            new("lock-in-impact", "Lock-In Impact", "LockInImpact.wav"),
            new("celestial-sweep", "Celestial Sweep", "CelestialSweep.wav"),
            new("assistant-beacon", "Assistant Beacon", "AssistantBeacon.wav"),
        ];

        private static readonly object SoundPlayerCacheLock = new();

        private static readonly Dictionary<string, SoundPlayer> SoundPlayerCache = new(StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, string> LegacyReadyCheckSoundKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["accept-action-v2"] = "numeric",
                ["reinforced-shield-v2"] = "sword",
                ["short-metallic-lock-v4"] = "metallic-lock",
                ["action-sound-effect-v4"] = "action-pulse",
            };

        private readonly Action<string>? _log;

        public NotificationSoundPlayer(Action<string>? log = null)
        {
            _log = log;
        }

        public void PlayReadyCheckDetectedCue(string? soundKey)
        {
            PlaySound(soundKey, "Ready check sound notification");
        }

        public void PreviewReadyCheckDetectedCue(string? soundKey)
        {
            PlaySound(soundKey, "Ready check sound preview");
        }

        public static string NormalizeReadyCheckSoundKey(string? soundKey)
        {
            if (string.IsNullOrWhiteSpace(soundKey))
                return DefaultReadyCheckSoundKey;

            string normalizedSoundKey = soundKey.Trim();
            if (LegacyReadyCheckSoundKeys.TryGetValue(normalizedSoundKey, out string? migratedSoundKey))
                normalizedSoundKey = migratedSoundKey;

            return ReadyCheckSoundOptions.Any(option => string.Equals(option.Key, normalizedSoundKey, StringComparison.Ordinal))
                ? normalizedSoundKey
                : DefaultReadyCheckSoundKey;
        }

        private void PlaySound(string? soundKey, string context)
        {
            try
            {
                var option = GetReadyCheckSoundOption(soundKey);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} could not access the app dispatcher.");
                    return;
                }

                if (dispatcher.CheckAccess())
                    PlayResourceSound(option, context);
                else
                    dispatcher.BeginInvoke(() => PlayResourceSound(option, context));
            }
            catch (Exception ex)
            {
                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} failed: {ex.Message}");
            }
        }

        private static NotificationSoundOption GetReadyCheckSoundOption(string? soundKey)
        {
            string normalizedKey = NormalizeReadyCheckSoundKey(soundKey);
            return ReadyCheckSoundOptions.First(option => string.Equals(option.Key, normalizedKey, StringComparison.Ordinal));
        }

        private void PlayResourceSound(NotificationSoundOption option, string context)
        {
            try
            {
                SoundPlayer? player = GetSoundPlayer(option);
                if (player is null)
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} resource was not found: {option.ResourceUri}");
                    return;
                }

                player.Play();
            }
            catch (Exception ex)
            {
                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} could not play {option.FileName}: {ex.Message}");
            }
        }

        private static SoundPlayer? GetSoundPlayer(NotificationSoundOption option)
        {
            lock (SoundPlayerCacheLock)
            {
                if (SoundPlayerCache.TryGetValue(option.Key, out var cachedPlayer))
                    return cachedPlayer;

                var resourceInfo = Application.GetResourceStream(option.ResourceUri);
                if (resourceInfo is null)
                    return null;

                using var resourceStream = resourceInfo.Stream;
                var playerStream = new MemoryStream();
                resourceStream.CopyTo(playerStream);
                playerStream.Position = 0;

                var player = new SoundPlayer(playerStream);
                player.Load();

                SoundPlayerCache[option.Key] = player;
                return player;
            }
        }
    }
}
