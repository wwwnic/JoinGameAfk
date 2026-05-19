using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;

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

        private static readonly object ActivePlayersLock = new();
        private static readonly object SoundFileCacheLock = new();

        private static readonly List<MediaPlayer> ActivePlayers = [];
        private static readonly Dictionary<string, string> SoundFileCache = new(StringComparer.Ordinal);

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

        public void PlayReadyCheckDetectedCue(string? soundKey, int volumePercent)
        {
            PlaySound(soundKey, volumePercent, "Ready check sound notification");
        }

        public void PreviewReadyCheckDetectedCue(string? soundKey, int volumePercent)
        {
            PlaySound(soundKey, volumePercent, "Ready check sound preview");
        }

        public static int NormalizeVolumePercent(int? volumePercent)
        {
            return Math.Clamp(volumePercent ?? 100, 0, 100);
        }

        public static double GetVolumeLevel(int? volumePercent)
        {
            return NormalizeVolumePercent(volumePercent) / 100d;
        }

        public static void SetActivePlayerVolume(int? volumePercent)
        {
            double volume = GetVolumeLevel(volumePercent);
            lock (ActivePlayersLock)
            {
                foreach (var player in ActivePlayers)
                    player.Volume = volume;
            }
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

        private void PlaySound(string? soundKey, int volumePercent, string context)
        {
            try
            {
                var option = GetReadyCheckSoundOption(soundKey);
                int normalizedVolumePercent = NormalizeVolumePercent(volumePercent);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} could not access the app dispatcher.");
                    return;
                }

                if (dispatcher.CheckAccess())
                    PlayResourceSound(option, normalizedVolumePercent, context);
                else
                    dispatcher.BeginInvoke(() => PlayResourceSound(option, normalizedVolumePercent, context));
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

        private void PlayResourceSound(NotificationSoundOption option, int volumePercent, string context)
        {
            try
            {
                var player = new MediaPlayer
                {
                    Volume = GetVolumeLevel(volumePercent)
                };
                player.MediaEnded += (_, _) => RemoveActivePlayer(player);
                player.MediaFailed += (_, e) =>
                {
                    RemoveActivePlayer(player);
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} could not play {option.FileName}: {e.ErrorException.Message}");
                };

                lock (ActivePlayersLock)
                    ActivePlayers.Add(player);

                string? soundFilePath = GetSoundFilePath(option);
                if (soundFilePath is null)
                {
                    RemoveActivePlayer(player);
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} resource was not found: {option.ResourceUri}");
                    return;
                }

                player.Open(new Uri(soundFilePath, UriKind.Absolute));
                player.Play();
            }
            catch (Exception ex)
            {
                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} could not play {option.FileName}: {ex.Message}");
            }
        }

        private static void RemoveActivePlayer(MediaPlayer player)
        {
            lock (ActivePlayersLock)
            {
                ActivePlayers.Remove(player);
            }

            player.Close();
        }

        private static string? GetSoundFilePath(NotificationSoundOption option)
        {
            lock (SoundFileCacheLock)
            {
                if (SoundFileCache.TryGetValue(option.Key, out string? cachedFilePath)
                    && File.Exists(cachedFilePath))
                {
                    return cachedFilePath;
                }

                var resourceInfo = Application.GetResourceStream(option.ResourceUri);
                if (resourceInfo is null)
                    return null;

                string soundDirectoryPath = Path.Combine(Path.GetTempPath(), "JoinGameAfk", "Sounds");
                Directory.CreateDirectory(soundDirectoryPath);
                string soundFilePath = Path.Combine(soundDirectoryPath, option.FileName);

                using (var resourceStream = resourceInfo.Stream)
                using (var fileStream = File.Create(soundFilePath))
                {
                    resourceStream.CopyTo(fileStream);
                }

                SoundFileCache[option.Key] = soundFilePath;
                return soundFilePath;
            }
        }
    }
}
