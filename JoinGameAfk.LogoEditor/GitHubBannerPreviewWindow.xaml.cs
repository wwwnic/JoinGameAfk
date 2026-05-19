using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JoinGameAfk.LogoEditor;

public partial class GitHubBannerPreviewWindow : Window
{
    private readonly LogoSettings _settings;
    private readonly string _defaultPath;

    public GitHubBannerPreviewWindow(LogoSettings settings, string defaultPath)
    {
        InitializeComponent();

        _settings = settings.Clone();
        _defaultPath = defaultPath;

        BitmapSource banner = PolyhedronLogoRenderer.RenderGitHubBannerBitmap(_settings);
        BannerImage.Source = banner;
        SizeText.Text = $"{banner.PixelWidth} x {banner.PixelHeight}px, transparent top half";
        PathText.Text = _defaultPath;
    }

    public string? SavedPath { get; private set; }

    private void SaveDefaultButton_Click(object sender, RoutedEventArgs e) =>
        SaveBanner(_defaultPath);

    private void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        string? defaultDirectory = Path.GetDirectoryName(_defaultPath);
        var dialog = new SaveFileDialog
        {
            Title = "Save GitHub banner",
            FileName = Path.GetFileName(_defaultPath),
            InitialDirectory = string.IsNullOrWhiteSpace(defaultDirectory) ? null : defaultDirectory,
            Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        SaveBanner(dialog.FileName);
    }

    private void SaveBanner(string path)
    {
        try
        {
            PolyhedronLogoRenderer.WriteGitHubBanner(path, _settings);
            SavedPath = path;
            PathText.Text = $"Saved to {path}";
        }
        catch (Exception ex)
        {
            PathText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();
}
