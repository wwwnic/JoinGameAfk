using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoinGameAfk.Enums;
using JoinGameAfk.Theme;

namespace JoinGameAfk.View.Controls
{
    public partial class PhaseStatusIndicator : UserControl
    {
        private static readonly Duration ChampionColorTransitionDuration = new(TimeSpan.FromMilliseconds(220));

        private bool _isChampionAnimationRunning;
        private ChampionAnimationPalette? _activeChampionAnimationPalette;
        private ClientPhase _phase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private string _champSelectSubPhase = string.Empty;
        private bool _isThemeRefreshSubscribed;
        private PhaseActivityRingMode? _activeActivityRingMode;
        private PhaseActivityRingProfile? _activeActivityRingProfile;

        private enum ChampionAnimationPalette
        {
            Neutral,
            Ban,
            Pick,
            Planning,
            Finalization
        }

        private enum ReadyCheckResponseState
        {
            Pending,
            Accepted,
            Declined
        }

        public PhaseStatusIndicator()
        {
            InitializeComponent();

            Loaded += PhaseStatusIndicator_Loaded;
            Unloaded += PhaseStatusIndicator_Unloaded;
            IndicatorContent.SizeChanged += (_, _) => RefreshIndicatorGeometry();
        }

        public void Update(
            ClientPhase phase,
            bool isWatcherRunning,
            bool isClientConnected,
            string champSelectSubPhase)
        {
            _phase = phase;
            _isWatcherRunning = isWatcherRunning;
            _isClientConnected = isClientConnected;
            _champSelectSubPhase = champSelectSubPhase;

            Refresh();
        }

        public void Refresh()
        {
            RefreshIndicatorGeometry();

            if (ShouldShowCompletedChampionIndicator())
            {
                ShowCompletedChampionIndicator();
                return;
            }

            if (ShouldShowChampionAnimation())
            {
                var palette = GetChampionAnimationPalette();

                if (IsLoaded)
                    ShowChampionAnimation(palette);
                else
                    ShowChampionGlyphWithoutAnimation(palette);

                return;
            }

            if (ShouldShowReadyCheckAnimation())
            {
                ShowReadyCheckAnimation();
                return;
            }

            if (ShouldShowQueueAnimation())
            {
                ShowQueueAnimation();
                return;
            }

            StopChampionAnimation();
            StopActivityRing();
            HideReadyCheckResponseGlyph();
            CompletionCheck.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Collapsed;
            PhaseCircle.Visibility = Visibility.Visible;
            PhaseCircle.Fill = GetPhaseBrush();
        }

        private bool ShouldShowChampionAnimation()
        {
            return _isWatcherRunning
                && _isClientConnected
                && _phase is ClientPhase.ChampSelect or ClientPhase.Planning;
        }

        private bool ShouldShowCompletedChampionIndicator()
        {
            return _isWatcherRunning
                && _isClientConnected
                && _phase is ClientPhase.ChampSelect or ClientPhase.Planning
                && IsCompletedSubPhase(_champSelectSubPhase);
        }

        private bool ShouldShowReadyCheckAnimation()
        {
            return _isWatcherRunning
                && _isClientConnected
                && _phase == ClientPhase.ReadyCheck;
        }

        private bool ShouldShowQueueAnimation()
        {
            return _isWatcherRunning
                && _isClientConnected
                && _phase == ClientPhase.Matchmaking;
        }

        private Brush GetPhaseBrush()
        {
            if (!_isWatcherRunning)
                return ResourceBrush("PhaseDefaultBrush", Brushes.White);

            if (!_isClientConnected)
                return ResourceBrush("PhaseBanBrush", Brushes.IndianRed);

            return _phase switch
            {
                ClientPhase.Lobby => ResourceBrush("PhaseHoverBrush", Brushes.Goldenrod),
                ClientPhase.Matchmaking => ResourceBrush("PhaseLobbyBrush", Brushes.DodgerBlue),
                ClientPhase.ReadyCheck => ResourceBrush("PhaseReadyCheckBrush", Brushes.ForestGreen),
                ClientPhase.ChampSelect => ResourceBrush("AccentBlueBrush", Brushes.DodgerBlue),
                ClientPhase.Planning => ResourceBrush("PhaseHoverBrush", Brushes.DarkOrange),
                ClientPhase.InGame => ResourceBrush("PhaseDefaultBrush", Brushes.White),
                _ => ResourceBrush("PhaseDefaultBrush", Brushes.White)
            };
        }

