using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Theme;

namespace JoinGameAfk.View
{
    public partial class SettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly ChampSelectSettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly Action<ChampSelectSettings>? _reloadUiForTheme;

        public SettingsPage(ChampSelectSettings settings, Action<ChampSelectSettings>? reloadUiForTheme = null)
        {
            InitializeComponent();
            _settings = settings;
            _reloadUiForTheme = reloadUiForTheme;
            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                SavedLabel.Visibility = Visibility.Collapsed;
            };

            StoragePathTextBlock.Text = AppStorage.DirectoryPath;
            LoadThemeOptions();
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
            _settings.ThemeKey = GetSelectedThemeKey();
            bool shouldReloadTheme = SelectedThemeRequiresReload();

            _settings.Save();
            if (shouldReloadTheme && _reloadUiForTheme is not null)
            {
                _reloadUiForTheme(_settings);
                return;
            }

            ShowSavedMessage();
        }

        private void ShowSavedMessage()
        {
            _savedMessageTimer.Stop();
            SavedLabel.Text = "Settings saved.";
            SavedLabel.Visibility = Visibility.Visible;
            _savedMessageTimer.Start();
        }

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

        private void LoadThemeOptions()
        {
            ThemeComboBox.ItemsSource = AppThemeManager.Themes;
            SelectTheme(AppThemeManager.CurrentThemeKey);
        }

        private void SelectTheme(string? themeKey)
        {
            string normalizedThemeKey = AppThemeManager.NormalizeThemeKey(themeKey);
            ThemeComboBox.SelectedItem = AppThemeManager.Themes.FirstOrDefault(theme =>
                string.Equals(theme.Key, normalizedThemeKey, StringComparison.OrdinalIgnoreCase))
                ?? AppThemeManager.Themes[0];
        }

        private string GetSelectedThemeKey()
        {
            if (ThemeComboBox.SelectedItem is AppThemeDefinition selectedTheme)
                return AppThemeManager.NormalizeThemeKey(selectedTheme.Key);

            return AppThemeManager.NormalizeThemeKey(ThemeComboBox.SelectedValue as string);
        }

        private bool SelectedThemeRequiresReload()
        {
            return !string.Equals(GetSelectedThemeKey(), AppThemeManager.CurrentThemeKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
