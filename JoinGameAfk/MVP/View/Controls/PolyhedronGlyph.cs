using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.View.Controls
{
    public sealed class PolyhedronGlyph : FrameworkElement
    {
        public static readonly DependencyProperty PrimaryColorProperty =
            DependencyProperty.Register(
                nameof(PrimaryColor),
                typeof(Color),
                typeof(PolyhedronGlyph),
                new FrameworkPropertyMetadata(Color.FromRgb(34, 211, 238), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SecondaryColorProperty =
            DependencyProperty.Register(
                nameof(SecondaryColor),
                typeof(Color),
                typeof(PolyhedronGlyph),
                new FrameworkPropertyMetadata(Color.FromRgb(124, 58, 237), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RidgeColorProperty =
            DependencyProperty.Register(
                nameof(RidgeColor),
                typeof(Color),
                typeof(PolyhedronGlyph),
                new FrameworkPropertyMetadata(Color.FromArgb(220, 236, 254, 255), FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly PolyhedronModel[] Models =
        [
            CreateSubdividedIcosahedron(subdivisionCount: 1)
        ];

        private readonly DateTime _createdAtUtc = DateTime.UtcNow;
        private bool _shouldAnimate;
        private bool _isRendering;

        public PolyhedronGlyph()
        {
            Loaded += (_, _) =>
            {
                if (_shouldAnimate)
                    StartRendering();
            };

            Unloaded += (_, _) => StopRendering();
        }

        public Color PrimaryColor
        {
            get => (Color)GetValue(PrimaryColorProperty);
            set => SetValue(PrimaryColorProperty, value);
        }

        public Color SecondaryColor
        {
            get => (Color)GetValue(SecondaryColorProperty);
            set => SetValue(SecondaryColorProperty, value);
        }

        public Color RidgeColor
        {
            get => (Color)GetValue(RidgeColorProperty);
            set => SetValue(RidgeColorProperty, value);
        }

        public void Start()
        {
            _shouldAnimate = true;
            if (IsLoaded)
                StartRendering();

            InvalidateVisual();
        }

        public void Stop()
        {
            _shouldAnimate = false;
            StopRendering();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            double elapsedSeconds = _shouldAnimate
                ? (DateTime.UtcNow - _createdAtUtc).TotalSeconds
                : 0;

            DrawPulse(drawingContext, width, height, elapsedSeconds);

            double angleY = elapsedSeconds * 1.32;
            double angleX = 0.58 + Math.Sin(elapsedSeconds * 0.74) * 0.24;
            double angleZ = elapsedSeconds * 0.34;

            DrawModel(drawingContext, Models[0], width, height, angleX, angleY, angleZ, 1);
        }

        private void StartRendering()
        {
            if (_isRendering)
                return;

            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _isRendering = true;
        }

        private void StopRendering()
        {
            if (!_isRendering)
                return;

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isRendering = false;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            InvalidateVisual();
        }

        private void DrawPulse(DrawingContext drawingContext, double width, double height, double elapsedSeconds)
        {
            double pulse = 0.5 + Math.Sin(elapsedSeconds * 2.2) * 0.5;
            double radius = Math.Min(width, height) * (0.4 + pulse * 0.08);
            byte alpha = (byte)(34 + pulse * 34);
            var pen = new Pen(new SolidColorBrush(WithAlpha(RidgeColor, alpha)), 0.7);
            drawingContext.DrawEllipse(null, pen, new Point(width / 2, height / 2), radius, radius);
        }

        private void DrawModel(
            DrawingContext drawingContext,
            PolyhedronModel model,
            double width,
            double height,
            double angleX,
            double angleY,
            double angleZ,
            double opacity)
        {
            if (opacity <= 0.01)
                return;

            Vector3[] rotatedVertices = model.Vertices
                .Select(vertex => Rotate(vertex, angleX, angleY, angleZ))
                .ToArray();

            var faces = model.Faces
                .Select(face => CreateProjectedFace(face, rotatedVertices, width, height))
                .OrderBy(face => face.Depth)
                .ToList();

            drawingContext.PushOpacity(opacity);

            foreach (ProjectedFace face in faces)
            {
                Color faceColor = Blend(PrimaryColor, SecondaryColor, face.ShadeMix);
                faceColor = ScaleBrightness(faceColor, face.Brightness);

                var fillBrush = new SolidColorBrush(WithAlpha(faceColor, (byte)(150 + face.Brightness * 72)));
                var edgePen = new Pen(new SolidColorBrush(WithAlpha(RidgeColor, (byte)(135 + face.Brightness * 80))), 0.62);
                drawingContext.DrawGeometry(fillBrush, edgePen, CreateFaceGeometry(face.Points));
            }

            drawingContext.Pop();
        }

        private static ProjectedFace CreateProjectedFace(int[] face, Vector3[] vertices, double width, double height)
        {
            Vector3 a = vertices[face[0]];
            Vector3 b = vertices[face[1]];
            Vector3 c = vertices[face[2]];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            double facing = Math.Max(0, normal.Z);
            double brightness = Math.Clamp(0.36 + facing * 0.58, 0.28, 1);
            double shadeMix = Math.Clamp((normal.X + 1) * 0.42 + facing * 0.18, 0.1, 0.9);
            double depth = face.Average(index => vertices[index].Z);
            Point[] points = face.Select(index => Project(vertices[index], width, height)).ToArray();

            return new ProjectedFace(points, depth, brightness, shadeMix);
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

        private static Point Project(Vector3 point, double width, double height)
        {
            const double cameraDistance = 3.2;
            double perspective = cameraDistance / (cameraDistance - point.Z);
            double scale = Math.Min(width, height) * 0.34;

            return new Point(
                width / 2 + point.X * scale * perspective,
                height / 2 - point.Y * scale * perspective);
        }

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

        private static PolyhedronModel CreateGem(int sideCount, double equatorRadius, double capRadius, double twist)
        {
            var vertices = new List<Vector3>();
            for (int i = 0; i < sideCount; i++)
                vertices.Add(CreateRingVertex(sideCount, i, equatorRadius, 0, 0));

            for (int i = 0; i < sideCount; i++)
                vertices.Add(CreateRingVertex(sideCount, i, capRadius, 0.72, twist));

            for (int i = 0; i < sideCount; i++)
                vertices.Add(CreateRingVertex(sideCount, i, capRadius, -0.72, -twist));

            var faces = new List<int[]>
            {
                Enumerable.Range(sideCount, sideCount).Reverse().ToArray(),
                Enumerable.Range(sideCount * 2, sideCount).ToArray()
            };

            for (int i = 0; i < sideCount; i++)
            {
                int next = (i + 1) % sideCount;
                faces.Add([i, next, sideCount + next, sideCount + i]);
                faces.Add([sideCount * 2 + i, sideCount * 2 + next, next, i]);
            }

            return Normalize(new PolyhedronModel(vertices.ToArray(), faces.ToArray()));
        }

        private static Vector3 CreateRingVertex(int sideCount, int index, double radius, double y, double twist)
        {
            double angle = Math.PI * 2 * index / sideCount + twist;
            return new Vector3(Math.Cos(angle) * radius, y, Math.Sin(angle) * radius);
        }

        private static PolyhedronModel CreateTetrahedron()
        {
            return Normalize(new PolyhedronModel(
                [
                    new Vector3(1, 1, 1),
                    new Vector3(-1, -1, 1),
                    new Vector3(-1, 1, -1),
                    new Vector3(1, -1, -1)
                ],
                [
                    [0, 1, 2],
                    [0, 3, 1],
                    [0, 2, 3],
                    [1, 3, 2]
                ]));
        }

        private static PolyhedronModel CreateOctahedron()
        {
            return Normalize(new PolyhedronModel(
                [
                    new Vector3(1, 0, 0),
                    new Vector3(-1, 0, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(0, -1, 0),
                    new Vector3(0, 0, 1),
                    new Vector3(0, 0, -1)
                ],
                [
                    [0, 2, 4],
                    [2, 1, 4],
                    [1, 3, 4],
                    [3, 0, 4],
                    [2, 0, 5],
                    [1, 2, 5],
                    [3, 1, 5],
                    [0, 3, 5]
                ]));
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

        private static PolyhedronModel CreateLatitudeSphere(int sideCount, int latitudeRingCount)
        {
            var vertices = new List<Vector3>
            {
                new(0, 1, 0)
            };

            for (int ring = 1; ring <= latitudeRingCount; ring++)
            {
                double latitude = Math.PI * ring / (latitudeRingCount + 1);
                double y = Math.Cos(latitude);
                double radius = Math.Sin(latitude);
                double twist = ring % 2 == 0
                    ? Math.PI / sideCount
                    : 0;

                for (int side = 0; side < sideCount; side++)
                    vertices.Add(CreateRingVertex(sideCount, side, radius, y, twist));
            }

            int bottomIndex = vertices.Count;
            vertices.Add(new Vector3(0, -1, 0));

            var faces = new List<int[]>();
            int firstRingStart = 1;
            int lastRingStart = 1 + (latitudeRingCount - 1) * sideCount;

            for (int side = 0; side < sideCount; side++)
            {
                int next = (side + 1) % sideCount;
                faces.Add([0, firstRingStart + side, firstRingStart + next]);
            }

            for (int ring = 0; ring < latitudeRingCount - 1; ring++)
            {
                int upperStart = 1 + ring * sideCount;
                int lowerStart = upperStart + sideCount;

                for (int side = 0; side < sideCount; side++)
                {
                    int next = (side + 1) % sideCount;
                    faces.Add([upperStart + side, lowerStart + side, lowerStart + next]);
                    faces.Add([upperStart + side, lowerStart + next, upperStart + next]);
                }
            }

            for (int side = 0; side < sideCount; side++)
            {
                int next = (side + 1) % sideCount;
                faces.Add([bottomIndex, lastRingStart + next, lastRingStart + side]);
            }

            return Normalize(new PolyhedronModel(vertices.ToArray(), faces.ToArray()));
        }

        private static PolyhedronModel SubdivideTriangles(PolyhedronModel model)
        {
            var vertices = model.Vertices.ToList();
            var midpointIndexes = new Dictionary<EdgeKey, int>();
            var faces = new List<int[]>();

            foreach (int[] face in model.Faces)
            {
                if (face.Length != 3)
                    continue;

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

        private static PolyhedronModel CreateDodecahedron()
        {
            PolyhedronModel icosahedron = CreateIcosahedron();
            Vector3[] faceCenters = icosahedron.Faces
                .Select(face => Vector3.Normalize(AverageVertices(icosahedron.Vertices, face)))
                .ToArray();

            int[][] faces = icosahedron.Vertices
                .Select((vertex, vertexIndex) => CreateDualFace(vertex, vertexIndex, icosahedron.Faces, faceCenters))
                .ToArray();

            return Normalize(new PolyhedronModel(faceCenters, faces));
        }

        private static int[] CreateDualFace(Vector3 vertex, int vertexIndex, int[][] sourceFaces, Vector3[] faceCenters)
        {
            Vector3 normal = Vector3.Normalize(vertex);
            Vector3 axis = Math.Abs(normal.Z) > 0.88
                ? new Vector3(0, 1, 0)
                : new Vector3(0, 0, 1);
            Vector3 basisU = Vector3.Normalize(Vector3.Cross(axis, normal));
            Vector3 basisV = Vector3.Cross(normal, basisU);

            int[] face = sourceFaces
                .Select((sourceFace, faceIndex) => new
                {
                    FaceIndex = faceIndex,
                    ContainsVertex = sourceFace.Contains(vertexIndex)
                })
                .Where(item => item.ContainsVertex)
                .Select(item =>
                {
                    Vector3 tangent = faceCenters[item.FaceIndex] - normal * Vector3.Dot(faceCenters[item.FaceIndex], normal);
                    double angle = Math.Atan2(Vector3.Dot(tangent, basisV), Vector3.Dot(tangent, basisU));
                    return new { item.FaceIndex, Angle = angle };
                })
                .OrderBy(item => item.Angle)
                .Select(item => item.FaceIndex)
                .ToArray();

            if (face.Length >= 3)
            {
                Vector3 a = faceCenters[face[0]];
                Vector3 b = faceCenters[face[1]];
                Vector3 c = faceCenters[face[2]];
                Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                if (Vector3.Dot(faceNormal, normal) < 0)
                    Array.Reverse(face);
            }

            return face;
        }

        private static Vector3 AverageVertices(Vector3[] vertices, int[] indexes)
        {
            Vector3 sum = new(0, 0, 0);
            foreach (int index in indexes)
                sum += vertices[index];

            return sum / indexes.Length;
        }

        private static PolyhedronModel Normalize(PolyhedronModel model)
        {
            double maxLength = model.Vertices.Max(vertex => vertex.Length);
            Vector3[] vertices = model.Vertices
                .Select(vertex => vertex / maxLength)
                .ToArray();

            return model with { Vertices = vertices };
        }

        private static double SmoothStep(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return value * value * (3 - 2 * value);
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

        private sealed record PolyhedronModel(Vector3[] Vertices, int[][] Faces);

        private sealed record ProjectedFace(Point[] Points, double Depth, double Brightness, double ShadeMix);

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

            public static double Dot(Vector3 left, Vector3 right) =>
                left.X * right.X + left.Y * right.Y + left.Z * right.Z;

            public static Vector3 Normalize(Vector3 vector)
            {
                double length = vector.Length;
                return length <= double.Epsilon
                    ? new Vector3(0, 0, 1)
                    : vector / length;
            }
        }
    }
}
