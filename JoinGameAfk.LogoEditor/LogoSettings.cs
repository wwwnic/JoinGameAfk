using System.Globalization;
using System.Windows.Media;

namespace JoinGameAfk.LogoEditor;

public sealed class LogoSettings
{
    public const double DefaultRotationXDegrees = 0.76 * 180 / Math.PI;

    public const double DefaultRotationYDegrees = -0.54 * 180 / Math.PI;

    public const double DefaultRotationZDegrees = 0.22 * 180 / Math.PI;

    public Color PrimaryColor { get; set; } = Color.FromRgb(147, 197, 253);

    public Color SecondaryColor { get; set; } = Color.FromRgb(37, 99, 235);

    public Color RidgeColor { get; set; } = Color.FromRgb(248, 250, 252);

    public Color EdgeShadowColor { get; set; } = Color.FromRgb(5, 7, 13);

    public Color CheckColor { get; set; } = Color.FromRgb(34, 197, 94);

    public double PolyhedronScale { get; set; } = 0.445;

    public double PolyhedronOffsetX { get; set; }

    public double PolyhedronOffsetY { get; set; }

    public double FacetStrokeWidth { get; set; } = 6;

    public double EdgeShadowStrokeWidth { get; set; } = 10;

    public double FadeStrength { get; set; } = 1;

    public double StrokeInsetScale { get; set; } = 1;

    public double FacetDetailLevel { get; set; } = 1;

    public bool HideFacetLinesAtAllSizes { get; set; }

    public bool SurfaceFacetLinesOnly { get; set; }

    public double RotationXDegrees { get; set; } = DefaultRotationXDegrees;

    public double RotationYDegrees { get; set; } = DefaultRotationYDegrees;

    public double RotationZDegrees { get; set; } = DefaultRotationZDegrees;

    public bool ShowCheckOverlay { get; set; }

    public bool ShowCheckContrastBorder { get; set; } = true;

    public double CheckScale { get; set; } = 0.44;

    public double CheckOffsetX { get; set; }

    public double CheckOffsetY { get; set; }

    public static LogoSettings CreateDefault() => new();

    public LogoSettings Clone() =>
        new()
        {
            PrimaryColor = PrimaryColor,
            SecondaryColor = SecondaryColor,
            RidgeColor = RidgeColor,
            EdgeShadowColor = EdgeShadowColor,
            CheckColor = CheckColor,
            PolyhedronScale = PolyhedronScale,
            PolyhedronOffsetX = PolyhedronOffsetX,
            PolyhedronOffsetY = PolyhedronOffsetY,
            FacetStrokeWidth = FacetStrokeWidth,
            EdgeShadowStrokeWidth = EdgeShadowStrokeWidth,
            FadeStrength = FadeStrength,
            StrokeInsetScale = StrokeInsetScale,
            FacetDetailLevel = FacetDetailLevel,
            HideFacetLinesAtAllSizes = HideFacetLinesAtAllSizes,
            SurfaceFacetLinesOnly = SurfaceFacetLinesOnly,
            RotationXDegrees = RotationXDegrees,
            RotationYDegrees = RotationYDegrees,
            RotationZDegrees = RotationZDegrees,
            ShowCheckOverlay = ShowCheckOverlay,
            ShowCheckContrastBorder = ShowCheckContrastBorder,
            CheckScale = CheckScale,
            CheckOffsetX = CheckOffsetX,
            CheckOffsetY = CheckOffsetY
        };

    public static string ToHex(Color color) =>
        FormattableString.Invariant($"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static bool TryParseHexColor(string value, out Color color)
    {
        color = Colors.Transparent;
        string hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length == 3)
        {
            hex = string.Concat(
                hex[0], hex[0],
                hex[1], hex[1],
                hex[2], hex[2]);
        }

        if (hex.Length != 6)
            return false;

        return byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)
            && AssignColor(r, g, b, out color);
    }

    private static bool AssignColor(byte r, byte g, byte b, out Color color)
    {
        color = Color.FromRgb(r, g, b);
        return true;
    }
}
