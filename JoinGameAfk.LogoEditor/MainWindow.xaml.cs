using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace JoinGameAfk.LogoEditor;

public partial class MainWindow : Window
{
    private const string PresetFileFilter = "JoinGameAfk logo preset (*.jgalogo)|*.jgalogo|JSON files (*.json)|*.json|All files (*.*)|*.*";
    private const string GeneratedPresetFileName = "logo.jgalogo";
    private const double MinimumLineSize = 1;
    private const double MaximumLineSize = 14;
    private const double MinimumContrastLineSize = 0;
    private const double MaximumContrastLineSize = 22;
    private const double MinimumFadeStrength = 0;
    private const double MaximumFadeStrength = 2;
    private const double MinimumPolyhedronScale = 0.34;
    private const double MaximumPolyhedronScale = 0.50;
    private const double MinimumStrokeInsetScale = 0;
    private const double MaximumStrokeInsetScale = 3;
    private const double MinimumFacetDetailLevel = 0;
    private const double MaximumFacetDetailLevel = 1;
    private const double MinimumRotationDegrees = -180;
    private const double MaximumRotationDegrees = 180;
    private const double MinimumCheckScale = 0.16;
    private const double MaximumCheckScale = 0.95;
    private const double MinimumCheckOffset = -0.35;
    private const double MaximumCheckOffset = 0.35;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Brush ValidInputBorderBrush = CreateSolidBrush(0x33, 0x41, 0x55);
    private static readonly Brush InvalidInputBorderBrush = CreateSolidBrush(0xF8, 0x71, 0x71);

    private readonly IReadOnlyList<LogoPreset> _presets = CreateLogoPresets();
    private readonly IReadOnlyList<PreviewBackgroundOption> _previewBackgroundOptions = CreatePreviewBackgroundOptions();
    private bool _isLoading;
    private string? _lastOutputFolder;

    public MainWindow()
    {
        InitializeComponent();

        PresetComboBox.ItemsSource = _presets;
        PreviewBackgroundComboBox.ItemsSource = _previewBackgroundOptions;
        PreviewBackgroundComboBox.DisplayMemberPath = nameof(PreviewBackgroundOption.Name);
        PreviewBackgroundComboBox.SelectedIndex = 0;

        LoadDefaults();
    }

    private void LoadDefaults()
    {
        ApplySettings(
            LogoSettings.CreateDefault(),
            "Defaults loaded. Generate app assets when ready.",
            clearPresetSelection: true);
    }

    private void ApplySettings(LogoSettings settings, string status, bool clearPresetSelection = false)
    {
        _isLoading = true;

        PrimaryColorTextBox.Text = LogoSettings.ToHex(settings.PrimaryColor);
        SecondaryColorTextBox.Text = LogoSettings.ToHex(settings.SecondaryColor);
        RidgeColorTextBox.Text = LogoSettings.ToHex(settings.RidgeColor);
        EdgeShadowColorTextBox.Text = LogoSettings.ToHex(settings.EdgeShadowColor);

        LineSizeSlider.Value = settings.FacetStrokeWidth;
        ContrastLineSizeSlider.Value = settings.EdgeShadowStrokeWidth;
        FadeStrengthSlider.Value = settings.FadeStrength;
        ShapeScaleSlider.Value = settings.PolyhedronScale;
        StrokeInsetSlider.Value = settings.StrokeInsetScale;
        FacetDetailSlider.Value = settings.FacetDetailLevel;
        HideFacetLinesAtAllSizesCheckBox.IsChecked = settings.HideFacetLinesAtAllSizes;
        RotationXSlider.Value = settings.RotationXDegrees;
        RotationYSlider.Value = settings.RotationYDegrees;
        RotationZSlider.Value = settings.RotationZDegrees;
        CheckOverlayCheckBox.IsChecked = settings.ShowCheckOverlay;
        CheckSizeSlider.Value = settings.CheckScale;
        CheckOffsetXSlider.Value = settings.CheckOffsetX;
        CheckOffsetYSlider.Value = settings.CheckOffsetY;

        if (clearPresetSelection)
            PresetComboBox.SelectedIndex = -1;

        _isLoading = false;

        RenderPreview(status);
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !IsLoaded)
            return;

