using System.Windows;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPictureDownloadWarningWindow : Window
    {
        public ChampionPictureDownloadWarningWindow(string championName, bool downloadRawPictures)
        {
            InitializeComponent();
            string cacheModeText = downloadRawPictures
                ? "Full-resolution enthusiast mode is enabled, so these downloads will keep Riot's original JPG files. JoinGameAfk normally shows no noticeable visual difference, while a complete collection can add 500 MB+ of disk and RAM usage."
                : "Compact picture mode is enabled, so these jpg files are resized to 96px-wide cache copies at maximum JPEG quality.";
            MessageTextBlock.Text =
                $"JoinGameAfk will use your internet connection to contact Riot Data Dragon and download every available tile image for {championName}. The JPG files will be stored in your local JoinGameAfk app storage.\n\n{cacheModeText}";
        }

        public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
