using System.Windows;
using System.Windows.Controls;
using JoinGameAfk.Model;

namespace JoinGameAfk.View
{
    public partial class SettingsPage : Page
    {
        private readonly ChampSelectSettings _settings;

        public SettingsPage(ChampSelectSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            ReadyCheckAcceptDelayBox.Text = _settings.ReadyCheckAcceptDelaySeconds.ToString();
            AutoLockSelectionCheckBox.IsChecked = _settings.AutoLockSelectionEnabled;
            PickLockDelayBox.Text = _settings.PickLockDelaySeconds.ToString();
            ChampionHoverDelayBox.Text = _settings.ChampionHoverDelaySeconds.ToString();
            BanLockDelayBox.Text = _settings.BanLockDelaySeconds.ToString();
            ChampSelectPollIntervalBox.Text = _settings.ChampSelectPollIntervalMs.ToString();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.ReadyCheckAcceptDelaySeconds = int.TryParse(ReadyCheckAcceptDelayBox.Text, out int readyCheckDelay)
                ? Math.Max(0, readyCheckDelay)
                : 0;
            _settings.AutoLockSelectionEnabled = AutoLockSelectionCheckBox.IsChecked == true;
            _settings.PickLockDelaySeconds = int.TryParse(PickLockDelayBox.Text, out int pickDelay) ? Math.Max(0, pickDelay) : 0;
            _settings.ChampionHoverDelaySeconds = int.TryParse(ChampionHoverDelayBox.Text, out int hoverDelay) ? Math.Max(0, hoverDelay) : 0;
            _settings.BanLockDelaySeconds = int.TryParse(BanLockDelayBox.Text, out int banDelay) ? Math.Max(0, banDelay) : 0;
            _settings.ChampSelectPollIntervalMs = int.TryParse(ChampSelectPollIntervalBox.Text, out int pollInterval)
                ? Math.Clamp(pollInterval, 100, 5000)
                : 1000;

            _settings.Save();
            SavedLabel.Visibility = Visibility.Visible;
        }
    }
}
