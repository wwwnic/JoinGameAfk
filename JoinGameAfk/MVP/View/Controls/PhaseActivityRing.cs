using System.Windows;
using System.Windows.Media;

namespace JoinGameAfk.View.Controls
{
    public enum PhaseActivityRingMode
    {
        QueueOrbit,
        ReadyCheckOrbitPulse,
        ChampionSelectOrbit
    }

    public enum PhaseActivityRingProfile
    {
        Neutral,
        Ban,
        Pick,
        Planning,
        Finalization,
        Queue,
        ReadyCheck
    }

    public sealed class PhaseActivityRing : FrameworkElement
    {
        public static readonly DependencyProperty RingColorProperty =
            DependencyProperty.Register(
                nameof(RingColor),
                typeof(Color),
                typeof(PhaseActivityRing),
                new FrameworkPropertyMetadata(Color.FromRgb(34, 211, 238), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PulseColorProperty =
            DependencyProperty.Register(
                nameof(PulseColor),
                typeof(Color),
                typeof(PhaseActivityRing),
                new FrameworkPropertyMetadata(Color.FromArgb(220, 236, 254, 255), FrameworkPropertyMetadataOptions.AffectsRender));

        private DateTime _startedAtUtc = DateTime.UtcNow;
        private bool _shouldAnimate;
        private bool _isRendering;
        private PhaseActivityRingMode _mode = PhaseActivityRingMode.ChampionSelectOrbit;
        private PhaseActivityRingProfile _profile = PhaseActivityRingProfile.Neutral;

        public PhaseActivityRing()
        {
            Loaded += (_, _) =>
            {
                if (_shouldAnimate)
                    StartRendering();
            };

            Unloaded += (_, _) => StopRendering();
        }

        public Color RingColor
        {
            get => (Color)GetValue(RingColorProperty);
            set => SetValue(RingColorProperty, value);
        }

        public Color PulseColor
        {
            get => (Color)GetValue(PulseColorProperty);
            set => SetValue(PulseColorProperty, value);
        }

        public void Start(PhaseActivityRingMode mode, PhaseActivityRingProfile profile)
        {
            bool restartLoop = !_shouldAnimate || _mode != mode;

            _mode = mode;
            _profile = profile;
            _shouldAnimate = true;

            if (restartLoop)
                _startedAtUtc = DateTime.UtcNow;

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
            if (!_shouldAnimate || width <= 0 || height <= 0)
                return;

            double size = Math.Min(width, height);
            if (size <= 2)
                return;

            Point center = new(width / 2, height / 2);
            double elapsedSeconds = (DateTime.UtcNow - _startedAtUtc).TotalSeconds;

            if (_mode == PhaseActivityRingMode.ReadyCheckOrbitPulse)
            {
                DrawReadyCheckOrbitPulse(drawingContext, center, size, elapsedSeconds);
                return;
            }

            if (_mode == PhaseActivityRingMode.QueueOrbit)
            {
                DrawQueueOrbit(drawingContext, center, size, elapsedSeconds);
                return;
            }

            DrawChampionSelectOrbit(drawingContext, center, size, elapsedSeconds);
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

        private void DrawReadyCheckOrbitPulse(DrawingContext drawingContext, Point center, double size, double elapsedSeconds)
        {
            const double loopSeconds = 0.95;
            double loop = GetLoop(elapsedSeconds, loopSeconds);
            double spinDegrees = elapsedSeconds * 360 / loopSeconds;
            double orbitRadius = Math.Floor(size * 0.39);

            DrawBaseRing(drawingContext, center, orbitRadius, RingColor, 28, 1.0);
            DrawPulseRing(drawingContext, center, size, loop, PulseColor, 0.24, 0.45, 92, 1.0);
            DrawArc(drawingContext, center, orbitRadius, -90 + spinDegrees, 112, RingColor, 226, 1.0);
            DrawArc(drawingContext, center, orbitRadius, 90 + spinDegrees, 112, RingColor, 226, 1.0);
        }

        private void DrawQueueOrbit(DrawingContext drawingContext, Point center, double size, double elapsedSeconds)
        {
            const double loopSeconds = 1.35;
            double spinDegrees = elapsedSeconds * 360 / loopSeconds;
            double orbitRadius = Math.Floor(size * 0.39);

            DrawBaseRing(drawingContext, center, orbitRadius, RingColor, 22, 1.0);
            DrawArc(drawingContext, center, orbitRadius, -90 + spinDegrees, 96, RingColor, 218, 1.0);
        }

        private void DrawChampionSelectOrbit(DrawingContext drawingContext, Point center, double size, double elapsedSeconds)
        {
            RingProfile profile = GetProfile(_profile);
            double orbitDegrees = elapsedSeconds * 360 / profile.OrbitSeconds;
            double pulseLoop = GetLoop(elapsedSeconds + profile.PulseOffsetSeconds, profile.PulseSeconds);

            DrawBaseRing(drawingContext, center, size * profile.RadiusRatio, RingColor, profile.BaseAlpha, 0.8);
            DrawPulseRing(
                drawingContext,
                center,
                size,
                pulseLoop,
                PulseColor,
                profile.PulseStartRatio,
                profile.PulseEndRatio,
                profile.PulseAlpha,
                profile.PulseThickness);

            DrawArc(
                drawingContext,
                center,
                size * profile.RadiusRatio,
                -90 + orbitDegrees,
                profile.SweepDegrees,
                RingColor,
                profile.ArcAlpha,
                profile.ArcThickness);

            DrawArc(
                drawingContext,
                center,
                size * profile.RadiusRatio,
                -112 + orbitDegrees,
                profile.SweepDegrees * 0.55,
                RingColor,
                profile.TailAlpha,
                Math.Max(0.7, profile.ArcThickness - 0.35));
        }

        private void DrawBaseRing(DrawingContext drawingContext, Point center, double radius, Color color, byte alpha, double thickness)
        {
            var pen = CreatePen(color, alpha, thickness);
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
        }

        private void DrawPulseRing(
            DrawingContext drawingContext,
            Point center,
            double size,
            double loop,
            Color color,
            double startRatio,
            double endRatio,
            byte baseAlpha,
            double thickness)
        {
            double easedLoop = EaseOutCubic(loop);
            double radius = size * (startRatio + (endRatio - startRatio) * easedLoop);
            byte alpha = ScaleAlpha(baseAlpha, 1 - EaseInCubic(loop));
            if (alpha <= 3)
                return;

            var pen = CreatePen(color, alpha, Math.Max(0.55, thickness - loop * 0.25));
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
        }

        private static void DrawArc(
            DrawingContext drawingContext,
            Point center,
            double radius,
            double startDegrees,
            double sweepDegrees,
            Color color,
            byte alpha,
            double thickness)
        {
            if (radius <= 0 || sweepDegrees <= 0 || alpha == 0)
                return;

            var pen = CreatePen(color, alpha, thickness);
            drawingContext.DrawGeometry(null, pen, CreateArcGeometry(center, radius, startDegrees, Math.Min(sweepDegrees, 359.5)));
        }

        private static Pen CreatePen(Color color, byte alpha, double thickness)
        {
            var brush = new SolidColorBrush(WithAlpha(color, alpha));
            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (brush.CanFreeze)
                brush.Freeze();
            if (pen.CanFreeze)
                pen.Freeze();

            return pen;
        }

        private static Geometry CreateArcGeometry(Point center, double radius, double startDegrees, double sweepDegrees)
        {
            Point startPoint = PointOnCircle(center, radius, startDegrees);
            Point endPoint = PointOnCircle(center, radius, startDegrees + sweepDegrees);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(startPoint, isFilled: false, isClosed: false);
                context.ArcTo(
                    endPoint,
                    new Size(radius, radius),
                    rotationAngle: 0,
                    isLargeArc: sweepDegrees > 180,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: true);
            }

            if (geometry.CanFreeze)
                geometry.Freeze();

            return geometry;
        }

        private static Point PointOnCircle(Point center, double radius, double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new Point(
                center.X + Math.Cos(radians) * radius,
                center.Y + Math.Sin(radians) * radius);
        }

        private static RingProfile GetProfile(PhaseActivityRingProfile profile)
        {
            return profile switch
            {
                PhaseActivityRingProfile.Ban => new(
                    OrbitSeconds: 0.92,
                    PulseSeconds: 1.05,
                    PulseOffsetSeconds: 0,
                    RadiusRatio: 0.405,
                    SweepDegrees: 126,
                    ArcThickness: 1.45,
                    PulseThickness: 1.05,
                    ArcAlpha: 232,
                    TailAlpha: 88,
                    PulseAlpha: 96,
                    BaseAlpha: 22,
                    PulseStartRatio: 0.23,
                    PulseEndRatio: 0.47),
                PhaseActivityRingProfile.Pick => new(
                    OrbitSeconds: 1.14,
                    PulseSeconds: 1.18,
                    PulseOffsetSeconds: 0.08,
                    RadiusRatio: 0.405,
                    SweepDegrees: 116,
                    ArcThickness: 1.35,
                    PulseThickness: 0.95,
                    ArcAlpha: 224,
                    TailAlpha: 78,
                    PulseAlpha: 86,
                    BaseAlpha: 20,
                    PulseStartRatio: 0.24,
                    PulseEndRatio: 0.46),
                PhaseActivityRingProfile.Finalization => new(
                    OrbitSeconds: 1.52,
                    PulseSeconds: 1.42,
                    PulseOffsetSeconds: 0.14,
                    RadiusRatio: 0.39,
                    SweepDegrees: 104,
                    ArcThickness: 1.25,
                    PulseThickness: 0.85,
                    ArcAlpha: 216,
                    TailAlpha: 68,
                    PulseAlpha: 76,
                    BaseAlpha: 18,
                    PulseStartRatio: 0.25,
                    PulseEndRatio: 0.43),
                PhaseActivityRingProfile.Planning => new(
                    OrbitSeconds: 1.38,
                    PulseSeconds: 1.28,
                    PulseOffsetSeconds: 0.05,
                    RadiusRatio: 0.40,
                    SweepDegrees: 112,
                    ArcThickness: 1.25,
                    PulseThickness: 0.9,
                    ArcAlpha: 214,
                    TailAlpha: 72,
                    PulseAlpha: 80,
                    BaseAlpha: 18,
                    PulseStartRatio: 0.24,
                    PulseEndRatio: 0.45),
                _ => new(
                    OrbitSeconds: 1.32,
                    PulseSeconds: 1.25,
                    PulseOffsetSeconds: 0,
                    RadiusRatio: 0.40,
                    SweepDegrees: 110,
                    ArcThickness: 1.25,
                    PulseThickness: 0.9,
                    ArcAlpha: 210,
                    TailAlpha: 70,
                    PulseAlpha: 78,
                    BaseAlpha: 18,
                    PulseStartRatio: 0.24,
                    PulseEndRatio: 0.45)
            };
        }

        private static double GetLoop(double elapsedSeconds, double loopSeconds)
        {
            if (loopSeconds <= double.Epsilon)
                return 0;

            double loop = elapsedSeconds % loopSeconds;
            return loop < 0
                ? (loop + loopSeconds) / loopSeconds
                : loop / loopSeconds;
        }

        private static double EaseOutCubic(double value)
        {
            value = Math.Clamp(value, 0, 1);
            double inverse = 1 - value;
            return 1 - inverse * inverse * inverse;
        }

        private static double EaseInCubic(double value)
        {
            value = Math.Clamp(value, 0, 1);
            return value * value * value;
        }

        private static byte ScaleAlpha(byte alpha, double amount) =>
            (byte)Math.Clamp(alpha * Math.Clamp(amount, 0, 1), 0, 255);

        private static Color WithAlpha(Color color, byte alpha)
        {
            byte effectiveAlpha = (byte)(color.A * alpha / 255);
            return Color.FromArgb(effectiveAlpha, color.R, color.G, color.B);
        }

        private sealed record RingProfile(
            double OrbitSeconds,
            double PulseSeconds,
            double PulseOffsetSeconds,
            double RadiusRatio,
            double SweepDegrees,
            double ArcThickness,
            double PulseThickness,
            byte ArcAlpha,
            byte TailAlpha,
            byte PulseAlpha,
            byte BaseAlpha,
            double PulseStartRatio,
            double PulseEndRatio);
    }
}