        private ChampionAnimationPalette GetChampionAnimationPalette()
        {
            return _champSelectSubPhase switch
            {
                "Ban" => ChampionAnimationPalette.Ban,
                "Pick" => ChampionAnimationPalette.Pick,
                "Planning" or "Hover" => ChampionAnimationPalette.Planning,
                "Finalization" => ChampionAnimationPalette.Finalization,
                _ => ChampionAnimationPalette.Neutral
            };
        }

        private void ShowChampionGlyphWithoutAnimation(ChampionAnimationPalette palette)
        {
            PhaseCircle.Visibility = Visibility.Collapsed;
            HideReadyCheckResponseGlyph();
            CompletionCheck.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Visible;
            ApplyChampionAnimationPalette(palette, animate: false);
            ShowChampionActivityRing(palette, animate: false);
            _activeChampionAnimationPalette = palette;
        }

        private void ShowChampionAnimation(ChampionAnimationPalette palette)
        {
            PhaseCircle.Visibility = Visibility.Collapsed;
            HideReadyCheckResponseGlyph();
            CompletionCheck.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Visible;

            bool paletteChanged = _activeChampionAnimationPalette != palette;

            ApplyChampionAnimationPalette(palette, animate: _isChampionAnimationRunning && paletteChanged);
            ShowChampionActivityRing(palette, animate: ActivityRing.Visibility == Visibility.Visible && paletteChanged);

            if (_isChampionAnimationRunning)
            {
                _activeChampionAnimationPalette = palette;
                return;
            }

            ChampionPolyhedron.Start();
            _isChampionAnimationRunning = true;
            _activeChampionAnimationPalette = palette;
        }

        private void ShowReadyCheckAnimation()
        {
            ReadyCheckResponseState responseState = GetReadyCheckResponseState();
            if (responseState == ReadyCheckResponseState.Accepted)
            {
                ShowReadyCheckAcceptedIndicator();
                return;
            }

            if (responseState == ReadyCheckResponseState.Declined)
            {
                ShowReadyCheckDeclinedIndicator();
                return;
            }

            StopChampionAnimation();

            Color readyCheckColor = ResourceColor("PhaseReadyCheckBrush", Colors.ForestGreen);

            ChampionGlyph.Visibility = Visibility.Collapsed;
            CompletionCheck.Visibility = Visibility.Collapsed;
            HideReadyCheckResponseGlyph();
            PhaseCircle.Visibility = Visibility.Visible;
            PhaseCircle.Fill = ResourceBrush("PhaseReadyCheckBrush", Brushes.ForestGreen);

            ShowReadyCheckActivityRing(readyCheckColor, animateColor: false);
        }

        private void ShowReadyCheckAcceptedIndicator()
        {
            StopChampionAnimation();

            Color readyCheckColor = ResourceColor("PhaseReadyCheckBrush", Colors.ForestGreen);

            ChampionGlyph.Visibility = Visibility.Collapsed;
            CompletionCheck.Visibility = Visibility.Collapsed;
            PhaseCircle.Visibility = Visibility.Collapsed;
            ReadyCheckResponseGlyph.Visibility = Visibility.Visible;
            ReadyCheckAcceptedGlyph.Visibility = Visibility.Visible;
            ReadyCheckDeclinedGlyph.Visibility = Visibility.Collapsed;

            ShowReadyCheckActivityRing(readyCheckColor, animateColor: false);
        }

        private void ShowReadyCheckDeclinedIndicator()
        {
            StopChampionAnimation();

            Color declinedColor = ResourceColor("PhaseBanBrush", Colors.IndianRed);

            ChampionGlyph.Visibility = Visibility.Collapsed;
            CompletionCheck.Visibility = Visibility.Collapsed;
            PhaseCircle.Visibility = Visibility.Collapsed;
            ReadyCheckResponseGlyph.Visibility = Visibility.Visible;
            ReadyCheckAcceptedGlyph.Visibility = Visibility.Collapsed;
            ReadyCheckDeclinedGlyph.Visibility = Visibility.Visible;

            ShowReadyCheckActivityRing(declinedColor, animateColor: true);
        }

