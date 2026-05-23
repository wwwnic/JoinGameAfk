using System.IO;
using System.Media;
using System.Windows;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JoinGameAfk.Services
{
    internal sealed record NotificationSoundOption(string Key, string DisplayName, string FileName, bool IsLoopable = false)
    {
        public Uri ResourceUri => new($"pack://application:,,,/Assets/Sounds/{FileName}", UriKind.Absolute);
    }

    internal sealed record NotificationSoundPlaybackOptions(
        int? PlaybackDurationSeconds = null,
        string? ChannelKey = null);

    internal sealed class NotificationSoundPlayer
    {
        public const string DefaultSoundKey = "metallic-lock";
        public const string DefaultReadyCheckSoundKey = DefaultSoundKey;

        public static IReadOnlyList<NotificationSoundOption> SoundOptions { get; } =
        [
            new(DefaultSoundKey, "Metallic Lock", "MetalicLock.wav"),
            new("numeric", "Accept Action", "Numeric.wav"),
            new("default-rising", "Rising Ready Cue", "ReadyCheckAlert.wav"),
            new("sword", "Sword Guard", "Sword.wav"),
            new("action-pulse", "Action Pulse", "ActionSound.wav"),
            new("queue-gong", "Queue Gong", "QueueGong.wav"),
            new("draft-surge", "Draft Surge", "DraftSurge.wav"),
            new("lock-in-impact", "Lock-In Impact", "LockInImpact.wav"),
            new("celestial-sweep", "Celestial Sweep", "CelestialSweep.wav"),
            new("assistant-beacon", "Assistant Beacon", "AssistantBeacon.wav"),
            new("epic-notify-a", "Epic Notify A", "EpicNotifyA.wav"),
            new("epic-notify-b", "Epic Notify B", "EpicNotifyB.wav"),
            new("dopamine-notify", "Dopamine Notify", "DopamineNotify.wav"),
            new("soft-digital-timbre", "Soft Digital Timbre", "SoftDigitalTimbre.wav"),
            new("modern-hd-notify", "Modern HD Notify", "ModernHDNotify.wav"),
            new("loud-building-notify", "Loud Building Notify", "LoudBuildingNotify.wav"),
            new("short-digital-alert", "Short Digital Alert", "ShortDigitalAlert.wav"),
            new("clean-sharp-digital", "Clean Sharp Digital", "CleanSharpDigital.wav"),
            new("clock-slow", "Clock Slow", "ClockSlow.wav", IsLoopable: true),
            new("clock-fast", "Clock Fast", "ClockFast.wav", IsLoopable: true),
        ];

        public static IReadOnlyList<NotificationSoundOption> ReadyCheckSoundOptions => SoundOptions;

        private static readonly object ActivePlaybacksLock = new();
        private static readonly object SoundCacheLock = new();

        private static readonly List<ActiveSoundPlayback> ActivePlaybacks = [];
        private static readonly Dictionary<string, CachedNotificationSound> SoundCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, ActiveSoundPlayback> ChannelPlaybacks = new(StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, string> LegacySoundKeys =
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
            PlayAlert(soundKey, volumePercent, "Ready check sound notification");
        }

        public void PreviewReadyCheckDetectedCue(string? soundKey, int volumePercent)
        {
            PreviewAlert(soundKey, volumePercent, "Ready check sound preview");
        }

        public void PlayAlert(string? soundKey, int volumePercent, string context, int? playbackDurationSeconds = null)
        {
            PlayAlert(soundKey, volumePercent, context, new NotificationSoundPlaybackOptions(playbackDurationSeconds));
        }

        public void PlayAlert(string? soundKey, int volumePercent, string context, NotificationSoundPlaybackOptions options)
        {
            PlaySound(soundKey, volumePercent, context, options);
        }

        public void PreviewAlert(string? soundKey, int volumePercent, string context, int? playbackDurationSeconds = null)
        {
            PlaySound(soundKey, volumePercent, context, new NotificationSoundPlaybackOptions(playbackDurationSeconds));
        }

        public void PreviewAlert(string? soundKey, int volumePercent, string context, NotificationSoundPlaybackOptions options)
        {
            PlaySound(soundKey, volumePercent, context, options);
        }

        public void PreloadAlert(string? soundKey, string context)
        {
            try
            {
                var option = GetSoundOption(soundKey);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null)
                    return;

                if (dispatcher.CheckAccess())
                    PreloadResourceSound(option, context);
                else
                    dispatcher.BeginInvoke(() => PreloadResourceSound(option, context));
            }
            catch (Exception ex)
            {
                _log?.Invoke($"{context} preload failed: {ex.Message}");
            }
        }

        public void StopChannel(string? channelKey)
        {
            try
            {
                string normalizedChannelKey = NormalizeChannelKey(channelKey);
                if (string.IsNullOrWhiteSpace(normalizedChannelKey))
                    return;

                StopChannelCore(normalizedChannelKey);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Sound channel stop failed: {ex.Message}");
            }
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
            int normalizedVolumePercent = NormalizeVolumePercent(volumePercent);
            lock (ActivePlaybacksLock)
            {
                foreach (var playback in ActivePlaybacks)
                    playback.SetVolume(normalizedVolumePercent);
            }
        }

        public static string NormalizeReadyCheckSoundKey(string? soundKey)
        {
            return NormalizeSoundKey(soundKey);
        }

        public static string NormalizeSoundKey(string? soundKey)
        {
            if (string.IsNullOrWhiteSpace(soundKey))
                return DefaultSoundKey;

            string normalizedSoundKey = soundKey.Trim();
            if (LegacySoundKeys.TryGetValue(normalizedSoundKey, out string? migratedSoundKey))
                normalizedSoundKey = migratedSoundKey;

            return SoundOptions.Any(option => string.Equals(option.Key, normalizedSoundKey, StringComparison.Ordinal))
                ? normalizedSoundKey
                : DefaultSoundKey;
        }

        public static bool IsLoopableSoundKey(string? soundKey)
        {
            return GetSoundOption(soundKey).IsLoopable;
        }

        private void PlaySound(string? soundKey, int volumePercent, string context, NotificationSoundPlaybackOptions options)
        {
            try
            {
                var option = GetSoundOption(soundKey);
                int normalizedVolumePercent = NormalizeVolumePercent(volumePercent);
                int? normalizedPlaybackDurationSeconds = NormalizePlaybackDurationSeconds(options.PlaybackDurationSeconds);
                string channelKey = NormalizeChannelKey(options.ChannelKey);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} could not access the app dispatcher.");
                    return;
                }

                if (dispatcher.CheckAccess())
                    PlayResourceSound(option, normalizedVolumePercent, context, normalizedPlaybackDurationSeconds, channelKey);
                else
                    dispatcher.BeginInvoke(() => PlayResourceSound(option, normalizedVolumePercent, context, normalizedPlaybackDurationSeconds, channelKey));
            }
            catch (Exception ex)
            {
                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} failed: {ex.Message}");
            }
        }

        private static int? NormalizePlaybackDurationSeconds(int? playbackDurationSeconds)
        {
            if (playbackDurationSeconds is null)
                return null;

            return Math.Clamp(playbackDurationSeconds.Value, 1, 30);
        }

        private static NotificationSoundOption GetReadyCheckSoundOption(string? soundKey)
        {
            return GetSoundOption(soundKey);
        }

        private static NotificationSoundOption GetSoundOption(string? soundKey)
        {
            string normalizedKey = NormalizeSoundKey(soundKey);
            return SoundOptions.First(option => string.Equals(option.Key, normalizedKey, StringComparison.Ordinal));
        }

        private static string NormalizeChannelKey(string? channelKey)
        {
            return channelKey?.Trim() ?? string.Empty;
        }

        private void PreloadResourceSound(NotificationSoundOption option, string context)
        {
            try
            {
                if (GetCachedSound(option) is null)
                {
                    _log?.Invoke($"{context} resource was not found: {option.ResourceUri}");
                    return;
                }

                LoopingSoundEngine.Instance.EnsureStarted();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"{context} preload failed for {option.FileName}: {ex.Message}");
            }
        }

        private void PlayResourceSound(NotificationSoundOption option, int volumePercent, string context, int? playbackDurationSeconds, string channelKey)
        {
            ActiveSoundPlayback? playback = null;
            try
            {
                var sound = GetCachedSound(option);
                if (sound is null)
                {
                    SystemSounds.Exclamation.Play();
                    _log?.Invoke($"{context} resource was not found: {option.ResourceUri}");
                    return;
                }

                bool usesChannel = !string.IsNullOrWhiteSpace(channelKey);
                if (usesChannel)
                    StopChannelCore(channelKey, flushQueuedAudio: false);

                bool shouldLoop = playbackDurationSeconds is not null || usesChannel;
                TimeSpan? playbackDuration = playbackDurationSeconds is int seconds
                    ? TimeSpan.FromSeconds(seconds)
                    : null;

                playback = new ActiveSoundPlayback(usesChannel ? channelKey : null);
                playback.Handle = LoopingSoundEngine.Instance.Play(
                    sound,
                    volumePercent,
                    shouldLoop,
                    playbackDuration,
                    onCompleted: () => RemoveActivePlayback(playback, stopEnginePlayback: false),
                    flushQueuedAudio: usesChannel);

                lock (ActivePlaybacksLock)
                {
                    ActivePlaybacks.Add(playback);
                    if (playback.ChannelKey is not null)
                        ChannelPlaybacks[playback.ChannelKey] = playback;
                }
            }
            catch (Exception ex)
            {
                if (playback is not null)
                    RemoveActivePlayback(playback);

                SystemSounds.Exclamation.Play();
                _log?.Invoke($"{context} could not play {option.FileName}: {ex.Message}");
            }
        }

        private static void StopChannelCore(string channelKey, bool flushQueuedAudio = true)
        {
            ActiveSoundPlayback? playback = null;
            lock (ActivePlaybacksLock)
            {
                ChannelPlaybacks.TryGetValue(channelKey, out playback);
            }

            if (playback is not null)
            {
                RemoveActivePlayback(playback);
                if (flushQueuedAudio)
                    LoopingSoundEngine.Instance.FlushQueuedAudio();
            }
        }

        private static void RemoveActivePlayback(ActiveSoundPlayback playback, bool stopEnginePlayback = true)
        {
            if (playback.IsClosed)
                return;

            playback.IsClosed = true;
            if (stopEnginePlayback)
                playback.Handle?.Stop();

            lock (ActivePlaybacksLock)
            {
                ActivePlaybacks.Remove(playback);
                if (playback.ChannelKey is not null
                    && ChannelPlaybacks.TryGetValue(playback.ChannelKey, out var currentPlayback)
                    && ReferenceEquals(currentPlayback, playback))
                {
                    ChannelPlaybacks.Remove(playback.ChannelKey);
                }
            }
        }

        private static CachedNotificationSound? GetCachedSound(NotificationSoundOption option)
        {
            lock (SoundCacheLock)
            {
                if (SoundCache.TryGetValue(option.Key, out var cachedSound))
                    return cachedSound;

                var resourceInfo = Application.GetResourceStream(option.ResourceUri);
                if (resourceInfo is null)
                    return null;

                using var resourceStream = resourceInfo.Stream;
                using var memoryStream = new MemoryStream();
                resourceStream.CopyTo(memoryStream);
                cachedSound = CachedNotificationSound.FromWaveBytes(
                    option.Key,
                    option.FileName,
                    memoryStream.ToArray());
                SoundCache[option.Key] = cachedSound;
                return cachedSound;
            }
        }

        private sealed class ActiveSoundPlayback(string? channelKey)
        {
            public string? ChannelKey { get; } = channelKey;
            public EnginePlaybackHandle? Handle { get; set; }
            public bool IsClosed { get; set; }

            public void SetVolume(int volumePercent)
            {
                Handle?.SetVolume(volumePercent);
            }
        }

        private sealed class CachedNotificationSound
        {
            private CachedNotificationSound(string key, string fileName, float[] samples)
            {
                Key = key;
                FileName = fileName;
                Samples = samples;
                FrameCount = samples.Length / LoopingSoundEngine.OutputChannels;
            }

            public string Key { get; }
            public string FileName { get; }
            public float[] Samples { get; }
            public int FrameCount { get; }

            public static CachedNotificationSound FromWaveBytes(string key, string fileName, byte[] waveBytes)
            {
                using var stream = new MemoryStream(waveBytes, writable: false);
                using var reader = new WaveFileReader(stream);
                ISampleProvider provider = reader.ToSampleProvider();

                if (provider.WaveFormat.SampleRate != LoopingSoundEngine.OutputSampleRate)
                    provider = new WdlResamplingSampleProvider(provider, LoopingSoundEngine.OutputSampleRate);

                provider = provider.WaveFormat.Channels switch
                {
                    1 => new MonoToStereoSampleProvider(provider),
                    2 => provider,
                    _ => throw new InvalidDataException($"{fileName} must be mono or stereo WAV audio.")
                };

                var samples = ReadAllSamples(provider);
                if (samples.Length == 0)
                    throw new InvalidDataException($"{fileName} does not contain audio samples.");

                return new CachedNotificationSound(key, fileName, samples);
            }

            private static float[] ReadAllSamples(ISampleProvider provider)
            {
                var samples = new List<float>();
                var buffer = new float[LoopingSoundEngine.OutputSampleRate * LoopingSoundEngine.OutputChannels];

                int read;
                while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int index = 0; index < read; index++)
                        samples.Add(buffer[index]);
                }

                int completeFrameSampleCount = samples.Count - (samples.Count % LoopingSoundEngine.OutputChannels);
                if (completeFrameSampleCount != samples.Count)
                    samples.RemoveRange(completeFrameSampleCount, samples.Count - completeFrameSampleCount);

                return samples.ToArray();
            }
        }

        private sealed class EnginePlaybackHandle(LoopingSoundEngine engine, EnginePlayback playback)
        {
            public void Stop()
            {
                engine.Stop(playback);
            }

            public void SetVolume(int volumePercent)
            {
                engine.SetVolume(playback, volumePercent);
            }
        }

        private sealed class EnginePlayback(
            CachedNotificationSound sound,
            int volumePercent,
            bool loop,
            long? remainingFrames,
            Action? onCompleted)
        {
            public CachedNotificationSound Sound { get; } = sound;
            public bool Loop { get; } = loop;
            public Action? OnCompleted { get; } = onCompleted;
            public long PositionFrame { get; private set; }
            public long? RemainingFrames { get; private set; } = remainingFrames;
            public int VolumePercent { get; set; } = NormalizeVolumePercent(volumePercent);
            public bool IsStopped { get; set; }

            public bool TryReadNextFrame(out float left, out float right)
            {
                left = 0;
                right = 0;

                if (IsStopped || RemainingFrames == 0)
                    return false;

                if (!Loop && PositionFrame >= Sound.FrameCount)
                    return false;

                int sourceFrame = Loop
                    ? (int)(PositionFrame % Sound.FrameCount)
                    : (int)PositionFrame;
                int sourceOffset = sourceFrame * LoopingSoundEngine.OutputChannels;
                float volume = VolumePercent / 100f;
                left = Sound.Samples[sourceOffset] * volume;
                right = Sound.Samples[sourceOffset + 1] * volume;

                PositionFrame++;
                if (RemainingFrames is not null)
                    RemainingFrames--;

                return true;
            }
        }

        private sealed class LoopingSoundEngine
        {
            public const int OutputSampleRate = 48000;
            public const int OutputChannels = 2;
            private const int DesiredLatencyMilliseconds = 60;
            private const int OutputBufferCount = 3;

            public static LoopingSoundEngine Instance { get; } = new();

            private readonly object _outputLock = new();
            private readonly SoundMixerSampleProvider _mixer = new();
            private WaveOutEvent? _output;

            public void EnsureStarted()
            {
                lock (_outputLock)
                {
                    if (_output is not null)
                        return;

                    _output = new WaveOutEvent
                    {
                        DesiredLatency = DesiredLatencyMilliseconds,
                        NumberOfBuffers = OutputBufferCount
                    };
                    _output.Init(_mixer);
                    _output.Play();
                }
            }

            public EnginePlaybackHandle Play(
                CachedNotificationSound sound,
                int volumePercent,
                bool loop,
                TimeSpan? duration,
                Action? onCompleted,
                bool flushQueuedAudio)
            {
                EnsureStarted();

                long? remainingFrames = duration is null
                    ? null
                    : Math.Max(1, (long)Math.Round(duration.Value.TotalSeconds * OutputSampleRate));
                var playback = new EnginePlayback(sound, volumePercent, loop, remainingFrames, onCompleted);
                _mixer.Add(playback);

                if (flushQueuedAudio)
                    FlushQueuedAudio();

                return new EnginePlaybackHandle(this, playback);
            }

            public void Stop(EnginePlayback playback)
            {
                _mixer.Remove(playback);
            }

            public void SetVolume(EnginePlayback playback, int volumePercent)
            {
                _mixer.SetVolume(playback, volumePercent);
            }

            public void FlushQueuedAudio()
            {
                lock (_outputLock)
                {
                    if (_output is null)
                        return;

                    _output.Stop();
                    _output.Play();
                }
            }
        }

        private sealed class SoundMixerSampleProvider : ISampleProvider
        {
            private readonly object _lock = new();
            private readonly List<EnginePlayback> _playbacks = [];

            public WaveFormat WaveFormat { get; } =
                WaveFormat.CreateIeeeFloatWaveFormat(LoopingSoundEngine.OutputSampleRate, LoopingSoundEngine.OutputChannels);

            public void Add(EnginePlayback playback)
            {
                lock (_lock)
                {
                    _playbacks.Add(playback);
                }
            }

            public void Remove(EnginePlayback playback)
            {
                lock (_lock)
                {
                    playback.IsStopped = true;
                    _playbacks.Remove(playback);
                }
            }

            public void SetVolume(EnginePlayback playback, int volumePercent)
            {
                lock (_lock)
                {
                    playback.VolumePercent = NormalizeVolumePercent(volumePercent);
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                Array.Clear(buffer, offset, count);

                List<Action>? completedActions = null;
                lock (_lock)
                {
                    int frameCount = count / LoopingSoundEngine.OutputChannels;
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        float mixedLeft = 0;
                        float mixedRight = 0;

                        for (int index = _playbacks.Count - 1; index >= 0; index--)
                        {
                            var playback = _playbacks[index];
                            if (!playback.TryReadNextFrame(out float left, out float right))
                            {
                                playback.IsStopped = true;
                                _playbacks.RemoveAt(index);
                                if (playback.OnCompleted is not null)
                                    (completedActions ??= []).Add(playback.OnCompleted);
                                continue;
                            }

                            mixedLeft += left;
                            mixedRight += right;
                        }

                        int sampleOffset = offset + (frame * LoopingSoundEngine.OutputChannels);
                        buffer[sampleOffset] = Math.Clamp(mixedLeft, -1f, 1f);
                        buffer[sampleOffset + 1] = Math.Clamp(mixedRight, -1f, 1f);
                    }
                }

                InvokeCompletedActions(completedActions);
                return count;
            }

            private static void InvokeCompletedActions(List<Action>? completedActions)
            {
                if (completedActions is null)
                    return;

                foreach (var completedAction in completedActions)
                {
                    try
                    {
                        completedAction();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
