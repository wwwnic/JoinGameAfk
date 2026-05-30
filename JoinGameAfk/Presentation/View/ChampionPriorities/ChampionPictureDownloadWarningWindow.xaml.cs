using System.Windows;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPictureDownloadWarningWindow : Window
    {
        public ChampionPictureDownloadWarningWindow(string championName, bool downloadRawPictures)
        {
            InitializeComponent();
            string cacheModeText = downloadRawPictures
                ? "Raw picture mode is enabled, so these jpg files stay as Riot's original files."
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