        private void ShowReadyCheckActivityRing(Color color, bool animateColor)
        {
            const PhaseActivityRingMode mode = PhaseActivityRingMode.ReadyCheckOrbitPulse;
            const PhaseActivityRingProfile profile = PhaseActivityRingProfile.ReadyCheck;
            bool activityRingStateChanged = _activeActivityRingMode != mode || _activeActivityRingProfile != profile;
            bool shouldAnimateRingColor = ActivityRing.Visibility == Visibility.Visible
                && (animateColor || activityRingStateChanged);

            ApplyActivityRingColors(color, color, shouldAnimateRingColor);
            ActivityRing.Visibility = Visibility.Visible;
            ActivityRing.Start(mode, profile);
            _activeActivityRingMode = mode;
            _activeActivityRingProfile = profile;
        }

        private void ShowQueueAnimation()
        {
            StopChampionAnimation();

            Color queueColor = ResourceColor("PhaseLobbyBrush", Colors.DodgerBlue);
            const PhaseActivityRingMode mode = PhaseActivityRingMode.QueueOrbit;
            const PhaseActivityRingProfile profile = PhaseActivityRingProfile.Queue;
            bool activityRingStateChanged = _activeActivityRingMode != mode || _activeActivityRingProfile != profile;
            bool shouldAnimateRingColor = ActivityRing.Visibility == Visibility.Visible && activityRingStateChanged;

            ChampionGlyph.Visibility = Visibility.Collapsed;
            CompletionCheck.Visibility = Visibility.Collapsed;
            HideReadyCheckResponseGlyph();
            PhaseCircle.Visibility = Visibility.Visible;
            PhaseCircle.Fill = ResourceBrush("PhaseLobbyBrush", Brushes.DodgerBlue);

            ApplyActivityRingColors(queueColor, queueColor, shouldAnimateRingColor);
            ActivityRing.Visibility = Visibility.Visible;
            ActivityRing.Start(mode, profile);
            _activeActivityRingMode = mode;
            _activeActivityRingProfile = profile;
        }

        private void ShowCompletedChampionIndicator()
        {
            ChampionAnimationPalette palette = GetCompletionPalette(_champSelectSubPhase);
            bool paletteChanged = _activeChampionAnimationPalette != palette;

            PhaseCircle.Visibility = Visibility.Collapsed;
            HideReadyCheckResponseGlyph();
            ChampionGlyph.Visibility = Visibility.Visible;
            CompletionCheck.Visibility = Visibility.Visible;

            ApplyChampionAnimationPalette(palette, animate: _isChampionAnimationRunning && paletteChanged);
            ShowChampionActivityRing(palette, animate: ActivityRing.Visibility == Visibility.Visible && paletteChanged);

            if (_isChampionAnimationRunning)
            {
                _activeChampionAnimationPalette = palette;
                return;
            }

            ChampionPolyhedron.Start();
            _isChampionAnimationRunning = true;
            _activeChampionAnimationPalette = palette;
        }

        private void StopChampionAnimation()
        {
            if (!_isChampionAnimationRunning)
                return;

            ChampionPolyhedron.Stop();
            _isChampionAnimationRunning = false;
            _activeChampionAnimationPalette = null;
        }

        private void StopActivityRing()
        {
            ActivityRing.Stop();
            ActivityRing.Visibility = Visibility.Collapsed;
            _activeActivityRingMode = null;
            _activeActivityRingProfile = null;
        }

        private void HideReadyCheckResponseGlyph()
        {
            ReadyCheckResponseGlyph.Visibility = Visibility.Collapsed;
            ReadyCheckAcceptedGlyph.Visibility = Visibility.Collapsed;
            ReadyCheckDeclinedGlyph.Visibility = Visibility.Collapsed;
        }

        private ReadyCheckResponseState GetReadyCheckResponseState()
        {
            if (string.Equals(_champSelectSubPhase, "Accepted", StringComparison.OrdinalIgnoreCase))
                return ReadyCheckResponseState.Accepted;

            if (string.Equals(_champSelectSubPhase, "Declined", StringComparison.OrdinalIgnoreCase))
                return ReadyCheckResponseState.Declined;

            return ReadyCheckResponseState.Pending;
        }

