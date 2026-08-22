using System.Windows;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPictureDownloadWarningWindow : Window
    {
        public ChampionPictureDownloadWarningWindow(
            string championName,
            bool downloadRawPictures,
            string platformId,
            string locale)
        {
            InitializeComponent();
            string cacheModeText = downloadRawPictures
                ? "Full-resolution enthusiast mode is enabled, so these downloads will keep Riot's original JPG files. JoinGameAfk normally shows no noticeable visual difference, while a complete collection can add 500 MB+ of disk and RAM usage."
                : "Compact picture mode is enabled, so these jpg files are resized to 96px-wide cache copies at maximum JPEG quality.";
            MessageTextBlock.Text =
                $"The League Client is not connected, so JoinGameAfk will use Riot Data Dragon with {platformId} · {locale} to download every available tile image for {championName}. "
                + "Start the watcher instead if you want the faster local download.\n\n"
                + $"The JPG files will be stored in your local JoinGameAfk app storage.\n\n{cacheModeText}";
        }

        public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
