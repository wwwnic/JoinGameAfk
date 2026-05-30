using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JoinGameAfk.Constant;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
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

        private readonly ChampSelectSettings _settings;
        private readonly OverlaySettings _overlaySettings;
        private readonly DispatcherTimer _savedMessageTimer;
        private readonly Action<ChampSelectSettings, OverlaySettings, string?, bool>? _reloadUiForTheme;
        private readonly Action<string>? _logMessage;
        private readonly Action<string>? _logErrorMessage;
        private readonly DataDragonChampionCatalogService _championCatalogRemoteService = new();
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
        private bool _isThemePickerExpanded;
        private string? _pendingInitialThemeSelectionKey;
        private string _selectedThemeKey = AppThemeManager.DefaultThemeKey;

        public GeneralSettingsPage(
            ChampSelectSettings settings,
            OverlaySettings overlaySettings,
            Action<ChampSelectSettings, OverlaySettings, string?, bool>? reloadUiForTheme = null,
            Action<string>? logMessage = null,
            Action<string>? logErrorMessage = null,
            string? selectedThemeKey = null,
            bool themePickerExpanded = false)
        {
            _settings = settings;
            _overlaySettings = overlaySettings;
            InitializeComponent();
            _reloadUiForTheme = reloadUiForTheme;
            _logMessage = logMessage;
            _logErrorMessage = logErrorMessage;
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
            RefreshChampionCatalogSyncStatus();
            RefreshChampionPictureCacheStatus();
            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
            _settings.Saved += Settings_Saved;
            Unloaded += GeneralSettingsPage_Unloaded;
            LoadThemeOptions();
            ApplySettingsToControls();
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
            ChampionCatalog.CatalogChanged -= ChampionCatalog_CatalogChanged;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
            Unloaded -= GeneralSettingsPage_Unloaded;
        }
    }
}