        private void ShowChampionActivityRing(ChampionAnimationPalette palette, bool animate)
        {
            var colors = GetChampionPaletteColors(palette);
            const PhaseActivityRingMode mode = PhaseActivityRingMode.ChampionSelectOrbit;
            PhaseActivityRingProfile profile = GetActivityRingProfile(palette);
            bool activityRingStateChanged = _activeActivityRingMode != mode || _activeActivityRingProfile != profile;

            ApplyActivityRingColors(colors.Primary, colors.Ridge, animate && activityRingStateChanged);
            ActivityRing.Visibility = Visibility.Visible;
            ActivityRing.Start(mode, profile);
            _activeActivityRingMode = mode;
            _activeActivityRingProfile = profile;
        }

        private void ApplyChampionAnimationPalette(ChampionAnimationPalette palette, bool animate)
        {
            var colors = GetChampionPaletteColors(palette);

            ApplyColor(ChampionPolyhedron, PolyhedronGlyph.PrimaryColorProperty, colors.Primary, animate);
            ApplyColor(ChampionPolyhedron, PolyhedronGlyph.SecondaryColorProperty, colors.Secondary, animate);
            ApplyColor(ChampionPolyhedron, PolyhedronGlyph.RidgeColorProperty, colors.Ridge, animate);
        }

        private void ApplyActivityRingColors(Color ringColor, Color pulseColor, bool animate)
        {
            ApplyColor(ActivityRing, PhaseActivityRing.RingColorProperty, ringColor, animate);
            ApplyColor(ActivityRing, PhaseActivityRing.PulseColorProperty, pulseColor, animate);
        }

        private static void ApplyColor(FrameworkElement target, DependencyProperty property, Color color, bool animate)
        {
            if (!animate)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, color);
                return;
            }

            Color currentColor = (Color)target.GetValue(property);
            if (currentColor == color)
                return;

            var animation = new ColorAnimation(currentColor, color, ChampionColorTransitionDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            animation.Completed += (_, _) => target.SetValue(property, color);
            target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        private void PhaseStatusIndicator_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isThemeRefreshSubscribed)
            {
                AppThemeManager.ThemeChanged += AppThemeManager_ThemeChanged;
                _isThemeRefreshSubscribed = true;
            }

