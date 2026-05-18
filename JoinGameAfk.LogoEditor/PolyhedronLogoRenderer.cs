using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JoinGameAfk.LogoEditor;

public static class PolyhedronLogoRenderer
{
    public const int CanvasSize = 512;
    public static readonly int[] DefaultIconSizes = [256, 128, 64, 48, 32, 24, 16];

    private const int MinimumFacetLineSize = 256;

    private static readonly PolyhedronModel SimpleModel = CreateSubdividedIcosahedron(0);
    private static readonly PolyhedronModel DetailedModel = CreateSubdividedIcosahedron(1);

    public static BitmapSource RenderBitmap(int size, LogoSettings settings)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawingContext = visual.RenderOpen())
            DrawLogo(drawingContext, size, settings);

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static string CreateSvg(LogoSettings settings)
    {
        bool drawFacetLines = ShouldDrawFacetLines(settings, CanvasSize);
        List<SvgFace> facePaths = CreateFaces(CanvasSize, settings)
            .Select(face =>
            {
                Color color = ScaleBrightness(Blend(settings.PrimaryColor, settings.SecondaryColor, GetFadeMix(settings, face.ShadeMix)), face.Brightness);
                double fillOpacity = (232 + face.Brightness * 23) / 255;
                double strokeOpacity = (212 + face.Brightness * 36) / 255;
                string points = string.Join(" ", face.Points.Select((point, index) =>
                    FormattableString.Invariant($"{(index == 0 ? "M" : "L")}{point.X:0.0} {point.Y:0.0}")));

                return new SvgFace($"{points} Z", color, fillOpacity, strokeOpacity);
            })
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("""<svg width="512" height="512" viewBox="0 0 512 512" fill="none" xmlns="http://www.w3.org/2000/svg">""");
        builder.AppendLine("""  <g stroke-linejoin="round">""");

        foreach (SvgFace face in facePaths)
        {
            string strokeAttributes = drawFacetLines && settings.EdgeShadowStrokeWidth > 0
                ? FormattableString.Invariant($" stroke=\"{LogoSettings.ToHex(settings.EdgeShadowColor)}\" stroke-opacity=\".36\" stroke-width=\"{settings.EdgeShadowStrokeWidth:0.###}\"")
                : string.Empty;
            builder.AppendLine(FormattableString.Invariant(
                $"""    <path d="{face.Path}" fill="{LogoSettings.ToHex(face.FillColor)}" fill-opacity="{face.FillOpacity:0.000}"{strokeAttributes}/>"""));
        }

        builder.AppendLine("  </g>");

        if (drawFacetLines)
        {
            builder.AppendLine("""  <g fill="none" stroke-linejoin="round">""");

            foreach (SvgFace face in facePaths)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"""    <path d="{face.Path}" stroke="{LogoSettings.ToHex(settings.RidgeColor)}" stroke-opacity="{face.StrokeOpacity:0.000}" stroke-width="{settings.FacetStrokeWidth:0.###}"/>"""));
            }

            builder.AppendLine("  </g>");
        }

        AppendCheckSvg(builder, settings);

        builder.AppendLine("</svg>");

        return builder.ToString();
    }

    public static LogoExportResult WriteAssets(LogoSettings settings, IReadOnlyCollection<int>? iconSizes = null)
    {
        DirectoryInfo root = FindRepoRoot();
        string assetsDirectory = Path.Combine(root.FullName, "JoinGameAfk", "Assets");
        string svgPath = Path.Combine(assetsDirectory, "logo.svg");
        string icoPath = Path.Combine(assetsDirectory, "logo.ico");
        string previewPath = Path.Combine(root.FullName, ".build-verify", "logo-polyhedron-preview.png");

        Directory.CreateDirectory(assetsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);

        File.WriteAllText(svgPath, CreateSvg(settings), Encoding.UTF8);
        WriteIcon(icoPath, settings, iconSizes);
        WritePng(previewPath, RenderBitmap(CanvasSize, settings));

        return new LogoExportResult(svgPath, icoPath, previewPath);
    }

    public static void WriteIcon(string path, LogoSettings settings, IReadOnlyCollection<int>? iconSizes = null)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        int[] sizes = NormalizeIconSizes(iconSizes);
        List<byte[]> pngFrames = sizes
            .Select(size => EncodePng(RenderBitmap(size, settings)))
            .ToList();

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);

        int imageOffset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            byte[] png = pngFrames[i];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)imageOffset);
            imageOffset += png.Length;
        }

        foreach (byte[] pngFrame in pngFrames)
            writer.Write(pngFrame);
    }

    private static int[] NormalizeIconSizes(IReadOnlyCollection<int>? iconSizes)
    {
        int[] supportedSizes = DefaultIconSizes;
        if (iconSizes is null || iconSizes.Count == 0)
            return supportedSizes;

        int[] selectedSizes = supportedSizes
            .Where(iconSizes.Contains)
            .ToArray();

        return selectedSizes.Length == 0
            ? supportedSizes
            : selectedSizes;
    }

    public static void WritePng(string path, int size, LogoSettings settings)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        WritePng(path, RenderBitmap(size, settings));
    }

    private static void DrawLogo(DrawingContext drawingContext, int size, LogoSettings settings)
    {
        bool drawFacetLines = ShouldDrawFacetLines(settings, size);
        List<ProjectedFace> faces = CreateFaces(size, settings);

        foreach (ProjectedFace face in faces)
        {
            Color faceColor = ScaleBrightness(Blend(settings.PrimaryColor, settings.SecondaryColor, GetFadeMix(settings, face.ShadeMix)), face.Brightness);
            var fill = new SolidColorBrush(WithAlpha(faceColor, (byte)Math.Round(232 + face.Brightness * 23)));
            Pen? edgePen = !drawFacetLines || settings.EdgeShadowStrokeWidth <= 0
                ? null
                : new Pen(new SolidColorBrush(WithAlpha(settings.EdgeShadowColor, 92)), Math.Max(1, settings.EdgeShadowStrokeWidth * size / CanvasSize));
            drawingContext.DrawGeometry(fill, edgePen, CreateFaceGeometry(face.Points));
        }

        if (drawFacetLines)
        {
            foreach (ProjectedFace face in faces)
            {
                var ridgePen = new Pen(
                    new SolidColorBrush(WithAlpha(settings.RidgeColor, (byte)Math.Round(212 + face.Brightness * 36))),
                    Math.Max(1, settings.FacetStrokeWidth * size / CanvasSize));
                drawingContext.DrawGeometry(null, ridgePen, CreateFaceGeometry(face.Points));
            }
        }

        DrawCheckOverlay(drawingContext, size, settings);
    }

    private static void AppendCheckSvg(StringBuilder builder, LogoSettings settings)
    {
        if (!settings.ShowCheckOverlay)
            return;

        string path = CreateCheckSvgPath(CanvasSize, settings);
        double shadowWidth = GetCheckShadowStrokeWidth(CanvasSize, settings);
        double strokeWidth = GetCheckStrokeWidth(CanvasSize, settings);

        builder.AppendLine("""  <g fill="none" stroke-linecap="round" stroke-linejoin="round">""");
        builder.AppendLine(FormattableString.Invariant(
            $"""    <path d="{path}" stroke="{LogoSettings.ToHex(settings.EdgeShadowColor)}" stroke-opacity=".64" stroke-width="{shadowWidth:0.###}"/>"""));
        builder.AppendLine(FormattableString.Invariant(
            $"""    <path d="{path}" stroke="{LogoSettings.ToHex(settings.CheckColor)}" stroke-width="{strokeWidth:0.###}"/>"""));
        builder.AppendLine("  </g>");
    }

    private static void DrawCheckOverlay(DrawingContext drawingContext, int size, LogoSettings settings)
    {
        if (!settings.ShowCheckOverlay)
            return;

        Geometry geometry = CreateCheckGeometry(size, settings);
        drawingContext.DrawGeometry(null, CreateRoundPen(WithAlpha(settings.EdgeShadowColor, 164), GetCheckShadowStrokeWidth(size, settings)), geometry);
        drawingContext.DrawGeometry(null, CreateRoundPen(settings.CheckColor, GetCheckStrokeWidth(size, settings)), geometry);
    }

    private static Pen CreateRoundPen(Color color, double width) =>
        new(new SolidColorBrush(color), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

    private static Geometry CreateCheckGeometry(double size, LogoSettings settings)
    {
        Point[] points = CreateCheckPoints(size, settings);
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
        return new PathGeometry([figure]);
    }

    private static string CreateCheckSvgPath(double size, LogoSettings settings)
    {
        Point[] points = CreateCheckPoints(size, settings);
        return FormattableString.Invariant($"M{points[0].X:0.0} {points[0].Y:0.0} L{points[1].X:0.0} {points[1].Y:0.0} L{points[2].X:0.0} {points[2].Y:0.0}");
    }

    private static Point[] CreateCheckPoints(double size, LogoSettings settings)
    {
        double checkSize = size * Math.Clamp(settings.CheckScale, 0.16, 0.95);
        double centerX = size / 2 + Math.Clamp(settings.CheckOffsetX, -0.35, 0.35) * size;
        double centerY = size / 2 + Math.Clamp(settings.CheckOffsetY, -0.35, 0.35) * size;

        return
        [
            new Point(centerX - checkSize * 0.36, centerY + checkSize * 0.04),
            new Point(centerX - checkSize * 0.12, centerY + checkSize * 0.27),
            new Point(centerX + checkSize * 0.39, centerY - checkSize * 0.29)
        ];
    }

    private static double GetCheckStrokeWidth(double size, LogoSettings settings) =>
        Math.Max(1, size * Math.Clamp(settings.CheckScale, 0.16, 0.95) * 0.145);

    private static double GetCheckShadowStrokeWidth(double size, LogoSettings settings) =>
        GetCheckStrokeWidth(size, settings) * 1.62;

    private static List<ProjectedFace> CreateFaces(double size, LogoSettings settings)
    {
        PolyhedronModel model = GetModel(settings);
        double rotationX = DegreesToRadians(settings.RotationXDegrees);
        double rotationY = DegreesToRadians(settings.RotationYDegrees);
        double rotationZ = DegreesToRadians(settings.RotationZDegrees);

        Vector3[] rotatedVertices = model.Vertices
            .Select(vertex => Rotate(vertex, rotationX, rotationY, rotationZ))
            .ToArray();

        return model.Faces
            .Select(face => CreateProjectedFace(face, rotatedVertices, size, settings))
            .OrderBy(face => face.Depth)
            .ToList();
    }

    private static PolyhedronModel GetModel(LogoSettings settings) =>
        settings.FacetDetailLevel < 0.5 ? SimpleModel : DetailedModel;

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180;

    private static ProjectedFace CreateProjectedFace(int[] face, Vector3[] vertices, double size, LogoSettings settings)
    {
        Vector3 a = vertices[face[0]];
        Vector3 b = vertices[face[1]];
        Vector3 c = vertices[face[2]];
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        double facing = Math.Max(0, normal.Z);
        double brightness = Math.Clamp(0.36 + facing * 0.58, 0.28, 1);
        double shadeMix = Math.Clamp((normal.X + 1) * 0.42 + facing * 0.18, 0.1, 0.9);
        double depth = face.Average(index => vertices[index].Z);
        Point[] points = face.Select(index => Project(vertices[index], size, settings)).ToArray();

        return new ProjectedFace(points, depth, brightness, shadeMix);
    }

    private static Point Project(Vector3 point, double size, LogoSettings settings)
    {
        const double cameraDistance = 3.2;
        double perspective = cameraDistance / (cameraDistance - point.Z);
        double scale = size * GetCenterlineScale(settings, size);

        return new Point(
            size / 2 + point.X * scale * perspective,
            size / 2 - point.Y * scale * perspective);
    }

    private static double GetCenterlineScale(LogoSettings settings, double size)
    {
        if (!ShouldDrawFacetLines(settings, size))
            return Math.Clamp(settings.PolyhedronScale, 0.1, 0.5);

        double maxStrokeWidth = Math.Max(settings.FacetStrokeWidth, settings.EdgeShadowStrokeWidth);
        double strokeCompensation = maxStrokeWidth / (CanvasSize * 2) * Math.Clamp(settings.StrokeInsetScale, 0, 3);
        return Math.Clamp(settings.PolyhedronScale - strokeCompensation, 0.1, 0.5);
    }

    private static bool ShouldDrawFacetLines(LogoSettings settings, double size) =>
        !settings.HideFacetLinesAtAllSizes && size >= MinimumFacetLineSize;

    private static double GetFadeMix(LogoSettings settings, double shadeMix) =>
        Math.Clamp(shadeMix * Math.Clamp(settings.FadeStrength, 0, 2), 0, 1);

    private static Vector3 Rotate(Vector3 point, double angleX, double angleY, double angleZ)
    {
        double cosX = Math.Cos(angleX);
        double sinX = Math.Sin(angleX);
        double y1 = point.Y * cosX - point.Z * sinX;
        double z1 = point.Y * sinX + point.Z * cosX;

        double cosY = Math.Cos(angleY);
        double sinY = Math.Sin(angleY);
        double x2 = point.X * cosY + z1 * sinY;
        double z2 = -point.X * sinY + z1 * cosY;

        double cosZ = Math.Cos(angleZ);
        double sinZ = Math.Sin(angleZ);
        double x3 = x2 * cosZ - y1 * sinZ;
        double y3 = x2 * sinZ + y1 * cosZ;

        return new Vector3(x3, y3, z2);
    }

    private static Geometry CreateFaceGeometry(IReadOnlyList<Point> points)
    {
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };

        for (int i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment(points[i], true));

        return new PathGeometry([figure]);
    }

    private static PolyhedronModel CreateIcosahedron()
    {
        double phi = (1 + Math.Sqrt(5)) / 2;
        return Normalize(new PolyhedronModel(
            [
                new Vector3(-1, phi, 0),
                new Vector3(1, phi, 0),
                new Vector3(-1, -phi, 0),
                new Vector3(1, -phi, 0),
                new Vector3(0, -1, phi),
                new Vector3(0, 1, phi),
                new Vector3(0, -1, -phi),
                new Vector3(0, 1, -phi),
                new Vector3(phi, 0, -1),
                new Vector3(phi, 0, 1),
                new Vector3(-phi, 0, -1),
                new Vector3(-phi, 0, 1)
            ],
            [
                [0, 11, 5],
                [0, 5, 1],
                [0, 1, 7],
                [0, 7, 10],
                [0, 10, 11],
                [1, 5, 9],
                [5, 11, 4],
                [11, 10, 2],
                [10, 7, 6],
                [7, 1, 8],
                [3, 9, 4],
                [3, 4, 2],
                [3, 2, 6],
                [3, 6, 8],
                [3, 8, 9],
                [4, 9, 5],
                [2, 4, 11],
                [6, 2, 10],
                [8, 6, 7],
                [9, 8, 1]
            ]));
    }

    private static PolyhedronModel CreateSubdividedIcosahedron(int subdivisionCount)
    {
        PolyhedronModel model = CreateIcosahedron();

        for (int i = 0; i < subdivisionCount; i++)
            model = SubdivideTriangles(model);

        return Normalize(model);
    }

    private static PolyhedronModel SubdivideTriangles(PolyhedronModel model)
    {
        var vertices = model.Vertices.ToList();
        var midpointIndexes = new Dictionary<EdgeKey, int>();
        var faces = new List<int[]>();

        foreach (int[] face in model.Faces)
        {
            int a = face[0];
            int b = face[1];
            int c = face[2];
            int ab = GetMidpointIndex(a, b, vertices, midpointIndexes);
            int bc = GetMidpointIndex(b, c, vertices, midpointIndexes);
            int ca = GetMidpointIndex(c, a, vertices, midpointIndexes);

            faces.Add([a, ab, ca]);
            faces.Add([b, bc, ab]);
            faces.Add([c, ca, bc]);
            faces.Add([ab, bc, ca]);
        }

        return new PolyhedronModel(vertices.ToArray(), faces.ToArray());
    }

    private static int GetMidpointIndex(
        int firstIndex,
        int secondIndex,
        List<Vector3> vertices,
        Dictionary<EdgeKey, int> midpointIndexes)
    {
        var key = new EdgeKey(firstIndex, secondIndex);
        if (midpointIndexes.TryGetValue(key, out int existingIndex))
            return existingIndex;

        Vector3 midpoint = Vector3.Normalize((vertices[firstIndex] + vertices[secondIndex]) / 2);
        int midpointIndex = vertices.Count;
        vertices.Add(midpoint);
        midpointIndexes[key] = midpointIndex;

        return midpointIndex;
    }

    private static PolyhedronModel Normalize(PolyhedronModel model)
    {
        double maxLength = model.Vertices.Max(vertex => vertex.Length);
        Vector3[] vertices = model.Vertices
            .Select(vertex => vertex / maxLength)
            .ToArray();

        return model with { Vertices = vertices };
    }

    private static Color Blend(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(first.R + (second.R - first.R) * amount),
            (byte)(first.G + (second.G - first.G) * amount),
            (byte)(first.B + (second.B - first.B) * amount));
    }

    private static Color ScaleBrightness(Color color, double brightness)
    {
        brightness = Math.Clamp(brightness, 0, 1.2);
        return Color.FromRgb(
            (byte)Math.Clamp(color.R * brightness, 0, 255),
            (byte)Math.Clamp(color.G * brightness, 0, 255),
            (byte)Math.Clamp(color.B * brightness, 0, 255));
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static void WritePng(string path, BitmapSource bitmap)
    {
        using var file = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(file);
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "JoinGameAfk.sln")))
                return directory;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate JoinGameAfk.sln from the logo editor output directory.");
    }

    private sealed record PolyhedronModel(Vector3[] Vertices, int[][] Faces);

    private sealed record ProjectedFace(Point[] Points, double Depth, double Brightness, double ShadeMix);

    private sealed record SvgFace(string Path, Color FillColor, double FillOpacity, double StrokeOpacity);

    private readonly record struct EdgeKey
    {
        public EdgeKey(int firstIndex, int secondIndex)
        {
            FirstIndex = Math.Min(firstIndex, secondIndex);
            SecondIndex = Math.Max(firstIndex, secondIndex);
        }

        public int FirstIndex { get; }

        public int SecondIndex { get; }
    }

    private readonly record struct Vector3(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static Vector3 operator -(Vector3 left, Vector3 right) =>
            new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3 operator +(Vector3 left, Vector3 right) =>
            new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Vector3 operator *(Vector3 vector, double scalar) =>
            new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

        public static Vector3 operator /(Vector3 vector, double scalar) =>
            new(vector.X / scalar, vector.Y / scalar, vector.Z / scalar);

        public static Vector3 Cross(Vector3 left, Vector3 right) =>
            new(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);

        public static Vector3 Normalize(Vector3 vector)
        {
            double length = vector.Length;
            return length <= double.Epsilon
                ? new Vector3(0, 0, 1)
                : vector / length;
        }
    }
}

public sealed record LogoExportResult(string SvgPath, string IcoPath, string PreviewPath, string? PresetPath = null);
