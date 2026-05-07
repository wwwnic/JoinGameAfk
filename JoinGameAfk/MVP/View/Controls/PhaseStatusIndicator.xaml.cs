using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoinGameAfk.Enums;

namespace JoinGameAfk.View.Controls
{
    public partial class PhaseStatusIndicator : UserControl
    {
        private Storyboard? _championSelectStoryboard;
        private bool _isChampionAnimationRunning;
        private ChampionAnimationPalette? _activeChampionAnimationPalette;
        private ClientPhase _phase = ClientPhase.Unknown;
        private bool _isWatcherRunning;
        private bool _isClientConnected;
        private string _champSelectSubPhase = string.Empty;

        private enum ChampionAnimationPalette
        {
            Neutral,
            Ban,
            Pick,
            Hover
        }

        public PhaseStatusIndicator()
        {
            InitializeComponent();

            Loaded += (_, _) => Refresh();
            Unloaded += (_, _) => StopChampionAnimation();
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
            if (ShouldShowChampionAnimation())
            {
                if (IsLoaded)
                    ShowChampionAnimation(GetChampionAnimationPalette());
                else
                    ShowChampionGlyphWithoutAnimation();

                return;
            }

            StopChampionAnimation();
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
                "Hover" => ChampionAnimationPalette.Hover,
                _ => ChampionAnimationPalette.Neutral
            };
        }

        private void ShowChampionGlyphWithoutAnimation()
        {
            PhaseCircle.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Visible;
        }

        private void ShowChampionAnimation(ChampionAnimationPalette palette)
        {
            PhaseCircle.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Visible;

            if (_isChampionAnimationRunning && _activeChampionAnimationPalette == palette)
                return;

            StopChampionAnimation();
            ApplyChampionAnimationPalette(palette);
            ChampionSelectStoryboard?.Begin(this, true);
            _isChampionAnimationRunning = ChampionSelectStoryboard is not null;
            _activeChampionAnimationPalette = _isChampionAnimationRunning ? palette : null;
        }

        private void StopChampionAnimation()
        {
            if (!_isChampionAnimationRunning)
                return;

            ChampionSelectStoryboard?.Stop(this);
            _isChampionAnimationRunning = false;
            _activeChampionAnimationPalette = null;
        }

        private Storyboard? ChampionSelectStoryboard =>
            _championSelectStoryboard ??= (TryFindResource("ChampionSelectStoryboard") as Storyboard)?.Clone();

        private void ApplyChampionAnimationPalette(ChampionAnimationPalette palette)
        {
            if (ChampionSelectStoryboard is not Storyboard storyboard)
                return;

            var colors = GetChampionPaletteColors(palette);

            ChampionPulse.Stroke = new SolidColorBrush(colors.PulseColor);

            foreach (Timeline timeline in storyboard.Children)
            {
                if (timeline is not ColorAnimationUsingKeyFrames colorAnimation)
                    continue;

                string targetName = Storyboard.GetTargetName(colorAnimation);
                if (string.Equals(targetName, "ChampionCoreBrush", StringComparison.Ordinal))
                    SetChampionKeyFrameColors(colorAnimation, colors.CoreColors);
                else if (string.Equals(targetName, "ChampionShadowBrush", StringComparison.Ordinal))
                    SetChampionKeyFrameColors(colorAnimation, colors.ShadowColors);
            }
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        private static (Color[] CoreColors, Color[] ShadowColors, Color PulseColor) GetChampionPaletteColors(ChampionAnimationPalette palette)
        {
            return palette switch
            {
                ChampionAnimationPalette.Ban => (
                    new[] { Rgb(239, 68, 68), Rgb(249, 115, 22), Rgb(220, 38, 38), Rgb(239, 68, 68) },
                    new[] { Rgb(127, 29, 29), Rgb(251, 113, 133), Rgb(153, 27, 27), Rgb(127, 29, 29) },
                    Argb(128, 248, 113, 113)),
                ChampionAnimationPalette.Pick => (
                    new[] { Rgb(56, 189, 248), Rgb(37, 99, 235), Rgb(34, 211, 238), Rgb(56, 189, 248) },
                    new[] { Rgb(30, 64, 175), Rgb(125, 211, 252), Rgb(30, 58, 138), Rgb(30, 64, 175) },
                    Argb(128, 125, 211, 252)),
                ChampionAnimationPalette.Hover => (
                    new[] { Rgb(245, 158, 11), Rgb(251, 191, 36), Rgb(167, 139, 250), Rgb(245, 158, 11) },
                    new[] { Rgb(180, 83, 9), Rgb(124, 58, 237), Rgb(249, 115, 22), Rgb(180, 83, 9) },
                    Argb(128, 251, 191, 36)),
                _ => (
                    new[] { Rgb(34, 211, 238), Rgb(167, 139, 250), Rgb(245, 158, 11), Rgb(34, 211, 238) },
                    new[] { Rgb(124, 58, 237), Rgb(239, 68, 68), Rgb(56, 189, 248), Rgb(124, 58, 237) },
                    Argb(128, 255, 255, 255))
            };
        }

        private static void SetChampionKeyFrameColors(ColorAnimationUsingKeyFrames animation, IReadOnlyList<Color> colors)
        {
            int count = Math.Min(animation.KeyFrames.Count, colors.Count);
            for (int i = 0; i < count; i++)
                animation.KeyFrames[i].Value = colors[i];
        }

        private static Color Rgb(byte red, byte green, byte blue) =>
            Color.FromRgb(red, green, blue);

        private static Color Argb(byte alpha, byte red, byte green, byte blue) =>
            Color.FromArgb(alpha, red, green, blue);
    }
}