            Refresh();
        }

        private void PhaseStatusIndicator_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isThemeRefreshSubscribed)
            {
                AppThemeManager.ThemeChanged -= AppThemeManager_ThemeChanged;
                _isThemeRefreshSubscribed = false;
            }

            StopChampionAnimation();
            StopActivityRing();
        }

        private void AppThemeManager_ThemeChanged()
        {
            if (Dispatcher.CheckAccess())
            {
                _activeChampionAnimationPalette = null;
                _activeActivityRingMode = null;
                _activeActivityRingProfile = null;
                Refresh();
                return;
            }

            Dispatcher.InvokeAsync(() =>
            {
                _activeChampionAnimationPalette = null;
                _activeActivityRingMode = null;
                _activeActivityRingProfile = null;
                Refresh();
            });
        }

        private ChampionPaletteColors GetChampionPaletteColors(ChampionAnimationPalette palette)
        {
            string resourcePrefix = palette switch
            {
                ChampionAnimationPalette.Ban => "ChampionStatusSpinnerBan",
                ChampionAnimationPalette.Pick => "ChampionStatusSpinnerPick",
                ChampionAnimationPalette.Planning => "ChampionStatusSpinnerPlanning",
                ChampionAnimationPalette.Finalization => "ChampionStatusSpinnerFinalization",
                _ => "ChampionStatusSpinnerNeutral"
            };

            ChampionPaletteColors fallback = GetDefaultChampionPaletteColors(palette);

            return new ChampionPaletteColors(
                ResourceColor($"{resourcePrefix}PrimaryBrush", fallback.Primary),
                ResourceColor($"{resourcePrefix}SecondaryBrush", fallback.Secondary),
                ResourceColor($"{resourcePrefix}RidgeBrush", fallback.Ridge));
        }

        private Color ResourceColor(string key, Color fallback)
        {
            return TryFindResource(key) switch
            {
                SolidColorBrush brush => brush.Color,
                Color color => color,
                _ => fallback
            };
        }

        private static ChampionPaletteColors GetDefaultChampionPaletteColors(ChampionAnimationPalette palette)
        {
            return palette switch
            {
                ChampionAnimationPalette.Ban => new(
                    Rgb(239, 68, 68),
                    Rgb(127, 29, 29),
                    Argb(220, 254, 202, 202)),
                ChampionAnimationPalette.Pick => new(
                    Rgb(56, 189, 248),
                    Rgb(30, 64, 175),
                    Argb(220, 219, 234, 254)),
                ChampionAnimationPalette.Planning => new(
                    Rgb(245, 158, 11),
                    Rgb(124, 58, 237),
                    Argb(220, 254, 243, 199)),
                ChampionAnimationPalette.Finalization => new(
                    Rgb(168, 85, 247),
                    Rgb(76, 29, 149),
                    Argb(235, 245, 208, 255)),
                _ => new(
                    Rgb(34, 211, 238),
                    Rgb(124, 58, 237),
                    Argb(220, 236, 254, 255))
            };
        }

        private static PhaseActivityRingProfile GetActivityRingProfile(ChampionAnimationPalette palette)
        {
            return palette switch
            {
                ChampionAnimationPalette.Ban => PhaseActivityRingProfile.Ban,
                ChampionAnimationPalette.Pick => PhaseActivityRingProfile.Pick,
                ChampionAnimationPalette.Planning => PhaseActivityRingProfile.Planning,
                ChampionAnimationPalette.Finalization => PhaseActivityRingProfile.Finalization,
                _ => PhaseActivityRingProfile.Neutral
            };
        }

        private static bool IsCompletedSubPhase(string champSelectSubPhase)
        {
            return string.Equals(champSelectSubPhase, "Finalization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "PlanningDone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "Planning done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "PickDone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "Pick done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "BanDone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "Ban done", StringComparison.OrdinalIgnoreCase);
        }

        private static ChampionAnimationPalette GetCompletionPalette(string champSelectSubPhase)
        {
            if (string.Equals(champSelectSubPhase, "BanDone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "Ban done", StringComparison.OrdinalIgnoreCase))
            {
                return ChampionAnimationPalette.Ban;
            }

            if (string.Equals(champSelectSubPhase, "PlanningDone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(champSelectSubPhase, "Planning done", StringComparison.OrdinalIgnoreCase))
            {
                return ChampionAnimationPalette.Planning;
            }

            return ChampionAnimationPalette.Pick;
        }

        private void RefreshIndicatorGeometry()
        {
            double size = Math.Min(IndicatorContent.ActualWidth, IndicatorContent.ActualHeight);
            if (size <= 0)
                return;

            double ringSize = FloorToEvenPixelSize(size);
            if (ringSize >= 2)
            {
                ActivityRing.Width = ringSize;
                ActivityRing.Height = ringSize;
            }

            double circleSize = NearestEvenPixelSize(Math.Clamp(ringSize * 0.38, 10, 14));
            PhaseCircle.Width = circleSize;
            PhaseCircle.Height = circleSize;
        }

        private static double FloorToEvenPixelSize(double value)
        {
            int pixels = Math.Max(0, (int)Math.Floor(value));
            if (pixels % 2 != 0)
                pixels--;

            return Math.Max(0, pixels);
        }

        private static double NearestEvenPixelSize(double value)
        {
            int rounded = Math.Max(2, (int)Math.Round(value));
            if (rounded % 2 == 0)
                return rounded;

            int lower = Math.Max(2, rounded - 1);
            int upper = rounded + 1;

            return Math.Abs(value - lower) <= Math.Abs(value - upper)
                ? lower
                : upper;
        }

        private static Color Rgb(byte red, byte green, byte blue) =>
            Color.FromRgb(red, green, blue);

        private static Color Argb(byte alpha, byte red, byte green, byte blue) =>
            Color.FromArgb(alpha, red, green, blue);

        private sealed record ChampionPaletteColors(
            Color Primary,
            Color Secondary,
            Color Ridge);
    }
}
