using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinGameAfk.Enums;

namespace JoinGameAfk.View.Controls
{
    public partial class PhaseStatusIndicator : UserControl
    {
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
            Hover,
            Finalization
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
                var palette = GetChampionAnimationPalette();

                if (IsLoaded)
                    ShowChampionAnimation(palette);
                else
                    ShowChampionGlyphWithoutAnimation(palette);

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
                "Finalization" => ChampionAnimationPalette.Finalization,
                _ => ChampionAnimationPalette.Neutral
            };
        }

        private void ShowChampionGlyphWithoutAnimation(ChampionAnimationPalette palette)
        {
            PhaseCircle.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Visible;
            ApplyChampionAnimationPalette(palette);
        }

        private void ShowChampionAnimation(ChampionAnimationPalette palette)
        {
            PhaseCircle.Visibility = Visibility.Collapsed;
            ChampionGlyph.Visibility = Visibility.Visible;

            if (_isChampionAnimationRunning && _activeChampionAnimationPalette == palette)
                return;

            StopChampionAnimation();
            ApplyChampionAnimationPalette(palette);
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

        private void ApplyChampionAnimationPalette(ChampionAnimationPalette palette)
        {
            var colors = GetChampionPaletteColors(palette);

            ChampionPolyhedron.PrimaryColor = colors.Primary;
            ChampionPolyhedron.SecondaryColor = colors.Secondary;
            ChampionPolyhedron.RidgeColor = colors.Ridge;
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        private static ChampionPaletteColors GetChampionPaletteColors(ChampionAnimationPalette palette)
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
                ChampionAnimationPalette.Hover => new(
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
