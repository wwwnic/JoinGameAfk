using System.Text.Json;
using JoinGameAfk.Constant;

namespace JoinGameAfk.Model
{
    public sealed class OverlaySettings
    {
        public const int MinPickBanOverlayScalePercent = 80;
        public const int MaxPickBanOverlayScalePercent = 140;
        public const int DefaultPickBanOverlayScalePercent = 100;
        public const int MinPickBanOverlayOpacityPercent = 55;
        public const int MaxPickBanOverlayOpacityPercent = 100;
        public const int DefaultPickBanOverlayOpacityPercent = 94;
        public const int MinQueueMicroOverlayScalePercent = 80;
        public const int MaxQueueMicroOverlayScalePercent = 180;
        public const int DefaultQueueMicroOverlayScalePercent = 100;

        public int Version { get; set; } = AppStorage.OverlaySettingsFileVersion;

        public bool QueueMicroOverlayEnabled { get; set; } = true;
        public bool QueueMicroOverlayTopmostEnabled { get; set; } = true;
        public int QueueMicroOverlayScalePercent { get; set; } = DefaultQueueMicroOverlayScalePercent;
        public double? QueueMicroOverlayLeft { get; set; }
        public double? QueueMicroOverlayTop { get; set; }

        public bool AutoShowPickBanOverlayEnabled { get; set; } = true;
        public bool PickBanOverlayOpenOnStartup { get; set; }
        public bool PickBanOverlayAutoCloseAfterChampSelectEnabled { get; set; } = true;
        public double? PickBanOverlayLeft { get; set; }
        public double? PickBanOverlayTop { get; set; }
        public int PickBanOverlayScalePercent { get; set; } = DefaultPickBanOverlayScalePercent;
        public double? PickBanOverlayWidth { get; set; }
        public double? PickBanOverlayHeight { get; set; }
        public int PickBanOverlayOpacityPercent { get; set; } = DefaultPickBanOverlayOpacityPercent;
        public bool PickBanOverlayTopmostEnabled { get; set; } = true;
        public bool PickBanOverlayShowPhaseSummary { get; set; } = true;
        public bool PickBanOverlayShowTimers { get; set; } = true;
        public bool PickBanOverlayShowPickPlan { get; set; } = true;
        public bool PickBanOverlayShowBanPlan { get; set; } = true;

        public event Action? Saved;

        public static OverlaySettings Load(ChampSelectSettings? legacySettings = null)
        {
            try
            {
                if (File.Exists(AppStorage.OverlaySettingsFilePath))
                {
                    var json = File.ReadAllText(AppStorage.OverlaySettingsFilePath);
                    var settings = JsonSerializer.Deserialize<OverlaySettings>(json);
                    return settings is null
                        ? ResetSettingsFile(legacySettings)
                        : NormalizeVersion(settings);
                }
            }
            catch
            {
                return ResetSettingsFile(legacySettings);
            }

            return ResetSettingsFile(legacySettings);
        }

        public void Save()
        {
            AppStorage.EnsureDirectoryExists();
            Version = AppStorage.OverlaySettingsFileVersion;
            NormalizeOptions();

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppStorage.OverlaySettingsFilePath, json);
            Saved?.Invoke();
        }

