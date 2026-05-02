using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
using JoinGameAfk.Theme;
using JoinGameAfk.Validation;

namespace JoinGameAfk.View
{
    public partial class SettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);

        private readonly ChampSelectSettings _settings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly Action<ChampSelectSettings>? _reloadUiForTheme;
        private readonly DataDragonChampionCatalogService _championCatalogRemoteService = new();
        private readonly List<NumericInputRule> _numericInputRules;
        private NumericInputRule _readyCheckAcceptDelayRule = null!;
        private NumericInputRule _pickLockDelayRule = null!;
        private NumericInputRule _championHoverDelayRule = null!;
        private NumericInputRule _banLockDelayRule = null!;
        private NumericInputRule _champSelectPollIntervalRule = null!;

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
            RefreshChampionCatalogSyncStatus();
            LoadThemeOptions();
            ReadyCheckAcceptDelayBox.Text = _settings.ReadyCheckAcceptDelaySeconds.ToString();
            AutoLockSelectionCheckBox.IsChecked = _settings.AutoLockSelectionEnabled;
            PickLockDelayBox.Text = _settings.PickLockDelaySeconds.ToString();
            ChampionHoverDelayBox.Text = _settings.ChampionHoverDelaySeconds.ToString();
            BanLockDelayBox.Text = _settings.BanLockDelaySeconds.ToString();
            ChampSelectPollIntervalBox.Text = _settings.ChampSelectPollIntervalMs.ToString();
            _numericInputRules = AttachNumericInputValidation();
        }

        private List<NumericInputRule> AttachNumericInputValidation()
        {
            _readyCheckAcceptDelayRule = InputValidator.AttachInteger(ReadyCheckAcceptDelayBox, "Auto accept delay", minimum: 0);
            _pickLockDelayRule = InputValidator.AttachInteger(PickLockDelayBox, "Pick lock timer", minimum: 0);
            _championHoverDelayRule = InputValidator.AttachInteger(ChampionHoverDelayBox, "Champion hover delay", minimum: 0);
            _banLockDelayRule = InputValidator.AttachInteger(BanLockDelayBox, "Ban lock timer", minimum: 0);
            _champSelectPollIntervalRule = InputValidator.AttachInteger(ChampSelectPollIntervalBox, "Polling interval", minimum: 100, maximum: 5000);

            return
            [
                _readyCheckAcceptDelayRule,
                _pickLockDelayRule,
                _championHoverDelayRule,
                _banLockDelayRule,
                _champSelectPollIntervalRule
            ];
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSettingsInput(out var input))
                return;

            _settings.ReadyCheckAcceptDelaySeconds = input.ReadyCheckAcceptDelaySeconds;
            _settings.AutoLockSelectionEnabled = AutoLockSelectionCheckBox.IsChecked == true;
            _settings.PickLockDelaySeconds = input.PickLockDelaySeconds;
            _settings.ChampionHoverDelaySeconds = input.ChampionHoverDelaySeconds;
            _settings.BanLockDelaySeconds = input.BanLockDelaySeconds;
            _settings.ChampSelectPollIntervalMs = input.ChampSelectPollIntervalMs;
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

        private bool TryReadSettingsInput(out SettingsInputValues input)
        {
            input = default;

            if (!InputValidator.TryValidateAll(_numericInputRules, out var invalidRule, out string errorMessage))
            {
                ShowValidationMessage(errorMessage);
                invalidRule?.TextBox.Focus();
                invalidRule?.TextBox.SelectAll();
                return false;
            }

            if (!_readyCheckAcceptDelayRule.TryGetInt32(out int readyCheckDelay)
                || !_pickLockDelayRule.TryGetInt32(out int pickDelay)
                || !_championHoverDelayRule.TryGetInt32(out int hoverDelay)
                || !_banLockDelayRule.TryGetInt32(out int banDelay)
                || !_champSelectPollIntervalRule.TryGetInt32(out int pollInterval))
            {
                ShowValidationMessage("Fix invalid settings before saving.");
                return false;
            }

            input = new SettingsInputValues(
                readyCheckDelay,
                pickDelay,
                hoverDelay,
                banDelay,
                pollInterval);

            return true;
        }

        private void ShowSavedMessage()
        {
            ShowStatusMessage("Settings saved.", "AccentGreenTextBrush", Brushes.ForestGreen);
        }

        private void ShowValidationMessage(string message)
        {
            ShowStatusMessage(message, "DangerTextBrush", Brushes.IndianRed);
        }

        private void ShowStatusMessage(string message, string brushResourceKey, Brush fallbackBrush)
        {
            _savedMessageTimer.Stop();
            SavedLabel.Text = message;
            SavedLabel.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
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

        private async void RefreshChampionCatalogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmChampionCatalogRefresh())
            {
                SetChampionCatalogRefreshStatus("Update canceled.", "TextSoftBrush", Brushes.SlateGray);
                return;
            }

            RefreshChampionCatalogButton.IsEnabled = false;
            SetChampionCatalogRefreshStatus("Updating champion list from Riot Data Dragon...", "TextSoftBrush", Brushes.SlateGray);

            try
            {
                var result = await ChampionCatalog.RefreshFromDataDragonAsync(_championCatalogRemoteService);
                RefreshChampionCatalogSyncStatus(result);
                SetChampionCatalogRefreshStatus(
                    "Champion list updated.",
                    "AccentGreenTextBrush",
                    Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetChampionCatalogRefreshStatus(
                    $"Champion list update failed. Existing local file was kept. {ex.Message}",
                    "DangerTextBrush",
                    Brushes.IndianRed);
            }
            finally
            {
                RefreshChampionCatalogButton.IsEnabled = true;
            }
        }

        private bool ConfirmChampionCatalogRefresh()
        {
            var result = MessageBox.Show(
                Window.GetWindow(this),
                "This will contact Riot Data Dragon at ddragon.leagueoflegends.com and update only the local champion list file.\n\nYour champion priorities and settings are kept.\n\nContinue?",
                "Update Champion List",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.Cancel);

            return result == MessageBoxResult.OK;
        }

        private void RefreshChampionCatalogSyncStatus(ChampionCatalogRefreshResult? refreshResult = null)
        {
            string? dataDragonVersion = refreshResult?.DataDragonVersion;
            int championCount = refreshResult?.ChampionCount ?? 0;

            if (refreshResult is null)
            {
                var syncInfo = ChampionCatalog.GetLocalSyncInfo();
                dataDragonVersion = syncInfo.DataDragonVersion;
                championCount = syncInfo.ChampionCount;
            }

            if (string.IsNullOrWhiteSpace(dataDragonVersion))
            {
                SetChampionCatalogSyncStatus(
                    "Champion list has never been synced with Riot Data Dragon.",
                    "TextSoftBrush",
                    Brushes.SlateGray);
                return;
            }

            SetChampionCatalogSyncStatus(
                $"Synced with Riot Data Dragon {dataDragonVersion} ({championCount} champions).",
                "TextSoftBrush",
                Brushes.SlateGray);
        }

        private void SetChampionCatalogSyncStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionCatalogSyncStatusTextBlock.Text = message;
            ChampionCatalogSyncStatusTextBlock.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
        }

        private void SetChampionCatalogRefreshStatus(string message, string brushResourceKey, Brush fallbackBrush)
        {
            ChampionCatalogRefreshStatusLabel.Text = message;
            ChampionCatalogRefreshStatusLabel.Foreground = TryFindResource(brushResourceKey) as Brush ?? fallbackBrush;
            ChampionCatalogRefreshStatusLabel.Visibility = Visibility.Visible;
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

        private readonly record struct SettingsInputValues(
            int ReadyCheckAcceptDelaySeconds,
            int PickLockDelaySeconds,
            int ChampionHoverDelaySeconds,
            int BanLockDelaySeconds,
            int ChampSelectPollIntervalMs);
    }
}
