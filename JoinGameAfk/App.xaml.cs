using System.Windows;
using JoinGameAfk.Model;
using JoinGameAfk.MVP.Controller;
using JoinGameAfk.View;

namespace JoinGameAfk
{
    public partial class App : Application
    {
        private MainWindow fMainWindow;
        private PhaseProgressionPage fDashboardPage;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                var champSelectSettings = ChampSelectSettings.Load();

                fDashboardPage = new PhaseProgressionPage();

                var phaseController = new PhaseController(fDashboardPage, champSelectSettings);
                fDashboardPage.SetController(phaseController);

                var champSelectPage = new ChampSelectSettingsPage(champSelectSettings);
                var settingsPage = new SettingsPage(champSelectSettings);

                fMainWindow = new MainWindow(fDashboardPage, champSelectPage, settingsPage);
                fDashboardPage.PhaseChanged += fMainWindow.UpdatePhaseIndicator;
                fMainWindow.UpdatePhaseIndicator(JoinGameAfk.Enums.ClientPhase.Unknown);
                fMainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "An error occurred", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
    }
}