using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.Services
{
    internal sealed record NotificationSoundOption(string Key, string DisplayName, string RelativePath);

    internal sealed class NotificationSoundPlayer
    {
        public const string DefaultReadyCheckSoundKey = "numeric";

        public static IReadOnlyList<NotificationSoundOption> ReadyCheckSoundOptions { get; } =
        [
            new(DefaultReadyCheckSoundKey, "Accept Action", Path.Combine("Sounds", "Numeric.wav")),
            new("default-rising", "Rising Ready Cue", Path.Combine("Sounds", "ReadyCheckAlert.wav")),
            new("sword", "Sword Guard", Path.Combine("Sounds", "Sword.wav")),
            new("metallic-lock", "Metallic Lock", Path.Combine("Sounds", "MetalicLock.wav")),
            new("action-pulse", "Action Pulse", Path.Combine("Sounds", "ActionSound.wav")),
        ];

        private static readonly IReadOnlyDictionary<string, string> LegacyReadyCheckSoundKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["accept-action-v2"] = DefaultReadyCheckSoundKey,
                ["reinforced-shield-v2"] = "sword",
                ["short-metallic-lock-v4"] = "metallic-lock",
                ["action-sound-effect-v4"] = "action-pulse",
            };

        private static readonly List<MediaPlayer> ActivePlayers = [];

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
                string cuePath = Path.Combine(AppContext.BaseDirectory, option.RelativePath);
                if (!File.Exists(cuePath))
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} file was not found: {cuePath}");
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} could not access the app dispatcher.");
                    return;
                }

                if (dispatcher.CheckAccess())
                    PlayWithMediaPlayer(cuePath, context);
                else
                    dispatcher.BeginInvoke(() => PlayWithMediaPlayer(cuePath, context));
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

        private void PlayWithMediaPlayer(string cuePath, string context)
        {
            var player = new MediaPlayer();

            void Cleanup()
            {
                player.MediaEnded -= Player_MediaEnded;
                player.MediaFailed -= Player_MediaFailed;
                player.Close();
                ActivePlayers.Remove(player);
            }

            void Player_MediaEnded(object? sender, EventArgs e)
            {
                Cleanup();
            }

            void Player_MediaFailed(object? sender, ExceptionEventArgs e)
            {
                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} could not play {Path.GetFileName(cuePath)}: {e.ErrorException.Message}");
                Cleanup();
            }

            player.MediaEnded += Player_MediaEnded;
            player.MediaFailed += Player_MediaFailed;
            ActivePlayers.Add(player);

            try
            {
                player.Open(new Uri(cuePath, UriKind.Absolute));
                player.Play();
            }
            catch (Exception ex)
            {
                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} could not play {Path.GetFileName(cuePath)}: {ex.Message}");
                Cleanup();
            }
        }
    }
}
