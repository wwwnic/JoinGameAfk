using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.Theme;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage : Page
    {
        private const double DragAutoScrollEdgeDistance = 56;
        private const double DragAutoScrollStep = 28;
        private const double PriorityChampionChipWidth = 48;
        private const double PriorityChampionChipHeight = 64;
        private const int DefaultPriorityChampionChipsPerRow = 5;
        private const string ChampionPillTag = "ChampionPill";

        private readonly ChampionDataSettings _championDataSettings;
        private readonly RolePlanSettings _rolePlanSettings;
        private readonly LeagueClientConnectionContext _leagueClientConnection;
        private readonly Action<string>? _logMessage;
        private readonly Action<string>? _logErrorMessage;
        private readonly ChampionCatalogSyncCoordinator _championCatalogSyncCoordinator;
        private readonly RolePlanProfileStore _rolePlanProfileStore = new();
        private readonly ObservableCollection<RolePlanProfileListItem> _profiles = [];
        private readonly DispatcherTimer _profileStatusTimer;
        private List<ChampionInfo> _allChampions;
        private List<ChampionInfo> _filteredChampions;
        private List<ChampionReferenceItem> _filteredChampionReferences;
        private readonly List<PositionRow> _rows;
        private readonly ObservableCollection<RoleFilterOption> _roleFilters;
        private readonly HashSet<Position> _activeRoleFilters = [];
        private LeagueGameMode _activeRolePlanMode = LeagueGameMode.Modern;
        private Brush _activeTargetBrush = Brushes.DodgerBlue;
        private Brush _inactiveTargetBrush = Brushes.SlateGray;
        private Brush _dropHoverTargetBrush = Brushes.DeepSkyBlue;
        private Brush _activeTargetBackgroundBrush = Brushes.Transparent;
        private Brush _inactiveTargetBackgroundBrush = Brushes.Transparent;
        private Brush _dropHoverBackgroundBrush = Brushes.Transparent;
        private PositionRow? _activeTargetRow;
        private bool _activeTargetIsPick;
        private Point _dragStartPoint;
        private ChampionSelectionItem? _draggedChampion;
        private ChampionSelectionItem? _pendingSelectionChampion;
        private ModifierKeys _pendingSelectionModifiers;
        private bool _pendingSelectionShouldToggle;
        private ChampionInfo? _draggedReferenceChampion;
        private bool _suppressReferenceChampionClick;
        private bool _isChampionDragActive;
        private ChampionDragData? _activeChampionDragData;
        private UIElement? _dragCaptureElement;
        private ChampionSelectionItem? _dragHoverChampion;
        private ChampionSelectionItem? _swapDropChampion;
        private ChampionSelectionItem? _duplicateDropChampion;
        private ChampionSelectionItem? _moveOriginDropChampion;
        private ChampionSelectionItem? _selectionAnchorChampion;
        private PositionRow? _dragHoverRow;
        private bool _dragHoverIsPick;
        private bool _dragHoverInsertAfter;
        private int? _dragHoverTargetIndex;
        private bool _isPreferenceSavePending;
        private DispatcherOperation? _pendingPreferenceSaveOperation;
        private ChampionInfo? _selectedChampionPictureChampion;
        private string? _originalChampionPictureFileName;
        private string? _pendingChampionPictureFileName;
        private bool _isUpdatingChampionPicturePicker;
        private bool _isChampionPictureDownloadInProgress;
        private bool _isApplyingChampionDataSettings;
        private bool _isChampionDataOperationInProgress;
        private bool _hasSynchronizedChampionSelectGameMode;
        private string? _checkedChampionCatalogPlatformId;
        private string? _latestConfiguredRegionDataDragonVersion;
        private ChampionInfo? _selectedProfileIconChampion;
        private Guid? _editingProfileId;
        private bool _isReloadingProfiles;

        public static readonly DependencyProperty IsChampionSelectLockActiveProperty = DependencyProperty.Register(
            nameof(IsChampionSelectLockActive),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(false));

        public bool IsChampionSelectLockActive
        {
            get => (bool)GetValue(IsChampionSelectLockActiveProperty);
            private set => SetValue(IsChampionSelectLockActiveProperty, value);
        }

        public static readonly DependencyProperty IsPriorityEditingEnabledProperty = DependencyProperty.Register(
            nameof(IsPriorityEditingEnabled),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(true));

        public bool IsPriorityEditingEnabled
        {
            get => (bool)GetValue(IsPriorityEditingEnabledProperty);
            private set => SetValue(IsPriorityEditingEnabledProperty, value);
        }

        public static readonly DependencyProperty HasSelectedChampionsProperty = DependencyProperty.Register(
            nameof(HasSelectedChampions),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(false));

        public bool HasSelectedChampions
        {
            get => (bool)GetValue(HasSelectedChampionsProperty);
            private set => SetValue(HasSelectedChampionsProperty, value);
        }

        public static readonly DependencyProperty SelectedChampionCountTextProperty = DependencyProperty.Register(
            nameof(SelectedChampionCountText),
            typeof(string),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata("(0)"));

        public string SelectedChampionCountText
        {
            get => (string)GetValue(SelectedChampionCountTextProperty);
            private set => SetValue(SelectedChampionCountTextProperty, value);
        }

        public static readonly DependencyProperty IsSearchDeleteDropTargetProperty = DependencyProperty.Register(
            nameof(IsSearchDeleteDropTarget),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(false));

        public bool IsSearchDeleteDropTarget
        {
            get => (bool)GetValue(IsSearchDeleteDropTargetProperty);
            private set => SetValue(IsSearchDeleteDropTargetProperty, value);
        }

        public static readonly DependencyProperty IsSearchDeleteDropHintVisibleProperty = DependencyProperty.Register(
            nameof(IsSearchDeleteDropHintVisible),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(false));

        public bool IsSearchDeleteDropHintVisible
        {
            get => (bool)GetValue(IsSearchDeleteDropHintVisibleProperty);
            private set => SetValue(IsSearchDeleteDropHintVisibleProperty, value);
        }

        public static readonly DependencyProperty IsChampionPictureEditModeProperty = DependencyProperty.Register(
            nameof(IsChampionPictureEditMode),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(false));

        public bool IsChampionPictureEditMode
        {
            get => (bool)GetValue(IsChampionPictureEditModeProperty);
            private set => SetValue(IsChampionPictureEditModeProperty, value);
        }

        public static readonly DependencyProperty IsLeagueClassicPlanModeProperty = DependencyProperty.Register(
            nameof(IsLeagueClassicPlanMode),
            typeof(bool),
            typeof(ChampionPrioritiesPage),
            new PropertyMetadata(false));

        public bool IsLeagueClassicPlanMode
        {
            get => (bool)GetValue(IsLeagueClassicPlanModeProperty);
            private set => SetValue(IsLeagueClassicPlanModeProperty, value);
        }

        public ChampionPrioritiesPage(
            ChampionDataSettings championDataSettings,
            RolePlanSettings rolePlanSettings,
            LeagueClientConnectionContext leagueClientConnection,
            ChampionCatalogSyncCoordinator championCatalogSyncCoordinator,
            Action<string>? logMessage = null,
            Action<string>? logErrorMessage = null)
        {
            InitializeComponent();
            _profileStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _profileStatusTimer.Tick += (_, _) => HideProfileStatus();
            _championDataSettings = championDataSettings;
            _rolePlanSettings = rolePlanSettings;
            _leagueClientConnection = leagueClientConnection;
            _championCatalogSyncCoordinator = championCatalogSyncCoordinator;
            _logMessage = logMessage;
            _logErrorMessage = logErrorMessage;
            ApplyChampionDataSettingsToControls();
            RefreshChampionCatalogSyncStatus();
            ChampionSearchBox.SizeChanged += ChampionSearchBox_SizeChanged;
            RefreshThemeBrushes();
            Unloaded += ChampionPrioritiesPage_Unloaded;
            AppThemeManager.ThemeChanged += RefreshTheme;

            _allChampions = ChampionCatalog.All
                .OrderBy(champion => champion.Name)
                .ToList();
            _filteredChampions = [.. _allChampions];
            _filteredChampionReferences = CreateChampionReferenceItems(_filteredChampions);
            ChampionReferenceList.ItemsSource = _filteredChampionReferences;

            _roleFilters =
            [
                new(Position.None, "All"),
                new(Position.Top, "Top"),
                new(Position.Jungle, "Jungle"),
                new(Position.Mid, "Mid"),
                new(Position.Adc, "ADC"),
                new(Position.Support, "Support")
            ];
            _roleFilters[0].IsSelected = true;
            RoleFilterList.ItemsSource = _roleFilters;

            _rows = [];
            foreach (Position position in Enum.GetValues<Position>().Where(position => position != Position.None))
            {
                var row = new PositionRow
                {
                    Position = position,
                    PositionName = position.ToString()
                };

                row.PickBorderBrush = _inactiveTargetBrush;
                row.BanBorderBrush = _inactiveTargetBrush;
                row.PickBackgroundBrush = _inactiveTargetBackgroundBrush;
                row.BanBackgroundBrush = _inactiveTargetBackgroundBrush;
                _rows.Add(row);
            }

            PositionList.ItemsSource = _rows;
            ReloadRolePlanRows();

            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
            ChampionImageSelectionStore.SelectionsChanged += ChampionImageSelectionStore_SelectionsChanged;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
            _championDataSettings.Saved += ChampionDataSettings_Saved;
            _leagueClientConnection.ConnectionChanged += LeagueClientConnection_ConnectionChanged;
        }

        public void SetChampionSelectActive(bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (IsChampionSelectLockActive == isActive)
                    return;

                if (isActive)
                {
                    if (_isChampionDragActive)
                        FinishChampionDrag(drop: false);

                    ClearChampionSelection();
                    ClearInsertionIndicator();
                    HideDragPreview();
                    IsSearchDeleteDropTarget = false;
                    IsSearchDeleteDropHintVisible = false;
                    ClearPendingChampionSelection();
                    SetChampionPictureEditMode(false);
                }

                IsChampionSelectLockActive = isActive;
                IsPriorityEditingEnabled = !isActive;
                AllowDrop = !isActive;
                _hasSynchronizedChampionSelectGameMode = false;
                RefreshTargetBrushes();
            });
        }

        private void ChampionPrioritiesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _pendingPreferenceSaveOperation?.Abort();
            _pendingPreferenceSaveOperation = null;
            FlushPendingPreferenceSave();
            AppThemeManager.ThemeChanged -= RefreshTheme;
            ChampionCatalog.CatalogChanged -= ChampionCatalog_CatalogChanged;
            ChampionImageSelectionStore.SelectionsChanged -= ChampionImageSelectionStore_SelectionsChanged;
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
            _championDataSettings.Saved -= ChampionDataSettings_Saved;
            _leagueClientConnection.ConnectionChanged -= LeagueClientConnection_ConnectionChanged;
        }
    }
}
