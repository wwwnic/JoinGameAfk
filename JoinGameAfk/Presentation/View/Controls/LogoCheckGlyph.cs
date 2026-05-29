using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.View.Controls
{
    public sealed class LogoCheckGlyph : FrameworkElement
    {
        private const string ResourceName = "JoinGameAfk.Assets.logo.jgalogo";
        private static readonly LogoCheckSettings Settings = LogoCheckSettings.Load();

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0)
                return;

            double left = (ActualWidth - size) / 2;
            double top = (ActualHeight - size) / 2;

            drawingContext.PushTransform(new TranslateTransform(left, top));
            try
            {
                Geometry geometry = CreateCheckGeometry(size, Settings);
                if (Settings.ShowCheckContrastBorder)
                {
                    drawingContext.DrawGeometry(
                        null,
                        CreateRoundPen(WithAlpha(Settings.EdgeShadowColor, 164), GetCheckShadowStrokeWidth(size, Settings)),
                        geometry);
                }

                drawingContext.DrawGeometry(
                    null,
                    CreateRoundPen(Settings.CheckColor, GetCheckStrokeWidth(size, Settings)),
                    geometry);
            }
            finally
            {
                drawingContext.Pop();
            }
        }

        private static Pen CreateRoundPen(Color color, double width)
        {
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            if (brush.CanFreeze)
                brush.Freeze();
            if (pen.CanFreeze)
                pen.Freeze();

            return pen;
        }

        private static Geometry CreateCheckGeometry(double size, LogoCheckSettings settings)
        {
            Point[] points = CreateCheckPoints(size, settings);
            var figure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };

            figure.Segments.Add(new PolyLineSegment(points.Skip(1), true));

            var geometry = new PathGeometry([figure]);
            if (geometry.CanFreeze)
                geometry.Freeze();

            return geometry;
        }

        private static Point[] CreateCheckPoints(double size, LogoCheckSettings settings)
        {
            double checkScale = Math.Clamp(settings.CheckScale, 0.16, 0.95);
            double checkSize = size * checkScale;
            double centerX = size / 2 + Math.Clamp(settings.CheckOffsetX, -0.35, 0.35) * size;
            double centerY = size / 2 + Math.Clamp(settings.CheckOffsetY, -0.35, 0.35) * size;

            return
            [
                new Point(centerX - checkSize * 0.36, centerY + checkSize * 0.04),
                new Point(centerX - checkSize * 0.12, centerY + checkSize * 0.27),
                new Point(centerX + checkSize * 0.39, centerY - checkSize * 0.29)
            ];
        }

        private static double GetCheckStrokeWidth(double size, LogoCheckSettings settings) =>
            Math.Max(1, size * Math.Clamp(settings.CheckScale, 0.16, 0.95) * 0.145);

        private static double GetCheckShadowStrokeWidth(double size, LogoCheckSettings settings) =>
            GetCheckStrokeWidth(size, settings) * 1.62;

        private static Color WithAlpha(Color color, byte alpha) =>
            Color.FromArgb(alpha, color.R, color.G, color.B);

        private sealed class LogoCheckSettings
        {
            public Color CheckColor { get; init; } = Color.FromRgb(34, 197, 94);

            public Color EdgeShadowColor { get; init; } = Color.FromRgb(17, 24, 39);

            public bool ShowCheckContrastBorder { get; init; } = true;

            public double CheckScale { get; init; } = 0.44;

            public double CheckOffsetX { get; init; }

            public double CheckOffsetY { get; init; }

            public static LogoCheckSettings Load()
            {
                try
                {
                    using Stream? stream = typeof(LogoCheckGlyph).Assembly.GetManifestResourceStream(ResourceName);
                    if (stream is null)
                        return new LogoCheckSettings();

                    using JsonDocument document = JsonDocument.Parse(stream);
                    return FromJson(document.RootElement).Sanitize();
                }
                catch (JsonException)
                {
                    return new LogoCheckSettings();
                }
                catch (IOException)
                {
                    return new LogoCheckSettings();
                }
            }

            private static LogoCheckSettings FromJson(JsonElement element)
            {
                var fallback = new LogoCheckSettings();

                return new LogoCheckSettings
                {
                    CheckColor = GetColor(element, nameof(CheckColor), fallback.CheckColor),
                    EdgeShadowColor = GetColor(element, nameof(EdgeShadowColor), fallback.EdgeShadowColor),
                    ShowCheckContrastBorder = GetBoolean(element, nameof(ShowCheckContrastBorder), fallback.ShowCheckContrastBorder),
                    CheckScale = GetDouble(element, nameof(CheckScale), fallback.CheckScale),
                    CheckOffsetX = GetDouble(element, nameof(CheckOffsetX), fallback.CheckOffsetX),
                    CheckOffsetY = GetDouble(element, nameof(CheckOffsetY), fallback.CheckOffsetY)
                };
            }

            private LogoCheckSettings Sanitize()
            {
                var fallback = new LogoCheckSettings();

                return new LogoCheckSettings
                {
                    CheckColor = CheckColor,
                    EdgeShadowColor = EdgeShadowColor,
                    ShowCheckContrastBorder = ShowCheckContrastBorder,
                    CheckScale = ClampFinite(CheckScale, 0.16, 0.95, fallback.CheckScale),
                    CheckOffsetX = ClampFinite(CheckOffsetX, -0.35, 0.35, fallback.CheckOffsetX),
                    CheckOffsetY = ClampFinite(CheckOffsetY, -0.35, 0.35, fallback.CheckOffsetY)
                };
            }

            private static Color GetColor(JsonElement element, string propertyName, Color fallback)
            {
                if (!element.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind != JsonValueKind.String)
                {
                    return fallback;
                }

                string? value = property.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    return fallback;

                return TryParseHexColor(value, out Color color)
                    ? color
                    : fallback;
            }

            private static bool TryParseHexColor(string value, out Color color)
            {
                color = default;
                string hex = value.Trim().TrimStart('#');
                if (hex.Length != 6 && hex.Length != 8)
                    return false;

                try
                {
                    byte alpha = hex.Length == 8 ? Convert.ToByte(hex[..2], 16) : byte.MaxValue;
                    int offset = hex.Length == 8 ? 2 : 0;
                    byte red = Convert.ToByte(hex.Substring(offset, 2), 16);
                    byte green = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
                    byte blue = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
                    color = Color.FromArgb(alpha, red, green, blue);
                    return true;
                }
                catch (FormatException)
                {
                    return false;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            private static double GetDouble(JsonElement element, string propertyName, double fallback) =>
                element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetDouble(out double value)
                    ? value
                    : fallback;

            private static bool GetBoolean(JsonElement element, string propertyName, bool fallback) =>
                element.TryGetProperty(propertyName, out JsonElement property)
                && property.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? property.GetBoolean()
                    : fallback;

            private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
                double.IsFinite(value)
                    ? Math.Clamp(value, minimum, maximum)
                    : fallback;
        }
    }
}
