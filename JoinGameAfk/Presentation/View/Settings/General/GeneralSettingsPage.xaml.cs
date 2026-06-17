using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.Theme;
using JoinGameAfk.Validation;

namespace JoinGameAfk.Presentation.View.Settings.General
{
    public partial class GeneralSettingsPage : Page
    {
        private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(3);
        private const int CollapsedPickerRows = 2;
        private const double ThemePickerTileOuterWidth = 192;
        private const double ThemePickerTileOuterHeight = 84;

        private readonly GeneralSettings _settings;
        private readonly ChampionDataSettings _championDataSettings;
        private readonly OverlaySettings _overlaySettings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly Action<GeneralSettings, OverlaySettings, string?, bool>? _reloadUiForTheme;
        private readonly List<ThemePickerOption> _themeOptions = [];
        private NumericInputRule _readyCheckAcceptDelayRule = null!;
        private NumericInputRule _pickLockDelayRule = null!;
        private NumericInputRule _championHoverDelayRule = null!;
        private NumericInputRule _planningHoverDelayRule = null!;
        private NumericInputRule _banLockDelayRule = null!;
        private NumericInputRule _champSelectPollIntervalRule = null!;
        private NumericInputRule _champSelectEventFallbackPollIntervalRule = null!;
        private bool _isUpdatingAutomationControls;
        private bool _isApplyingSettingsToControls;
        private bool _isApplyingChampionDataSettingsToControls;
        private bool _isThemePickerExpanded;
        private string? _pendingInitialThemeSelectionKey;
        private string _selectedThemeKey = AppThemeManager.DefaultThemeKey;

        public GeneralSettingsPage(
            GeneralSettings settings,
            ChampionDataSettings championDataSettings,
            OverlaySettings overlaySettings,
            Action<GeneralSettings, OverlaySettings, string?, bool>? reloadUiForTheme = null,
            string? selectedThemeKey = null,
            bool themePickerExpanded = false)
        {
            _settings = settings;
            _championDataSettings = championDataSettings;
            _overlaySettings = overlaySettings;
            InitializeComponent();
            _reloadUiForTheme = reloadUiForTheme;
            _isThemePickerExpanded = themePickerExpanded;
            _pendingInitialThemeSelectionKey = string.IsNullOrWhiteSpace(selectedThemeKey)
                ? null
                : AppThemeManager.NormalizeThemeKey(selectedThemeKey);
            _savedMessageTimer = new DispatcherTimer
            {
                Interval = SavedMessageDuration
            };
            _savedMessageTimer.Tick += (_, _) =>
            {
                _savedMessageTimer.Stop();
                FloatingSettingsStatusBar.Visibility = Visibility.Collapsed;
            };

            StoragePathTextBlock.Text = AppStorage.DirectoryPath;
            ChampionPictureFolderPathTextBlock.Text = ChampionTileCatalog.TileDirectoryPath;
            _settings.Saved += Settings_Saved;
            _championDataSettings.Saved += ChampionDataSettings_Saved;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
            Unloaded += GeneralSettingsPage_Unloaded;
            LoadThemeOptions();
            ApplySettingsToControls();
            ApplyChampionDataSettingsToControls();
            RefreshChampionPictureCacheStatus();
            AttachNumericInputValidation();
            UpdateAutomationInputStates();
            AttachDirtyStateTracking();
            RefreshDirtyState();
        }

        private void Settings_Saved()
        {
            Dispatcher.TryInvoke(() =>
            {
                if (DirtySettingsBar.Visibility == Visibility.Visible)
                    return;

                ApplySettingsToControls();
                UpdateAutomationInputStates();
                RefreshDirtyState();
            });
        }

        private void GeneralSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _settings.Saved -= Settings_Saved;
            _championDataSettings.Saved -= ChampionDataSettings_Saved;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
            Unloaded -= GeneralSettingsPage_Unloaded;
        }

        private void ChampionDataSettings_Saved()
        {
            Dispatcher.TryInvoke(ApplyChampionDataSettingsToControls);
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.TryInvoke(RefreshChampionPictureCacheStatus);
        }

        public event Action? OpenChampionDataRequested;

        private void OpenChampionDataButton_Click(object sender, RoutedEventArgs e)
        {
            OpenChampionDataRequested?.Invoke();
        }
    }
}