        public void ResetToDefaults()
        {
            var defaults = new OverlaySettings();

            QueueMicroOverlayEnabled = defaults.QueueMicroOverlayEnabled;
            QueueMicroOverlayTopmostEnabled = defaults.QueueMicroOverlayTopmostEnabled;
            QueueMicroOverlayScalePercent = defaults.QueueMicroOverlayScalePercent;
            QueueMicroOverlayLeft = defaults.QueueMicroOverlayLeft;
            QueueMicroOverlayTop = defaults.QueueMicroOverlayTop;

            AutoShowPickBanOverlayEnabled = defaults.AutoShowPickBanOverlayEnabled;
            PickBanOverlayOpenOnStartup = defaults.PickBanOverlayOpenOnStartup;
            PickBanOverlayAutoCloseAfterChampSelectEnabled = defaults.PickBanOverlayAutoCloseAfterChampSelectEnabled;
            PickBanOverlayLeft = defaults.PickBanOverlayLeft;
            PickBanOverlayTop = defaults.PickBanOverlayTop;
            PickBanOverlayScalePercent = defaults.PickBanOverlayScalePercent;
            PickBanOverlayWidth = defaults.PickBanOverlayWidth;
            PickBanOverlayHeight = defaults.PickBanOverlayHeight;
            PickBanOverlayOpacityPercent = defaults.PickBanOverlayOpacityPercent;
            PickBanOverlayTopmostEnabled = defaults.PickBanOverlayTopmostEnabled;
            PickBanOverlayShowPhaseSummary = defaults.PickBanOverlayShowPhaseSummary;
            PickBanOverlayShowTimers = defaults.PickBanOverlayShowTimers;
            PickBanOverlayShowPickPlan = defaults.PickBanOverlayShowPickPlan;
            PickBanOverlayShowBanPlan = defaults.PickBanOverlayShowBanPlan;
        }

        public void NormalizeOptions()
        {
            QueueMicroOverlayScalePercent = NormalizeQueueMicroOverlayScalePercent(QueueMicroOverlayScalePercent);
            QueueMicroOverlayLeft = NormalizeNullableScreenCoordinate(QueueMicroOverlayLeft);
            QueueMicroOverlayTop = NormalizeNullableScreenCoordinate(QueueMicroOverlayTop);

            PickBanOverlayScalePercent = NormalizePickBanOverlayScalePercent(PickBanOverlayScalePercent);
            PickBanOverlayWidth = NormalizeNullableOverlayLength(PickBanOverlayWidth);
            PickBanOverlayHeight = NormalizeNullableOverlayLength(PickBanOverlayHeight);
            PickBanOverlayOpacityPercent = NormalizePickBanOverlayOpacityPercent(PickBanOverlayOpacityPercent);
            PickBanOverlayLeft = NormalizeNullableScreenCoordinate(PickBanOverlayLeft);
            PickBanOverlayTop = NormalizeNullableScreenCoordinate(PickBanOverlayTop);
            EnsurePickBanOverlayHasVisibleSection();
        }

        public void NormalizePickBanOverlayOptions()
        {
            NormalizeOptions();
        }

        public void EnsurePickBanOverlayHasVisibleSection()
        {
            if (PickBanOverlayShowPhaseSummary
                || PickBanOverlayShowTimers
                || PickBanOverlayShowPickPlan
                || PickBanOverlayShowBanPlan)
            {
                return;
            }

            PickBanOverlayShowPhaseSummary = true;
        }

        public static int NormalizePickBanOverlayScalePercent(int scalePercent)
        {
            return Math.Clamp(
                scalePercent <= 0 ? DefaultPickBanOverlayScalePercent : scalePercent,
                MinPickBanOverlayScalePercent,
                MaxPickBanOverlayScalePercent);
        }

        public static int NormalizePickBanOverlayOpacityPercent(int opacityPercent)
        {
            return Math.Clamp(
                opacityPercent <= 0 ? DefaultPickBanOverlayOpacityPercent : opacityPercent,
                MinPickBanOverlayOpacityPercent,
                MaxPickBanOverlayOpacityPercent);
        }

        public static int NormalizeQueueMicroOverlayScalePercent(int scalePercent)
        {
            return Math.Clamp(
                scalePercent <= 0 ? DefaultQueueMicroOverlayScalePercent : scalePercent,
                MinQueueMicroOverlayScalePercent,
                MaxQueueMicroOverlayScalePercent);
        }

        private static OverlaySettings ResetSettingsFile(ChampSelectSettings? legacySettings)
        {
            var defaults = legacySettings is null
                ? new OverlaySettings()
                : CreateFromLegacySettings(legacySettings);
            try
            {
                defaults.Save();
            }
            catch
            {
            }

            return defaults;
        }

