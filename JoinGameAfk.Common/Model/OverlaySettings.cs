using JoinGameAfk.Constant;
using JoinGameAfk.Services;

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

        public static OverlaySettings Load()
        {
            return JsonSettingsStore.Load(AppStorage.OverlaySettingsFilePath, () => new OverlaySettings(), NormalizeSettings);
        }

        public void Save()
        {
            JsonSettingsStore.Save(AppStorage.OverlaySettingsFilePath, this, NormalizeSettings);
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

        private static void NormalizeSettings(OverlaySettings settings)
        {
            settings.Version = AppStorage.OverlaySettingsFileVersion;
            settings.NormalizeOptions();
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