        RenderPreview();
    }

    private void ColorTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || !LogoSettings.TryParseHexColor(textBox.Text, out Color color))
            return;

        string normalizedColor = LogoSettings.ToHex(color);
        if (!string.Equals(textBox.Text, normalizedColor, StringComparison.Ordinal))
            textBox.Text = normalizedColor;
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !IsLoaded)
            return;

        ApplySelectedPreset();
    }

    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplySelectedPreset();

    private void ApplySelectedPreset()
    {
        if (PresetComboBox.SelectedItem is not LogoPreset preset)
        {
            StatusText.Text = "Choose a preset to apply.";
            return;
        }

        ApplySettings(preset.ToSettings(), $"Applied {preset.Name} preset.");
    }

    private void PreviewBackgroundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewBackgroundComboBox.SelectedItem is not PreviewBackgroundOption option)
            return;

        PreviewSurface.Background = option.Brush;
        ThumbnailStrip.Background = option.Brush;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDefaults();
        StatusText.Text = "Defaults restored. Generate app assets to write files.";
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load logo preset",
            Filter = PresetFileFilter,
            DefaultExt = ".jgalogo"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            string json = File.ReadAllText(dialog.FileName);
            LogoPresetFile presetFile = JsonSerializer.Deserialize<LogoPresetFile>(json)
                ?? throw new InvalidDataException("The selected preset file is empty.");

            ApplySettings(presetFile.ToSettings(), $"Loaded preset from {dialog.FileName}.", clearPresetSelection: true);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load failed: {ex.Message}";
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save logo preset",
            FileName = "joingameafk-logo.jgalogo",
            Filter = PresetFileFilter,
            DefaultExt = ".jgalogo",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            WritePresetFile(dialog.FileName, settings);
            StatusText.Text = $"Saved preset to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        int[] iconSizes = GetSelectedIconSizes();
        if (iconSizes.Length == 0)
        {
            StatusText.Text = "Choose at least one ICO frame size before generating app assets.";
            return;
        }

        try
        {
            LogoExportResult result = PolyhedronLogoRenderer.WriteAssets(settings, iconSizes);
            string presetPath = Path.Combine(Path.GetDirectoryName(result.SvgPath)!, GeneratedPresetFileName);
            WritePresetFile(presetPath, settings);
            result = result with { PresetPath = presetPath };

            _lastOutputFolder = Path.GetDirectoryName(result.SvgPath);
            OpenOutputFolderButton.IsEnabled = true;
            StatusText.Text = $"Generated app assets:\n{result.SvgPath}\n{result.IcoPath}\n{result.PresetPath}\n{result.PreviewPath}\nICO frames: {FormatIconSizes(iconSizes)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Generate failed: {ex.Message}";
        }
    }

    private static void WritePresetFile(string path, LogoSettings settings)
    {
        string json = JsonSerializer.Serialize(LogoPresetFile.FromSettings(settings), JsonOptions);
        File.WriteAllText(path, json);
    }

    private void CopySvgButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        try
        {
            Clipboard.SetText(PolyhedronLogoRenderer.CreateSvg(settings));
            StatusText.Text = "Copied SVG markup to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Copy failed: {ex.Message}";
        }
    }

    private void ExportPngButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export logo PNG",
            FileName = "joingameafk-logo-512.png",
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            PolyhedronLogoRenderer.WritePng(dialog.FileName, PolyhedronLogoRenderer.CanvasSize, settings);
            StatusText.Text = $"Exported PNG to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"PNG export failed: {ex.Message}";
        }
    }

    private void ExportIcoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        int[] iconSizes = GetSelectedIconSizes();
        if (iconSizes.Length == 0)
        {
            StatusText.Text = "Choose at least one ICO frame size before exporting.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Windows ICO",
            FileName = "joingameafk-logo.ico",
            Filter = "Windows icon (*.ico)|*.ico|All files (*.*)|*.*",
            DefaultExt = ".ico",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            PolyhedronLogoRenderer.WriteIcon(dialog.FileName, settings, iconSizes);
            StatusText.Text = $"Exported ICO to {dialog.FileName}.\nPrimary frame: {iconSizes[0]}px. Included frames: {FormatIconSizes(iconSizes)}. Use Export PNG for a 512px image.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"ICO export failed: {ex.Message}";
        }
    }

    private void PreviewGitHubBannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        try
        {
            var previewWindow = new GitHubBannerPreviewWindow(settings, PolyhedronLogoRenderer.GetDefaultGitHubBannerPath())
            {
                Owner = this
            };

            previewWindow.ShowDialog();

            if (string.IsNullOrWhiteSpace(previewWindow.SavedPath))
            {
                StatusText.Text = "GitHub banner preview closed.";
                return;
            }

            _lastOutputFolder = Path.GetDirectoryName(previewWindow.SavedPath);
            OpenOutputFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastOutputFolder);
            StatusText.Text = $"Saved GitHub banner:\n{previewWindow.SavedPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"GitHub banner preview failed: {ex.Message}";
        }
    }

    private int[] GetSelectedIconSizes()
    {
        var selectedSizes = new List<int>();
        AddSelectedIconSize(selectedSizes, IconSize256CheckBox, 256);
        AddSelectedIconSize(selectedSizes, IconSize128CheckBox, 128);
        AddSelectedIconSize(selectedSizes, IconSize64CheckBox, 64);
        AddSelectedIconSize(selectedSizes, IconSize48CheckBox, 48);
        AddSelectedIconSize(selectedSizes, IconSize32CheckBox, 32);
        AddSelectedIconSize(selectedSizes, IconSize24CheckBox, 24);
        AddSelectedIconSize(selectedSizes, IconSize16CheckBox, 16);
        return selectedSizes.ToArray();
    }

    private static void AddSelectedIconSize(List<int> selectedSizes, CheckBox checkBox, int size)
    {
        if (checkBox.IsChecked == true)
            selectedSizes.Add(size);
    }

    private static string FormatIconSizes(IEnumerable<int> iconSizes) =>
        string.Join(", ", iconSizes);

    private void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastOutputFolder))
        {
            StatusText.Text = "Generate or export before opening the output folder.";
            return;
        }

        if (!Directory.Exists(_lastOutputFolder))
        {
            StatusText.Text = "The output folder no longer exists.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastOutputFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open folder: {ex.Message}";
        }
    }

    private void RenderPreview(string? status = null)
    {
        if (!TryReadSettings(out LogoSettings? settings))
            return;

        UpdateLabels(settings);
        UpdateSwatches(settings);

        LargePreviewImage.Source = PolyhedronLogoRenderer.RenderBitmap(PolyhedronLogoRenderer.CanvasSize, settings);
        Preview16Image.Source = PolyhedronLogoRenderer.RenderBitmap(16, settings);
        Preview24Image.Source = PolyhedronLogoRenderer.RenderBitmap(24, settings);
        Preview32Image.Source = PolyhedronLogoRenderer.RenderBitmap(32, settings);
        Preview48Image.Source = PolyhedronLogoRenderer.RenderBitmap(48, settings);
        Preview64Image.Source = PolyhedronLogoRenderer.RenderBitmap(64, settings);
        Preview128Image.Source = PolyhedronLogoRenderer.RenderBitmap(128, settings);

        StatusText.Text = status ?? "Live preview only. Generate app assets writes logo.svg and logo.ico.";
    }

    private bool TryReadSettings([NotNullWhen(true)] out LogoSettings? settings)
    {
        settings = null;
        string? invalidColorLabel = null;

        bool hasPrimaryColor = TryReadColor(PrimaryColorTextBox, "Face color", ref invalidColorLabel, out Color primaryColor);
        bool hasSecondaryColor = TryReadColor(SecondaryColorTextBox, "Fade color", ref invalidColorLabel, out Color secondaryColor);
        bool hasRidgeColor = TryReadColor(RidgeColorTextBox, "Line color", ref invalidColorLabel, out Color ridgeColor);
        bool hasEdgeShadowColor = TryReadColor(EdgeShadowColorTextBox, "Contrast line", ref invalidColorLabel, out Color edgeShadowColor);

        if (!hasPrimaryColor || !hasSecondaryColor || !hasRidgeColor || !hasEdgeShadowColor)
        {
            StatusText.Text = $"{invalidColorLabel} must be a hex color like #F8FAFC or #FFF.";
            return false;
        }

        bool hideFacetLinesAtAllSizes = HideFacetLinesAtAllSizesCheckBox.IsChecked == true;

        settings = new LogoSettings
        {
            PrimaryColor = primaryColor,
            SecondaryColor = secondaryColor,
            RidgeColor = ridgeColor,
            EdgeShadowColor = edgeShadowColor,
            FacetStrokeWidth = LineSizeSlider.Value,
            EdgeShadowStrokeWidth = ContrastLineSizeSlider.Value,
            FadeStrength = FadeStrengthSlider.Value,
            PolyhedronScale = ShapeScaleSlider.Value,
            StrokeInsetScale = StrokeInsetSlider.Value,
            FacetDetailLevel = FacetDetailSlider.Value,
            HideFacetLinesAtAllSizes = hideFacetLinesAtAllSizes,
            RotationXDegrees = RotationXSlider.Value,
            RotationYDegrees = RotationYSlider.Value,
            RotationZDegrees = RotationZSlider.Value,
            ShowCheckOverlay = CheckOverlayCheckBox.IsChecked == true,
            CheckScale = CheckSizeSlider.Value,
            CheckOffsetX = CheckOffsetXSlider.Value,
            CheckOffsetY = CheckOffsetYSlider.Value
        };
        return true;
    }

    private static bool TryReadColor(TextBox textBox, string label, ref string? invalidColorLabel, out Color color)
    {
        if (LogoSettings.TryParseHexColor(textBox.Text, out color))
        {
            textBox.BorderBrush = ValidInputBorderBrush;
            return true;
        }

        textBox.BorderBrush = InvalidInputBorderBrush;
        invalidColorLabel ??= label;
        return false;
    }

    private void UpdateLabels(LogoSettings settings)
    {
        bool showFacetLinesAtLargeSizes = !settings.HideFacetLinesAtAllSizes;

        LineSizeLabel.Text = showFacetLinesAtLargeSizes
            ? $"Line thickness: {settings.FacetStrokeWidth:0.0}"
            : "Line thickness: hidden";
        ContrastLineSizeLabel.Text = showFacetLinesAtLargeSizes
            ? $"Contrast thickness: {settings.EdgeShadowStrokeWidth:0.0}"
            : "Contrast thickness: hidden";
        FadeStrengthLabel.Text = $"Fade strength: {settings.FadeStrength:0.00}x";
        ShapeScaleLabel.Text = $"Logo size: {settings.PolyhedronScale:0.000}";
        StrokeInsetLabel.Text = showFacetLinesAtLargeSizes
            ? $"Line inset: {settings.StrokeInsetScale:0.00}x"
            : "Line inset: hidden";
        FacetDetailLabel.Text = settings.FacetDetailLevel < 0.5
            ? "Facet detail: Simple"
            : "Facet detail: Detailed";
        RotationXLabel.Text = $"Rotate X: {settings.RotationXDegrees:0.#} degrees";
        RotationYLabel.Text = $"Rotate Y: {settings.RotationYDegrees:0.#} degrees";
        RotationZLabel.Text = $"Rotate Z: {settings.RotationZDegrees:0.#} degrees";
        CheckSizeLabel.Text = $"Check size: {settings.CheckScale:0.00}x";
        CheckOffsetXLabel.Text = $"Check horizontal: {settings.CheckOffsetX:+0.000;-0.000;0.000}";
        CheckOffsetYLabel.Text = $"Check vertical: {settings.CheckOffsetY:+0.000;-0.000;0.000}";
        UpdateFacetLineControls(showFacetLinesAtLargeSizes);
        UpdateCheckOverlayControls(settings.ShowCheckOverlay);
    }

    private void UpdateFacetLineControls(bool isEnabled)
    {
        LineSizeSlider.IsEnabled = isEnabled;
        ContrastLineSizeSlider.IsEnabled = isEnabled;
        StrokeInsetSlider.IsEnabled = isEnabled;
    }

    private void UpdateCheckOverlayControls(bool isEnabled)
    {
        CheckSizeSlider.IsEnabled = isEnabled;
        CheckOffsetXSlider.IsEnabled = isEnabled;
        CheckOffsetYSlider.IsEnabled = isEnabled;
    }

    private void UpdateSwatches(LogoSettings settings)
    {
        PrimaryColorSwatch.Background = new SolidColorBrush(settings.PrimaryColor);
        SecondaryColorSwatch.Background = new SolidColorBrush(settings.SecondaryColor);
        RidgeColorSwatch.Background = new SolidColorBrush(settings.RidgeColor);
        EdgeShadowColorSwatch.Background = new SolidColorBrush(settings.EdgeShadowColor);
    }

    private static IReadOnlyList<LogoPreset> CreateLogoPresets() =>
    [
        new("Rift Blue", "#93C5FD", "#2563EB", "#F8FAFC", "#05070D", 6, 10, 1, 0.445, 1, 1, LogoSettings.DefaultRotationXDegrees, LogoSettings.DefaultRotationYDegrees, LogoSettings.DefaultRotationZDegrees, false, 0.44, 0, 0),
        new("Icon Simple", "#93C5FD", "#2563EB", "#F8FAFC", "#05070D", 8.5, 12, 0.85, 0.455, 1.4, 0, LogoSettings.DefaultRotationXDegrees, LogoSettings.DefaultRotationYDegrees, LogoSettings.DefaultRotationZDegrees, false, 0.44, 0, 0),
        new("Neon Circuit", "#5EEAD4", "#2563EB", "#ECFEFF", "#06131B", 5.4, 8.5, 1, 0.452, 1, 1, 38, -35, 18, false, 0.44, 0, 0),
        new("Golden Rift", "#FACC15", "#B45309", "#FFFBEB", "#111827", 6.2, 10.5, 1, 0.438, 1, 1, 32, -42, 24, false, 0.44, 0, 0),
        new("Color Safe", "#38BDF8", "#F97316", "#F8FAFC", "#111827", 5.6, 9.2, 1, 0.448, 1, 1, 46, -28, 4, false, 0.44, 0, 0)
    ];

    private static IReadOnlyList<PreviewBackgroundOption> CreatePreviewBackgroundOptions() =>
    [
        new("Dark canvas", CreateSolidBrush(0x11, 0x18, 0x27)),
        new("Light canvas", CreateSolidBrush(0xF8, 0xFA, 0xFC)),
        new("Transparent grid", CreateCheckerBrush())
    ];

    private static Brush CreateSolidBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateCheckerBrush()
    {
        var lightBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
        var darkBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));

        var darkSquares = new GeometryGroup();
        darkSquares.Children.Add(new RectangleGeometry(new Rect(0, 0, 12, 12)));
        darkSquares.Children.Add(new RectangleGeometry(new Rect(12, 12, 12, 12)));

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(lightBrush, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        drawing.Children.Add(new GeometryDrawing(darkBrush, null, darkSquares));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 24, 24),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private sealed record LogoPreset(
        string Name,
        string PrimaryColor,
        string SecondaryColor,
        string RidgeColor,
        string EdgeShadowColor,
        double FacetStrokeWidth,
        double EdgeShadowStrokeWidth,
        double FadeStrength,
        double PolyhedronScale,
        double StrokeInsetScale,
        double FacetDetailLevel,
        double RotationXDegrees,
        double RotationYDegrees,
        double RotationZDegrees,
        bool ShowCheckOverlay,
        double CheckScale,
        double CheckOffsetX,
        double CheckOffsetY,
        bool HideFacetLinesAtAllSizes = false)
    {
        public LogoSettings ToSettings()
        {
            LogoSettings.TryParseHexColor(PrimaryColor, out Color primaryColor);
            LogoSettings.TryParseHexColor(SecondaryColor, out Color secondaryColor);
            LogoSettings.TryParseHexColor(RidgeColor, out Color ridgeColor);
            LogoSettings.TryParseHexColor(EdgeShadowColor, out Color edgeShadowColor);

            return new LogoSettings
            {
                PrimaryColor = primaryColor,
                SecondaryColor = secondaryColor,
                RidgeColor = ridgeColor,
                EdgeShadowColor = edgeShadowColor,
                FacetStrokeWidth = FacetStrokeWidth,
                EdgeShadowStrokeWidth = EdgeShadowStrokeWidth,
                FadeStrength = FadeStrength,
                PolyhedronScale = PolyhedronScale,
                StrokeInsetScale = StrokeInsetScale,
                FacetDetailLevel = FacetDetailLevel,
                HideFacetLinesAtAllSizes = HideFacetLinesAtAllSizes,
                RotationXDegrees = RotationXDegrees,
                RotationYDegrees = RotationYDegrees,
                RotationZDegrees = RotationZDegrees,
                ShowCheckOverlay = ShowCheckOverlay,
                CheckScale = CheckScale,
                CheckOffsetX = CheckOffsetX,
                CheckOffsetY = CheckOffsetY
            };
        }
    }

    private sealed record PreviewBackgroundOption(string Name, Brush Brush);

    private sealed record LogoPresetFile(
        int Version,
        string PrimaryColor,
        string SecondaryColor,
        string RidgeColor,
        string EdgeShadowColor,
        double FacetStrokeWidth,
        double EdgeShadowStrokeWidth,
        double? FadeStrength,
        double PolyhedronScale,
        double? StrokeInsetScale,
        double? FacetDetailLevel,
        double? RotationXDegrees,
        double? RotationYDegrees,
        double? RotationZDegrees,
        bool? ShowCheckOverlay,
        double? CheckScale,
        double? CheckOffsetX,
        double? CheckOffsetY,
        bool? HideFacetLinesAtAllSizes = null,
        bool? ShowFacetLines = null)
    {
        public static LogoPresetFile FromSettings(LogoSettings settings) =>
            new(
                3,
                LogoSettings.ToHex(settings.PrimaryColor),
                LogoSettings.ToHex(settings.SecondaryColor),
                LogoSettings.ToHex(settings.RidgeColor),
                LogoSettings.ToHex(settings.EdgeShadowColor),
                settings.FacetStrokeWidth,
                settings.EdgeShadowStrokeWidth,
                settings.FadeStrength,
                settings.PolyhedronScale,
                settings.StrokeInsetScale,
                settings.FacetDetailLevel,
                settings.RotationXDegrees,
                settings.RotationYDegrees,
                settings.RotationZDegrees,
                settings.ShowCheckOverlay,
                settings.CheckScale,
                settings.CheckOffsetX,
                settings.CheckOffsetY,
                settings.HideFacetLinesAtAllSizes);

        public LogoSettings ToSettings()
        {
            if (!LogoSettings.TryParseHexColor(PrimaryColor, out Color primaryColor))
                throw new InvalidDataException("Preset face color is invalid.");

            if (!LogoSettings.TryParseHexColor(SecondaryColor, out Color secondaryColor))
                throw new InvalidDataException("Preset fade color is invalid.");

            if (!LogoSettings.TryParseHexColor(RidgeColor, out Color ridgeColor))
                throw new InvalidDataException("Preset line color is invalid.");

            if (!LogoSettings.TryParseHexColor(EdgeShadowColor, out Color edgeShadowColor))
                throw new InvalidDataException("Preset contrast line color is invalid.");

            return new LogoSettings
            {
                PrimaryColor = primaryColor,
                SecondaryColor = secondaryColor,
                RidgeColor = ridgeColor,
                EdgeShadowColor = edgeShadowColor,
                FacetStrokeWidth = ClampFinite(FacetStrokeWidth, MinimumLineSize, MaximumLineSize),
                EdgeShadowStrokeWidth = ClampFinite(EdgeShadowStrokeWidth, MinimumContrastLineSize, MaximumContrastLineSize),
                FadeStrength = ClampFinite(FadeStrength ?? 1, MinimumFadeStrength, MaximumFadeStrength),
                PolyhedronScale = ClampFinite(PolyhedronScale, MinimumPolyhedronScale, MaximumPolyhedronScale),
                StrokeInsetScale = ClampFinite(StrokeInsetScale ?? 1, MinimumStrokeInsetScale, MaximumStrokeInsetScale),
                FacetDetailLevel = ClampFinite(FacetDetailLevel ?? 1, MinimumFacetDetailLevel, MaximumFacetDetailLevel),
                HideFacetLinesAtAllSizes = HideFacetLinesAtAllSizes ?? !(ShowFacetLines ?? true),
                RotationXDegrees = ClampFinite(RotationXDegrees ?? LogoSettings.DefaultRotationXDegrees, MinimumRotationDegrees, MaximumRotationDegrees),
                RotationYDegrees = ClampFinite(RotationYDegrees ?? LogoSettings.DefaultRotationYDegrees, MinimumRotationDegrees, MaximumRotationDegrees),
                RotationZDegrees = ClampFinite(RotationZDegrees ?? LogoSettings.DefaultRotationZDegrees, MinimumRotationDegrees, MaximumRotationDegrees),
                ShowCheckOverlay = ShowCheckOverlay ?? false,
                CheckScale = ClampFinite(CheckScale ?? 0.44, MinimumCheckScale, MaximumCheckScale),
                CheckOffsetX = ClampFinite(CheckOffsetX ?? 0, MinimumCheckOffset, MaximumCheckOffset),
                CheckOffsetY = ClampFinite(CheckOffsetY ?? 0, MinimumCheckOffset, MaximumCheckOffset)
            };
        }
    }

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : minimum;
}
