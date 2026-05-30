using System.Diagnostics;
using System.Windows;
using JoinGameAfk.Constant;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage
    {
        private void OpenStorageFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppStorage.EnsureDirectoryExists();
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppStorage.DirectoryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open storage folder: {ex.Message}", "Open Folder Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}