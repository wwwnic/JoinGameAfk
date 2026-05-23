using System.Windows;

namespace JoinGameAfk.View
{
    public partial class ChampionPictureDownloadWarningWindow : Window
    {
        public ChampionPictureDownloadWarningWindow(string championName)
        {
            InitializeComponent();
            MessageTextBlock.Text =
                $"JoinGameAfk will use your internet connection to contact Riot Data Dragon and download every available tile image for {championName}. The JPG files will be stored in your local JoinGameAfk app storage.";
        }

        public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