        private static OverlaySettings NormalizeVersion(OverlaySettings settings)
        {
            settings.Version = AppStorage.OverlaySettingsFileVersion;
            settings.NormalizeOptions();
            return settings;
        }

        private static OverlaySettings CreateFromLegacySettings(ChampSelectSettings settings)
        {
            var overlaySettings = new OverlaySettings
            {
                AutoShowPickBanOverlayEnabled = settings.AutoShowPickBanOverlayEnabled,
                PickBanOverlayOpenOnStartup = settings.PickBanOverlayOpenOnStartup,
                PickBanOverlayAutoCloseAfterChampSelectEnabled = settings.PickBanOverlayAutoCloseAfterChampSelectEnabled,
                PickBanOverlayLeft = settings.PickBanOverlayLeft,
                PickBanOverlayTop = settings.PickBanOverlayTop,
                PickBanOverlayScalePercent = settings.PickBanOverlayScalePercent,
                PickBanOverlayWidth = settings.PickBanOverlayWidth,
                PickBanOverlayHeight = settings.PickBanOverlayHeight,
                PickBanOverlayOpacityPercent = settings.PickBanOverlayOpacityPercent,
                PickBanOverlayTopmostEnabled = settings.PickBanOverlayTopmostEnabled,
                PickBanOverlayShowPhaseSummary = settings.PickBanOverlayShowPhaseSummary,
                PickBanOverlayShowTimers = settings.PickBanOverlayShowTimers,
                PickBanOverlayShowPickPlan = settings.PickBanOverlayShowPickPlan,
                PickBanOverlayShowBanPlan = settings.PickBanOverlayShowBanPlan
            };

            ApplyLegacyQueueMicroOverlaySettings(overlaySettings);
            overlaySettings.NormalizeOptions();
            return overlaySettings;
        }

        private static void ApplyLegacyQueueMicroOverlaySettings(OverlaySettings overlaySettings)
        {
            try
            {
                if (!File.Exists(AppStorage.SettingsFilePath))
                    return;

                using var document = JsonDocument.Parse(File.ReadAllText(AppStorage.SettingsFilePath));
                var root = document.RootElement;

                if (TryGetBoolean(root, nameof(QueueMicroOverlayEnabled), out bool enabled))
                    overlaySettings.QueueMicroOverlayEnabled = enabled;

                if (TryGetBoolean(root, nameof(QueueMicroOverlayTopmostEnabled), out bool topmostEnabled))
                    overlaySettings.QueueMicroOverlayTopmostEnabled = topmostEnabled;

                if (TryGetInt32(root, nameof(QueueMicroOverlayScalePercent), out int scalePercent))
                    overlaySettings.QueueMicroOverlayScalePercent = scalePercent;

                if (TryGetDouble(root, nameof(QueueMicroOverlayLeft), out double left))
                    overlaySettings.QueueMicroOverlayLeft = left;

                if (TryGetDouble(root, nameof(QueueMicroOverlayTop), out double top))
                    overlaySettings.QueueMicroOverlayTop = top;
            }
            catch
            {
            }
        }

        private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
        {
            value = false;
            if (!element.TryGetProperty(propertyName, out var property))
                return false;

            if (property.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            return property.ValueKind == JsonValueKind.False;
        }

        private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            return element.TryGetProperty(propertyName, out var property)
                && property.TryGetInt32(out value);
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            return element.TryGetProperty(propertyName, out var property)
                && property.TryGetDouble(out value)
                && double.IsFinite(value);
        }

        private static double? NormalizeNullableOverlayLength(double? length)
        {
            return length is double value && double.IsFinite(value) && value > 0
                ? value
                : null;
        }

        private static double? NormalizeNullableScreenCoordinate(double? coordinate)
        {
            return coordinate is double value && double.IsFinite(value)
                ? value
                : null;
        }
    }
}
