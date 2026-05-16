using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;
using JoinGameAfk.Theme;
using JoinGameAfk.View.Controls;

namespace JoinGameAfk.View
{
    public partial class ChampionPrioritiesPage : Page
    {
        private const double DragAutoScrollEdgeDistance = 56;
        private const double DragAutoScrollStep = 28;
        private const double PriorityChampionChipWidth = 48;
        private const double PriorityChampionChipHeight = 64;
        private const int DefaultPriorityChampionChipsPerRow = 5;
        private const string ChampionPillTag = "ChampionPill";

        private readonly ChampSelectSettings _settings;
        private List<ChampionInfo> _allChampions;
        private List<ChampionInfo> _filteredChampions;
        private List<ChampionReferenceItem> _filteredChampionReferences;
        private readonly List<PositionRow> _rows;
        private readonly ObservableCollection<RoleFilterOption> _roleFilters;
        private readonly HashSet<Position> _activeRoleFilters = [];
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
        private ChampionSelectionItem? _selectionAnchorChampion;
        private PositionRow? _dragHoverRow;
        private bool _dragHoverIsPick;
        private bool _dragHoverInsertAfter;
        private int? _dragHoverTargetIndex;
        private bool _isPreferenceSavePending;
        private DispatcherOperation? _pendingPreferenceSaveOperation;
        private string _championImageSelectionSignature;

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

        public ChampionPrioritiesPage(ChampSelectSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _championImageSelectionSignature = CreateChampionImageSelectionSignature(_settings.ChampionImageFileNames);
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
                new(Position.Top, "Top"),
                new(Position.Jungle, "Jungle"),
                new(Position.Mid, "Mid"),
                new(Position.Adc, "Adc"),
                new(Position.Support, "Support")
            ];
            RoleFilterList.ItemsSource = _roleFilters;

            _rows = [];
            foreach (Position position in Enum.GetValues<Position>().Where(position => position != Position.None))
            {
                var pref = _settings.Preferences.GetValueOrDefault(position) ?? new PositionPreference();
                var row = new PositionRow
                {
                    Position = position,
                    PositionName = position.ToString()
                };

                foreach (int championId in pref.PickChampionIds)
                {
                    row.PickChampions.Add(CreateSelectionItem(row, championId, isPick: true));
                }

                foreach (int championId in pref.BanChampionIds)
                {
                    row.BanChampions.Add(CreateSelectionItem(row, championId, isPick: false));
                }

                UpdateRowTextFromCollection(row, isPick: true);
                UpdateRowTextFromCollection(row, isPick: false);
                row.PickBorderBrush = _inactiveTargetBrush;
                row.BanBorderBrush = _inactiveTargetBrush;
                row.PickBackgroundBrush = _inactiveTargetBackgroundBrush;
                row.BanBackgroundBrush = _inactiveTargetBackgroundBrush;
                _rows.Add(row);
            }

            PositionList.ItemsSource = _rows;
            if (_rows.Count > 0)
            {
                SetActiveTarget(_rows[0], isPick: true);
            }

            ChampionCatalog.CatalogChanged += ChampionCatalog_CatalogChanged;
            ChampionTileCatalog.TileCatalogChanged += ChampionTileCatalog_TileCatalogChanged;
            _settings.Saved += Settings_Saved;
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
                    ClearPendingChampionSelection();
                }

                IsChampionSelectLockActive = isActive;
                IsPriorityEditingEnabled = !isActive;
                AllowDrop = !isActive;
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
            ChampionTileCatalog.TileCatalogChanged -= ChampionTileCatalog_TileCatalogChanged;
            _settings.Saved -= Settings_Saved;
        }

        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshChampionCatalogView);
        }

        private void Settings_Saved()
        {
            string imageSelectionSignature = CreateChampionImageSelectionSignature(_settings.ChampionImageFileNames);
            if (string.Equals(imageSelectionSignature, _championImageSelectionSignature, StringComparison.Ordinal))
                return;

            _championImageSelectionSignature = imageSelectionSignature;
            Dispatcher.InvokeAsync(RefreshChampionImages);
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshChampionImages);
        }

        private void RefreshChampionCatalogView()
        {
            _allChampions = ChampionCatalog.All
                .OrderBy(champion => champion.Name)
                .ToList();

            RefreshConfiguredChampionDisplayText();
            RefreshChampionImages();
            UpdateChampionFilter();
        }

        private void RefreshChampionImages()
        {
            _championImageSelectionSignature = CreateChampionImageSelectionSignature(_settings.ChampionImageFileNames);
            foreach (var champion in _rows.SelectMany(row => row.PickChampions.Concat(row.BanChampions)))
            {
                champion.PortraitImageSource = ChampionTileCatalog.GetSelectedImageSource(champion.ChampionId, _settings);
            }

            _filteredChampionReferences = CreateChampionReferenceItems(_filteredChampions);
            ChampionReferenceList.ItemsSource = _filteredChampionReferences;
        }

        private void RefreshConfiguredChampionDisplayText()
        {
            foreach (var row in _rows)
            {
                foreach (var champion in row.PickChampions.Concat(row.BanChampions))
                    champion.DisplayText = ChampionCatalog.FormatWithName(champion.ChampionId);

                UpdateRowTextFromCollection(row, isPick: true);
                UpdateRowTextFromCollection(row, isPick: false);
            }
        }

        private void ChampionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateChampionFilter();
        }

        private void ChampionSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.A)
                {
                    ChampionSearchBox.SelectAll();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Back)
                {
                    ChampionSearchBox.Clear();
                    e.Handled = true;
                    return;
                }
            }

            if (!IsPriorityEditingEnabled)
                return;

            if (e.Key != Key.Enter || _filteredChampions.Count == 0)
                return;

            if (AddFirstFilteredChampion())
            {
                e.Handled = true;
            }
        }

        private void ChampionSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChampionSearchBox.Text.Length > 0)
                ChampionSearchBox.Clear();

            FocusSearchBox();
            e.Handled = true;
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (DeleteSelectedChampions())
            {
                e.Handled = true;
            }
        }

        private void RoleFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RoleFilterOption filter)
                return;

            filter.IsSelected = !filter.IsSelected;
            if (filter.IsSelected)
                _activeRoleFilters.Add(filter.Position);
            else
                _activeRoleFilters.Remove(filter.Position);

            UpdateChampionFilter();
            ChampionSearchBox.Focus();
            e.Handled = true;
        }

        private void Page_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!IsPriorityEditingEnabled || _activeTargetRow is null || IsSearchBoxFocused() || string.IsNullOrEmpty(e.Text))
                return;

            FocusSearchBox();
            InsertSearchText(e.Text);
            e.Handled = true;
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isChampionDragActive && e.Key == Key.Escape)
            {
                FinishChampionDrag(drop: false);
                e.Handled = true;
                return;
            }

            if (!IsPriorityEditingEnabled)
                return;

            if (!_isChampionDragActive
                && !IsSearchBoxFocused()
                && IsChampionDeleteKey(e.Key)
                && TryDeleteFocusedOrSelectedChampion())
            {
                e.Handled = true;
                return;
            }

            if (_activeTargetRow is null || IsSearchBoxFocused())
                return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.A)
                {
                    FocusSearchBox();
                    ChampionSearchBox.SelectAll();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Back)
                {
                    FocusSearchBox();
                    ChampionSearchBox.Clear();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Back)
            {
                FocusSearchBox();
                RemoveSearchText();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && !IsFocusWithinChampionPlan() && AddFirstFilteredChampion())
            {
                e.Handled = true;
            }
        }

        private void ChampionReferenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (_suppressReferenceChampionClick)
            {
                _suppressReferenceChampionClick = false;
                e.Handled = true;
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is not ChampionReferenceItem championReference)
                return;

            var champion = championReference.Champion;
            TryAddChampionToActiveTarget(champion);
            _draggedReferenceChampion = null;
        }

        private void ChampionReferenceButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionReferenceItem championReference)
                return;

            _suppressReferenceChampionClick = false;
            _draggedReferenceChampion = championReference.Champion;
            _dragStartPoint = e.GetPosition(this);
        }

        private void ChampionReferenceButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (_isChampionDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed || _draggedReferenceChampion is null)
                return;

            Point currentPosition = e.GetPosition(this);
            if (!IsPastDragThreshold(_dragStartPoint, currentPosition))
                return;

            var champion = _draggedReferenceChampion;
            _suppressReferenceChampionClick = true;
            StartChampionDrag((DependencyObject)sender, ChampionDragData.FromReference(champion, _settings), currentPosition);

            e.Handled = true;
        }

        private void ChampionTarget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not PositionRow row)
                return;

            bool isControlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!isControlPressed && !TryFindChampionItemTarget(e.OriginalSource as DependencyObject, out _, out _))
                ClearChampionSelection();

            bool isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            SetActiveTarget(row, isPick);
        }

        private void ChampionTarget_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (!TryResolveChampionTarget(sender, out var row, out bool isPick))
                return;

            SetActiveTarget(row, isPick, focusSearch: false);
            (sender as FrameworkElement)?.BringIntoView();
        }

        private void ChampionTarget_KeyDown(object sender, KeyEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (e.Key != Key.Enter && e.Key != Key.Space)
                return;

            if (!TryResolveChampionTarget(sender, out var row, out bool isPick))
                return;

            SetActiveTarget(row, isPick, focusSearch: false);
            if (!string.IsNullOrWhiteSpace(ChampionSearchBox.Text) && AddFirstFilteredChampion())
            {
                e.Handled = true;
                return;
            }

            FocusSearchBox();
            e.Handled = true;
        }

        private static bool TryResolveChampionTarget(object? sender, [NotNullWhen(true)] out PositionRow? row, out bool isPick)
        {
            row = null;
            isPick = false;

            if ((sender as FrameworkElement)?.DataContext is not PositionRow targetRow)
                return false;

            row = targetRow;
            isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private void ChampionItem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            SetActiveTarget(champion.Row, champion.IsPick, focusSearch: false);
            (sender as FrameworkElement)?.BringIntoView();
        }

        private void ChampionItem_KeyDown(object sender, KeyEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                UpdateChampionSelection(
                    champion,
                    Keyboard.Modifiers,
                    shouldToggleSelection: e.Key == Key.Space);
                SetActiveTarget(champion.Row, champion.IsPick, focusSearch: false);
                e.Handled = true;
                return;
            }

            if (!IsChampionDeleteKey(e.Key))
                return;

            if (TryDeleteFocusedOrSelectedChampion(champion))
                e.Handled = true;
        }

        private static bool IsChampionDeleteKey(Key key)
        {
            return key is Key.Back or Key.Delete;
        }

        private void UpdateChampionFilter()
        {
            string search = ChampionSearchBox.Text.Trim();
            var roleFilteredChampions = _allChampions.Where(MatchesActiveRoleFilter);

            _filteredChampions = string.IsNullOrWhiteSpace(search)
                ? [.. roleFilteredChampions]
                : [.. roleFilteredChampions
                    .Select(champion => new
                    {
                        Champion = champion,
                        Score = GetChampionSearchScore(champion, search)
                    })
                    .Where(result => result.Score >= 0)
                    .OrderBy(result => result.Score)
                    .ThenBy(result => result.Champion.Name)
                    .ThenBy(result => result.Champion.Id)
                    .Select(result => result.Champion)];

            _filteredChampionReferences = CreateChampionReferenceItems(_filteredChampions);
            ChampionReferenceList.ItemsSource = _filteredChampionReferences;
        }

        private List<ChampionReferenceItem> CreateChampionReferenceItems(IEnumerable<ChampionInfo> champions)
        {
            return champions
                .Select(champion => new ChampionReferenceItem(champion, _settings))
                .ToList();
        }

        private bool MatchesActiveRoleFilter(ChampionInfo champion)
        {
            return _activeRoleFilters.Count == 0
                || champion.Roles.Any(_activeRoleFilters.Contains);
        }

        private static int GetChampionSearchScore(ChampionInfo champion, string search)
        {
            string championName = champion.Name;
            string championId = champion.Id.ToString();

            if (championName.Equals(search, StringComparison.OrdinalIgnoreCase)
                || championId.Equals(search, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (championName.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                return 1;

            if (championName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.StartsWith(search, StringComparison.OrdinalIgnoreCase)))
            {
                return 2;
            }

            if (championName.Contains(search, StringComparison.OrdinalIgnoreCase))
                return 3;

            if (championId.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                return 4;

            if (championId.Contains(search, StringComparison.OrdinalIgnoreCase))
                return 5;

            return -1;
        }

        private bool TryAddChampionToActiveTarget(ChampionInfo champion)
        {
            if (!IsPriorityEditingEnabled || _activeTargetRow is null)
                return false;

            InsertChampion(_activeTargetRow, _activeTargetIsPick, champion, null);
            ChampionSearchBox.Focus();
            return true;
        }

        private bool AddFirstFilteredChampion()
        {
            if (_filteredChampions.Count == 0 || !TryAddChampionToActiveTarget(_filteredChampions[0]))
                return false;

            ChampionSearchBox.Clear();
            return true;
        }

        private void SetActiveTarget(PositionRow row, bool isPick, bool focusSearch = true)
        {
            _activeTargetRow = row;
            _activeTargetIsPick = isPick;

            RefreshTargetBrushes();

            if (!focusSearch)
                return;

            ChampionSearchBox.Focus();
            ChampionSearchBox.SelectAll();
        }

        private void UpdateChampionSelection(ChampionSelectionItem champion, ModifierKeys modifiers, bool shouldToggleSelection)
        {
            bool isControlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool isShiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            shouldToggleSelection |= isControlPressed;

            if (isShiftPressed
                && _selectionAnchorChampion is not null
                && TryGetSelectionRange(_selectionAnchorChampion, champion, out var range))
            {
                if (!isControlPressed)
                    ClearChampionSelection(resetAnchor: false);

                foreach (var item in range)
                    item.IsSelected = true;

                RefreshSelectedChampionState();
                return;
            }

            if (shouldToggleSelection)
            {
                champion.IsSelected = !champion.IsSelected;
                _selectionAnchorChampion = champion;
                RefreshSelectedChampionState();
                return;
            }

            ClearChampionSelection();
            champion.IsSelected = true;
            _selectionAnchorChampion = champion;
            RefreshSelectedChampionState();
        }

        private bool ShouldTreatClickAsControlSelection(ChampionSelectionItem champion, bool isShiftPressed)
        {
            return !isShiftPressed
                && HasSelectedChampions
                && ReferenceEquals(_activeTargetRow, champion.Row)
                && _activeTargetIsPick == champion.IsPick;
        }

        private static bool TryGetSelectionRange(
            ChampionSelectionItem anchor,
            ChampionSelectionItem champion,
            [NotNullWhen(true)] out List<ChampionSelectionItem>? range)
        {
            range = null;
            if (!ReferenceEquals(anchor.Row, champion.Row) || anchor.IsPick != champion.IsPick)
                return false;

            var collection = GetChampionCollection(champion.Row, champion.IsPick);
            int anchorIndex = collection.IndexOf(anchor);
            int championIndex = collection.IndexOf(champion);
            if (anchorIndex < 0 || championIndex < 0)
                return false;

            int startIndex = Math.Min(anchorIndex, championIndex);
            int endIndex = Math.Max(anchorIndex, championIndex);
            range = collection
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .ToList();

            return true;
        }

        private void ClearChampionSelection(bool resetAnchor = true)
        {
            foreach (var row in _rows)
            {
                foreach (var champion in row.PickChampions)
                    champion.IsSelected = false;

                foreach (var champion in row.BanChampions)
                    champion.IsSelected = false;
            }

            if (resetAnchor)
                _selectionAnchorChampion = null;

            RefreshSelectedChampionState();
        }

        private bool DeleteSelectedChampions()
        {
            if (!IsPriorityEditingEnabled)
                return false;

            bool removedAny = false;

            foreach (var row in _rows)
            {
                removedAny |= DeleteSelectedChampions(row, isPick: true);
                removedAny |= DeleteSelectedChampions(row, isPick: false);
            }

            if (!removedAny)
            {
                RefreshSelectedChampionState();
                return false;
            }

            _selectionAnchorChampion = null;
            SaveChampionPreferences();
            RefreshSelectedChampionState();
            return true;
        }

        private bool TryDeleteFocusedOrSelectedChampion(ChampionSelectionItem? fallbackChampion = null)
        {
            if (!IsPriorityEditingEnabled)
                return false;

            TryGetFocusedChampionTarget(out var focusedRow, out bool focusedIsPick, out var focusedChampion);

            if (DeleteSelectedChampions())
            {
                if (focusedRow is not null)
                    SetActiveTarget(focusedRow, focusedIsPick, focusSearch: false);

                return true;
            }

            ChampionSelectionItem? championToDelete = focusedChampion ?? fallbackChampion;
            if (championToDelete is not null)
                return DeleteChampion(championToDelete);

            if (focusedRow is not null)
                return DeleteLastChampionFromTarget(focusedRow, focusedIsPick);

            return false;
        }

        private bool TryGetFocusedChampionTarget(
            [NotNullWhen(true)] out PositionRow? row,
            out bool isPick,
            out ChampionSelectionItem? champion)
        {
            row = null;
            isPick = false;
            champion = null;

            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
                return false;

            if (TryFindChampionItemTarget(focusedElement, out _, out var focusedChampion))
            {
                row = focusedChampion.Row;
                isPick = focusedChampion.IsPick;
                champion = focusedChampion;
                return true;
            }

            if (TryFindChampionListTarget(focusedElement, out _, out var focusedRow, out bool focusedIsPick))
            {
                row = focusedRow;
                isPick = focusedIsPick;
                return true;
            }

            return false;
        }

        private bool DeleteLastChampionFromTarget(PositionRow row, bool isPick)
        {
            var collection = GetChampionCollection(row, isPick);
            if (collection.Count == 0)
                return false;

            return DeleteChampion(collection[collection.Count - 1]);
        }

        private bool DeleteChampion(ChampionSelectionItem champion)
        {
            if (!IsPriorityEditingEnabled)
                return false;

            var collection = GetChampionCollection(champion.Row, champion.IsPick);
            if (!collection.Remove(champion))
                return false;

            if (ReferenceEquals(_selectionAnchorChampion, champion))
                _selectionAnchorChampion = null;

            UpdateRowTextFromCollection(champion.Row, champion.IsPick);
            SaveChampionPreferences();
            RefreshSelectedChampionState();
            SetActiveTarget(champion.Row, champion.IsPick, focusSearch: false);
            return true;
        }

        private static bool DeleteSelectedChampions(PositionRow row, bool isPick)
        {
            var collection = GetChampionCollection(row, isPick);
            var selectedChampions = collection
                .Where(champion => champion.IsSelected)
                .ToList();

            if (selectedChampions.Count == 0)
                return false;

            foreach (var champion in selectedChampions)
                collection.Remove(champion);

            UpdateRowTextFromCollection(row, isPick);
            return true;
        }

        private bool DeleteDraggedChampion(ChampionDragData champion)
        {
            if (!IsPriorityEditingEnabled)
                return false;

            if (champion.SourceItem is not ChampionSelectionItem sourceItem)
                return false;

            var collection = GetChampionCollection(sourceItem.Row, sourceItem.IsPick);
            if (!collection.Remove(sourceItem))
                return false;

            if (ReferenceEquals(_selectionAnchorChampion, sourceItem))
                _selectionAnchorChampion = null;

            UpdateRowTextFromCollection(sourceItem.Row, sourceItem.IsPick);
            SaveChampionPreferences();
            RefreshSelectedChampionState();
            return true;
        }

        private static bool CanDeleteChampionFromSearchArea(ChampionDragData champion)
        {
            return champion.SourceItem is not null;
        }

        private bool IsPointerOverSearchDeleteArea(Point pagePosition)
        {
            return IsPointInsideElement(ChampionSearchCard, TranslatePoint(pagePosition, ChampionSearchCard));
        }

        private void RefreshSelectedChampionState()
        {
            int selectedChampionCount = _rows.Sum(row =>
                row.PickChampions.Count(champion => champion.IsSelected)
                + row.BanChampions.Count(champion => champion.IsSelected));

            HasSelectedChampions = selectedChampionCount > 0;
            SelectedChampionCountText = $"({Math.Min(selectedChampionCount, 99)})";
        }

        private void ChampionItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            ModifierKeys modifiers = Keyboard.Modifiers;
            bool isShiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool isSelectionModifierPressed = (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;

            _pendingSelectionChampion = champion;
            _pendingSelectionModifiers = modifiers;
            _pendingSelectionShouldToggle = ShouldTreatClickAsControlSelection(champion, isShiftPressed);
            _draggedChampion = isSelectionModifierPressed ? null : champion;
            _dragStartPoint = e.GetPosition(this);

            if (isSelectionModifierPressed)
                e.Handled = true;
        }

        private void ChampionItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (_isChampionDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed || _draggedChampion is null)
                return;

            Point currentPosition = e.GetPosition(this);
            if (!IsPastDragThreshold(_dragStartPoint, currentPosition))
                return;

            var champion = _draggedChampion;
            ClearPendingChampionSelection();
            StartChampionDrag((DependencyObject)sender, ChampionDragData.FromSelection(champion), currentPosition);

            e.Handled = true;
        }

        private bool TryCommitPendingChampionSelection(DependencyObject? source)
        {
            if (_pendingSelectionChampion is not ChampionSelectionItem pendingChampion)
                return false;

            bool isPendingChampionTarget = TryFindChampionItemTarget(source, out _, out var targetChampion)
                && ReferenceEquals(targetChampion, pendingChampion);

            if (!isPendingChampionTarget)
            {
                ClearPendingChampionSelection();
                return false;
            }

            UpdateChampionSelection(pendingChampion, _pendingSelectionModifiers, _pendingSelectionShouldToggle);
            SetActiveTarget(pendingChampion.Row, pendingChampion.IsPick, focusSearch: false);
            ClearPendingChampionSelection();
            return true;
        }

        private void ClearPendingChampionSelection()
        {
            _pendingSelectionChampion = null;
            _pendingSelectionModifiers = ModifierKeys.None;
            _pendingSelectionShouldToggle = false;
        }

        private void ChampionItem_DragOver(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!TryGetChampionDragData(e, out var champion) || (sender as FrameworkElement)?.DataContext is not ChampionSelectionItem targetChampion)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!CanDropChampion(champion, targetChampion.Row, targetChampion.IsPick))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var itemElement = sender as FrameworkElement;
            if (TryShowSameLaneSwapPreview(champion, targetChampion))
            {
                SetActiveTarget(targetChampion.Row, targetChampion.IsPick, focusSearch: false);
                if (!UpdateDragFeedback(e))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                e.Effects = GetDragDropEffect(champion);
                e.Handled = true;
                return;
            }

            int? targetIndex = ResolveDropIndexFromItem(itemElement, targetChampion);
            if (targetIndex is not int resolvedTargetIndex)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ShowInsertionIndicatorAtIndex(targetChampion.Row, targetChampion.IsPick, resolvedTargetIndex);
            ShowDuplicateDropWarning(champion, targetChampion.Row, targetChampion.IsPick);

            SetActiveTarget(targetChampion.Row, targetChampion.IsPick, focusSearch: false);
            if (!UpdateDragFeedback(e))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = GetDragDropEffect(champion);
            e.Handled = true;
        }

        private void ChampionItem_Drop(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (!TryGetChampionDragData(e, out var champion) || (sender as FrameworkElement)?.DataContext is not ChampionSelectionItem targetChampion)
                return;

            if (!CanDropChampion(champion, targetChampion.Row, targetChampion.IsPick))
                return;

            var itemElement = sender as FrameworkElement;
            if (DropChampionOnSwapTarget(champion, targetChampion))
            {
                ClearInsertionIndicator();
                e.Handled = true;
                return;
            }

            int? targetIndex = ResolveDropIndexFromHover(targetChampion.Row, targetChampion.IsPick)
                ?? ResolveDropIndexFromItem(itemElement, targetChampion);

            DropChampionOnTarget(champion, targetChampion.Row, targetChampion.IsPick, targetIndex);
            ClearInsertionIndicator();
            e.Handled = true;
        }

        private void ChampionList_DragOver(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!TryGetChampionDragData(e, out var champion) || sender is not FrameworkElement listElement || listElement.DataContext is not PositionRow row)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            bool isPick = string.Equals(listElement.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            if (!CanDropChampion(champion, row, isPick))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            int targetIndex = ResolveAppendDropIndex(row, isPick);
            ShowInsertionIndicatorAtIndex(row, isPick, targetIndex);

            ShowDuplicateDropWarning(champion, row, isPick);

            SetActiveTarget(row, isPick, focusSearch: false);
            if (!UpdateDragFeedback(e))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = GetDragDropEffect(champion);
            e.Handled = true;
        }

        private void ChampionList_Drop(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (!TryGetChampionDragData(e, out var champion) || sender is not FrameworkElement listElement || listElement.DataContext is not PositionRow row)
                return;

            bool isPick = string.Equals(listElement.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            if (!CanDropChampion(champion, row, isPick))
                return;

            int resolvedTargetIndex = ResolveDropIndexFromHover(row, isPick)
                ?? ResolveAppendDropIndex(row, isPick);

            DropChampionOnTarget(champion, row, isPick, resolvedTargetIndex);
            ClearInsertionIndicator();
            e.Handled = true;
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!TryGetChampionDragData(e, out _))
                return;

            IsSearchDeleteDropTarget = false;
            ClearInsertionIndicator();
            if (!UpdateDragFeedback(e))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (!TryGetChampionDragData(e, out _))
                return;

            Point position = e.GetPosition(this);
            if (!IsPointInsideElement(this, position))
            {
                IsSearchDeleteDropTarget = false;
                ClearInsertionIndicator();
                HideDragPreview();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void SearchArea_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!TryGetChampionDragData(e, out var champion))
                return;

            ClearInsertionIndicator();
            IsSearchDeleteDropTarget = CanDeleteChampionFromSearchArea(champion);
            if (!UpdateDragFeedback(e))
            {
                IsSearchDeleteDropTarget = false;
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = IsSearchDeleteDropTarget ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void SearchArea_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (!TryGetChampionDragData(e, out var champion))
                return;

            if (CanDeleteChampionFromSearchArea(champion))
                DeleteDraggedChampion(champion);

            IsSearchDeleteDropTarget = false;
            ClearInsertionIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Page_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (!_isChampionDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishChampionDrag(drop: false);
                e.Handled = true;
                return;
            }

            UpdateManualChampionDrag(e.GetPosition(this));
            e.Handled = true;
        }

        private void Page_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isChampionDragActive)
            {
                FinishChampionDrag(drop: true, e.GetPosition(this));
                e.Handled = true;
                return;
            }

            if (TryCommitPendingChampionSelection(e.OriginalSource as DependencyObject))
                e.Handled = true;

            ClearDragState();
            HideDragPreview();
        }

        private bool IsSearchBoxFocused()
        {
            return ReferenceEquals(Keyboard.FocusedElement, ChampionSearchBox);
        }

        private bool IsFocusWithinChampionPlan()
        {
            return Keyboard.FocusedElement is DependencyObject focusedElement
                && (TryFindChampionItemTarget(focusedElement, out _, out _)
                    || TryFindChampionListTarget(focusedElement, out _, out _, out _));
        }

        private void FocusSearchBox()
        {
            ChampionSearchBox.Focus();
            ChampionSearchBox.CaretIndex = ChampionSearchBox.Text.Length;
        }

        private void InsertSearchText(string text)
        {
            int selectionStart = ChampionSearchBox.SelectionStart;
            int selectionLength = ChampionSearchBox.SelectionLength;
            string existingText = ChampionSearchBox.Text;

            ChampionSearchBox.Text = existingText.Remove(selectionStart, selectionLength).Insert(selectionStart, text);
            ChampionSearchBox.CaretIndex = selectionStart + text.Length;
        }

        private void RemoveSearchText()
        {
            if (ChampionSearchBox.SelectionLength > 0)
            {
                int selectionStart = ChampionSearchBox.SelectionStart;
                ChampionSearchBox.Text = ChampionSearchBox.Text.Remove(selectionStart, ChampionSearchBox.SelectionLength);
                ChampionSearchBox.CaretIndex = selectionStart;
                return;
            }

            int caretIndex = ChampionSearchBox.CaretIndex;
            if (caretIndex == 0)
                return;

            ChampionSearchBox.Text = ChampionSearchBox.Text.Remove(caretIndex - 1, 1);
            ChampionSearchBox.CaretIndex = caretIndex - 1;
        }

        private void InsertChampion(PositionRow row, bool isPick, ChampionInfo champion, int? targetIndex)
        {
            if (!IsPriorityEditingEnabled)
                return;

            var collection = GetChampionCollection(row, isPick);
            int existingIndex = collection.ToList().FindIndex(item => item.ChampionId == champion.Id);
            if (existingIndex >= 0)
            {
                int destinationIndex = Math.Clamp(targetIndex ?? collection.Count, 0, collection.Count);
                if (existingIndex < destinationIndex)
                    destinationIndex--;

                if (existingIndex != destinationIndex)
                    collection.Move(existingIndex, destinationIndex);
            }
            else
            {
                var item = CreateSelectionItem(row, champion.Id, isPick);
                if (targetIndex is int index)
                    collection.Insert(Math.Clamp(index, 0, collection.Count), item);
                else
                    collection.Add(item);
            }

            UpdateRowTextFromCollection(row, isPick);
            SaveChampionPreferences();
            SetActiveTarget(row, isPick);
        }

        private void MoveChampionToTarget(ChampionSelectionItem champion, PositionRow targetRow, bool targetIsPick, int? targetIndex)
        {
            if (!IsPriorityEditingEnabled)
                return;

            var sourceCollection = GetChampionCollection(champion.Row, champion.IsPick);
            var targetCollection = GetChampionCollection(targetRow, targetIsPick);
            int sourceIndex = sourceCollection.IndexOf(champion);
            if (sourceIndex < 0)
                return;

            bool wasSelected = champion.IsSelected;
            bool wasSelectionAnchor = ReferenceEquals(_selectionAnchorChampion, champion);
            bool sameCollection = ReferenceEquals(sourceCollection, targetCollection);
            int destinationIndex = targetIndex ?? targetCollection.Count;
            if (sameCollection)
            {
                destinationIndex = Math.Clamp(destinationIndex, 0, targetCollection.Count);
                if (sourceIndex < destinationIndex)
                    destinationIndex--;

                if (destinationIndex == sourceIndex)
                    return;

                targetCollection.Move(sourceIndex, destinationIndex);
                UpdateRowTextFromCollection(targetRow, targetIsPick);
            }
            else
            {
                int existingTargetIndex = targetCollection.ToList().FindIndex(item => item.ChampionId == champion.ChampionId);
                if (existingTargetIndex >= 0)
                {
                    targetCollection.RemoveAt(existingTargetIndex);
                    if (existingTargetIndex < destinationIndex)
                        destinationIndex--;
                }

                sourceCollection.RemoveAt(sourceIndex);
                UpdateRowTextFromCollection(champion.Row, champion.IsPick);

                destinationIndex = Math.Clamp(destinationIndex, 0, targetCollection.Count);
                var movedItem = CreateSelectionItem(targetRow, champion.ChampionId, targetIsPick);
                movedItem.IsSelected = wasSelected;
                targetCollection.Insert(destinationIndex, movedItem);
                if (wasSelectionAnchor)
                    _selectionAnchorChampion = movedItem;

                UpdateRowTextFromCollection(targetRow, targetIsPick);
            }

            SaveChampionPreferences();
            RefreshSelectedChampionState();
            SetActiveTarget(targetRow, targetIsPick);
        }

        private void DropChampionOnTarget(ChampionDragData champion, PositionRow targetRow, bool targetIsPick, int? targetIndex)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (champion.SourceItem is not null)
                MoveChampionToTarget(champion.SourceItem, targetRow, targetIsPick, targetIndex);
            else
                InsertChampion(targetRow, targetIsPick, champion.ToChampionInfo(), targetIndex);
        }

        private bool DropChampionOnSwapTarget(ChampionDragData champion, ChampionSelectionItem targetChampion)
        {
            if (champion.SourceItem is not ChampionSelectionItem sourceChampion
                || !IsSamePriorityLaneDrag(champion, targetChampion.Row, targetChampion.IsPick))
            {
                return false;
            }

            if (ReferenceEquals(sourceChampion, targetChampion))
                return true;

            SwapChampionsInLane(sourceChampion, targetChampion);
            return true;
        }

        private void SwapChampionsInLane(ChampionSelectionItem sourceChampion, ChampionSelectionItem targetChampion)
        {
            var collection = GetChampionCollection(sourceChampion.Row, sourceChampion.IsPick);
            int sourceIndex = collection.IndexOf(sourceChampion);
            int targetIndex = collection.IndexOf(targetChampion);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
                return;

            collection[sourceIndex] = targetChampion;
            collection[targetIndex] = sourceChampion;
            UpdateRowTextFromCollection(sourceChampion.Row, sourceChampion.IsPick);
            SaveChampionPreferences();
            RefreshSelectedChampionState();
            SetActiveTarget(sourceChampion.Row, sourceChampion.IsPick);
        }

        private void StartChampionDrag(DependencyObject source, ChampionDragData champion, Point position)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (_isChampionDragActive)
                FinishChampionDrag(drop: false);

            _isChampionDragActive = true;
            _activeChampionDragData = champion;
            if (champion.SourceItem is not null)
                champion.SourceItem.IsDragging = true;

            RefreshTargetBrushes();
            ShowDragPreview(champion, position);

            if (source is UIElement sourceElement && sourceElement.CaptureMouse())
            {
                _dragCaptureElement = sourceElement;
                _dragCaptureElement.LostMouseCapture += DragCaptureElement_LostMouseCapture;
            }

            UpdateManualChampionDrag(position);
        }

        private ChampionSelectionItem CreateSelectionItem(PositionRow row, int championId, bool isPick)
        {
            return new ChampionSelectionItem
            {
                ChampionId = championId,
                DisplayText = ChampionCatalog.FormatWithName(championId),
                PortraitImageSource = ChampionTileCatalog.GetSelectedImageSource(championId, _settings),
                Row = row,
                IsPick = isPick
            };
        }

        private bool TryGetChampionDragData(DragEventArgs e, [NotNullWhen(true)] out ChampionDragData? champion)
        {
            champion = null;
            if (!_isChampionDragActive || _activeChampionDragData is null)
                return false;

            champion = _activeChampionDragData;
            return true;
        }

        private void FinishChampionDrag(bool drop, Point? pagePosition = null)
        {
            try
            {
                if (drop && _activeChampionDragData is not null)
                {
                    if (pagePosition is Point position)
                        UpdateManualChampionDrag(position);

                    if (IsSearchDeleteDropTarget && CanDeleteChampionFromSearchArea(_activeChampionDragData))
                    {
                        DeleteDraggedChampion(_activeChampionDragData);
                    }
                    else if (_swapDropChampion is not null)
                    {
                        DropChampionOnSwapTarget(_activeChampionDragData, _swapDropChampion);
                    }
                    else if (_dragHoverRow is not null && _dragHoverTargetIndex is int targetIndex)
                    {
                        DropChampionOnTarget(
                            _activeChampionDragData,
                            _dragHoverRow,
                            _dragHoverIsPick,
                            targetIndex);
                    }
                }
            }
            finally
            {
                ReleaseDragCapture();
                HideDragPreview();
                ClearDragState();
            }
        }

        private void DragCaptureElement_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isChampionDragActive)
                FinishChampionDrag(drop: false);
        }

        private void ReleaseDragCapture()
        {
            if (_dragCaptureElement is null)
                return;

            var capturedElement = _dragCaptureElement;
            _dragCaptureElement = null;
            capturedElement.LostMouseCapture -= DragCaptureElement_LostMouseCapture;

            if (ReferenceEquals(Mouse.Captured, capturedElement))
                capturedElement.ReleaseMouseCapture();
        }

        private void UpdateManualChampionDrag(Point pagePosition)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (_activeChampionDragData is not ChampionDragData champion)
                return;

            if (!UpdateDragFeedback(pagePosition))
            {
                IsSearchDeleteDropTarget = false;
                return;
            }

            if (CanDeleteChampionFromSearchArea(champion) && IsPointerOverSearchDeleteArea(pagePosition))
            {
                ClearInsertionIndicator();
                IsSearchDeleteDropTarget = true;
                return;
            }

            IsSearchDeleteDropTarget = false;
            DependencyObject? hitElement = InputHitTest(pagePosition) as DependencyObject;
            if (TryFindChampionItemTarget(hitElement, out var itemElement, out var targetChampion))
            {
                if (!CanDropChampion(champion, targetChampion.Row, targetChampion.IsPick))
                {
                    ClearInsertionIndicator();
                    return;
                }

                if (TryShowSameLaneSwapPreview(champion, targetChampion))
                {
                    SetActiveTarget(targetChampion.Row, targetChampion.IsPick, focusSearch: false);
                    return;
                }

                int? targetIndex = ResolveDropIndexFromItem(itemElement, targetChampion);
                if (targetIndex is not int resolvedTargetIndex)
                {
                    ClearInsertionIndicator();
                    return;
                }

                ShowInsertionIndicatorAtIndex(targetChampion.Row, targetChampion.IsPick, resolvedTargetIndex);
                ShowDuplicateDropWarning(champion, targetChampion.Row, targetChampion.IsPick);
                SetActiveTarget(targetChampion.Row, targetChampion.IsPick, focusSearch: false);
                return;
            }

            if (TryFindAppendDropTarget(pagePosition, out var appendRow, out bool appendIsPick, out int appendTargetIndex))
            {
                if (!CanDropChampion(champion, appendRow, appendIsPick))
                {
                    ClearInsertionIndicator();
                    return;
                }

                ShowInsertionIndicatorAtIndex(appendRow, appendIsPick, appendTargetIndex);
                ShowDuplicateDropWarning(champion, appendRow, appendIsPick);
                SetActiveTarget(appendRow, appendIsPick, focusSearch: false);
                return;
            }

            if (TryFindChampionListTarget(hitElement, out var listElement, out var row, out bool isPick))
            {
                if (!CanDropChampion(champion, row, isPick))
                {
                    ClearInsertionIndicator();
                    return;
                }

                int targetIndex = ResolveAppendDropIndex(row, isPick);
                ShowInsertionIndicatorAtIndex(row, isPick, targetIndex);
                ShowDuplicateDropWarning(champion, row, isPick);
                SetActiveTarget(row, isPick, focusSearch: false);
                return;
            }

            ClearInsertionIndicator();
        }

        private static bool CanDropChampion(ChampionDragData champion, PositionRow targetRow, bool targetIsPick)
        {
            return champion is not null && targetRow is not null;
        }

        private static DragDropEffects GetDragDropEffect(ChampionDragData champion)
        {
            return champion.SourceItem is null
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
        }

        private static bool IsPastDragThreshold(Point start, Point current)
        {
            return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        private bool UpdateDragFeedback(DragEventArgs e)
        {
            return UpdateDragFeedback(e.GetPosition(this));
        }

        private bool UpdateDragFeedback(Point pagePosition)
        {
            if (!IsPointInsideElement(this, pagePosition))
            {
                ClearInsertionIndicator();
                HideDragPreview();
                return false;
            }

            AutoScrollScrollableRegionWhileDragging(pagePosition);
            UpdateDragPreviewPosition(pagePosition);
            return true;
        }

        private void AutoScrollScrollableRegionWhileDragging(Point pagePosition)
        {
            if (TryAutoScrollViewerWhileDragging(PriorityListScrollViewer, pagePosition))
                return;

            TryAutoScrollViewerWhileDragging(ChampionReferenceScrollViewer, pagePosition);
        }

        private bool TryAutoScrollViewerWhileDragging(ScrollViewer scrollViewer, Point pagePosition)
        {
            if (!scrollViewer.IsVisible || scrollViewer.ScrollableHeight <= 0 || scrollViewer.ViewportHeight <= 0)
                return false;

            Point position = TranslatePoint(pagePosition, scrollViewer);
            if (!IsPointInsideElement(scrollViewer, position))
                return false;

            double edgeDistance = Math.Min(
                DragAutoScrollEdgeDistance,
                Math.Max(16, scrollViewer.ActualHeight / 4));

            if (position.Y < edgeDistance)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - DragAutoScrollStep));
                return true;
            }

            if (position.Y > scrollViewer.ActualHeight - edgeDistance)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + DragAutoScrollStep));
                return true;
            }

            return false;
        }

        private static bool IsPointInsideElement(FrameworkElement element, Point point)
        {
            return point.X >= 0
                && point.Y >= 0
                && point.X <= element.ActualWidth
                && point.Y <= element.ActualHeight;
        }

        private void ShowDragPreview(ChampionDragData champion, Point position)
        {
            DragPreviewImage.Source = champion.PreviewImageSource;
            DragPreviewImageFrame.Visibility = Visibility.Visible;
            DragPreviewText.Text = champion.PreviewText;
            UpdateDragPreviewPosition(position);
            DragPreviewPopup.IsOpen = true;
        }

        private void UpdateDragPreviewPosition(Point position)
        {
            DragPreviewPopup.HorizontalOffset = position.X + 14;
            DragPreviewPopup.VerticalOffset = position.Y + 14;
            if (!DragPreviewPopup.IsOpen)
                DragPreviewPopup.IsOpen = true;
        }

        private void HideDragPreview()
        {
            DragPreviewPopup.IsOpen = false;
            DragPreviewImage.Source = null;
            DragPreviewImageFrame.Visibility = Visibility.Collapsed;
        }

        private void ClearDragState()
        {
            ClearInsertionIndicator();
            IsSearchDeleteDropTarget = false;
            if (_activeChampionDragData?.SourceItem is not null)
                _activeChampionDragData.SourceItem.IsDragging = false;

            _isChampionDragActive = false;
            _activeChampionDragData = null;
            _draggedChampion = null;
            ClearPendingChampionSelection();
            _draggedReferenceChampion = null;
            RefreshTargetBrushes();
        }

        private static ObservableCollection<ChampionSelectionItem> GetChampionCollection(PositionRow row, bool isPick)
        {
            return isPick ? row.PickChampions : row.BanChampions;
        }

        private static bool IsSamePriorityLaneDrag(ChampionDragData champion, PositionRow targetRow, bool targetIsPick)
        {
            return champion.SourceItem is not null
                && ReferenceEquals(champion.SourceItem.Row, targetRow)
                && champion.SourceItem.IsPick == targetIsPick;
        }

        private bool TryShowSameLaneSwapPreview(ChampionDragData champion, ChampionSelectionItem targetChampion)
        {
            if (!IsSamePriorityLaneDrag(champion, targetChampion.Row, targetChampion.IsPick))
                return false;

            if (ReferenceEquals(champion.SourceItem, targetChampion))
            {
                ShowSameLaneNoOpPreview(targetChampion.Row, targetChampion.IsPick);
                return true;
            }

            ShowSwapPreview(targetChampion);
            return true;
        }

        private int? ResolveDropIndexFromHover(PositionRow row, bool isPick)
        {
            return ReferenceEquals(_dragHoverRow, row) && _dragHoverIsPick == isPick
                ? _dragHoverTargetIndex
                : null;
        }

        private static int ResolveAppendDropIndex(PositionRow row, bool isPick)
        {
            return GetChampionCollection(row, isPick).Count;
        }

        private bool TryFindAppendDropTarget(
            Point pagePosition,
            [NotNullWhen(true)] out PositionRow? row,
            out bool isPick,
            out int targetIndex)
        {
            foreach (var candidateRow in _rows)
            {
                if (TryResolveAppendDropIndex(candidateRow, isPick: true, pagePosition, out targetIndex))
                {
                    row = candidateRow;
                    isPick = true;
                    return true;
                }

                if (TryResolveAppendDropIndex(candidateRow, isPick: false, pagePosition, out targetIndex))
                {
                    row = candidateRow;
                    isPick = false;
                    return true;
                }
            }

            row = null;
            isPick = false;
            targetIndex = 0;
            return false;
        }

        private bool TryResolveAppendDropIndex(PositionRow row, bool isPick, Point pagePosition, out int targetIndex)
        {
            targetIndex = ResolveAppendDropIndex(row, isPick);
            var itemsControl = FindChampionItemsControl(row, isPick);
            return itemsControl is not null
                && IsPointerInsideAppendDropBounds(row, isPick, pagePosition, itemsControl, null);
        }

        private bool IsPointerInsideAppendDropBounds(
            PositionRow row,
            bool isPick,
            Point pagePosition,
            ItemsControl? itemsControl,
            FrameworkElement? fallbackElement)
        {
            int targetIndex = GetChampionCollection(row, isPick).Count;
            Rect appendBounds = ResolveDropPreviewBounds(row, isPick, targetIndex, itemsControl);
            Point pointerPosition = itemsControl is not null
                ? TranslatePoint(pagePosition, itemsControl)
                : fallbackElement is not null
                    ? TranslatePoint(pagePosition, fallbackElement)
                    : pagePosition;

            return appendBounds.Contains(pointerPosition);
        }

        private List<ChampionPillLayout> GetChampionPillLayouts(
            ItemsControl itemsControl,
            ObservableCollection<ChampionSelectionItem> collection)
        {
            var layouts = new List<ChampionPillLayout>(collection.Count);
            for (int i = 0; i < collection.Count; i++)
            {
                if (itemsControl.ItemContainerGenerator.ContainerFromItem(collection[i]) is not DependencyObject container)
                    continue;

                var pillElement = FindTaggedVisualChild(container, ChampionPillTag);
                if (pillElement is null || pillElement.ActualWidth <= 0 || pillElement.ActualHeight <= 0)
                    continue;

                Point topLeft = pillElement.TranslatePoint(new Point(0, 0), itemsControl);
                layouts.Add(new ChampionPillLayout(
                    i,
                    new Rect(topLeft, new Size(pillElement.ActualWidth, pillElement.ActualHeight))));
            }

            return layouts;
        }

        private static int? ResolveDropIndexFromItem(FrameworkElement? itemElement, ChampionSelectionItem targetChampion)
        {
            if (itemElement is null)
                return null;

            return ResolveDropIndexFromItemPosition(itemElement, targetChampion);
        }

        private static int? ResolveDropIndexFromItemPosition(FrameworkElement? itemElement, ChampionSelectionItem targetChampion)
        {
            if (itemElement is null)
                return null;

            var collection = GetChampionCollection(targetChampion.Row, targetChampion.IsPick);
            int targetIndex = collection.IndexOf(targetChampion);
            if (targetIndex < 0)
                return null;

            return targetIndex;
        }

        private static bool TryFindChampionItemTarget(
            DependencyObject? start,
            [NotNullWhen(true)] out FrameworkElement? itemElement,
            [NotNullWhen(true)] out ChampionSelectionItem? champion)
        {
            itemElement = null;
            champion = null;

            for (DependencyObject? current = start; current is not null; current = GetParent(current))
            {
                if (current is FrameworkElement element
                    && element.AllowDrop
                    && element.DataContext is ChampionSelectionItem item)
                {
                    itemElement = element;
                    champion = item;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindChampionListTarget(
            DependencyObject? start,
            [NotNullWhen(true)] out FrameworkElement? listElement,
            [NotNullWhen(true)] out PositionRow? row,
            out bool isPick)
        {
            listElement = null;
            row = null;
            isPick = false;

            for (DependencyObject? current = start; current is not null; current = GetParent(current))
            {
                if (current is not FrameworkElement element
                    || !element.AllowDrop
                    || element.DataContext is not PositionRow positionRow
                    || element.Tag is not string tag)
                {
                    continue;
                }

                if (!string.Equals(tag, "Pick", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tag, "Ban", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                listElement = element;
                row = positionRow;
                isPick = string.Equals(tag, "Pick", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            return false;
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            if (element is FrameworkElement frameworkElement && frameworkElement.Parent is DependencyObject logicalParent)
                return logicalParent;

            if (element is FrameworkContentElement contentElement && contentElement.Parent is DependencyObject contentParent)
                return contentParent;

            return VisualTreeHelper.GetParent(element);
        }

        private void ShowInsertionIndicatorAtIndex(PositionRow row, bool isPick, int targetIndex)
        {
            var collection = GetChampionCollection(row, isPick);
            int index = Math.Clamp(targetIndex, 0, collection.Count);

            if (collection.Count == 0)
            {
                ShowInsertionIndicator(row, isPick, null, insertAfter: true, 0);
                return;
            }

            if (index == 0)
            {
                ShowInsertionIndicator(row, isPick, collection[0], insertAfter: false, 0);
                return;
            }

            if (index >= collection.Count)
            {
                ShowInsertionIndicator(row, isPick, null, insertAfter: true, collection.Count);
                return;
            }

            ShowInsertionIndicator(row, isPick, collection[index], insertAfter: false, index);
        }

        private void ShowDuplicateDropWarning(ChampionDragData champion, PositionRow row, bool isPick)
        {
            var duplicate = GetChampionCollection(row, isPick)
                .FirstOrDefault(item => item.ChampionId == champion.ChampionId && !ReferenceEquals(item, champion.SourceItem));

            if (ReferenceEquals(_duplicateDropChampion, duplicate))
                return;

            if (_duplicateDropChampion is not null)
                _duplicateDropChampion.IsDuplicateDropTarget = false;

            _duplicateDropChampion = duplicate;
            if (_duplicateDropChampion is not null)
                _duplicateDropChampion.IsDuplicateDropTarget = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;

                T? descendant = FindVisualChild<T>(child);
                if (descendant is not null)
                    return descendant;
            }

            return null;
        }

        private static FrameworkElement? FindTaggedVisualChild(DependencyObject parent, string tag)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && string.Equals(element.Tag as string, tag, StringComparison.Ordinal))
                    return element;

                FrameworkElement? descendant = FindTaggedVisualChild(child, tag);
                if (descendant is not null)
                    return descendant;
            }

            return null;
        }

        private void ShowSameLaneNoOpPreview(PositionRow row, bool isPick)
        {
            if (_swapDropChampion is null
                && _dragHoverChampion is null
                && ReferenceEquals(_dragHoverRow, row)
                && _dragHoverIsPick == isPick
                && _dragHoverTargetIndex is null)
            {
                RefreshTargetBrushes();
                return;
            }

            ClearInsertionIndicator();
            _dragHoverRow = row;
            _dragHoverIsPick = isPick;
            _dragHoverInsertAfter = false;
            _dragHoverTargetIndex = null;
            RefreshTargetBrushes();
        }

        private void ShowSwapPreview(ChampionSelectionItem targetChampion)
        {
            var collection = GetChampionCollection(targetChampion.Row, targetChampion.IsPick);
            int targetIndex = collection.IndexOf(targetChampion);
            if (targetIndex < 0)
            {
                ClearInsertionIndicator();
                return;
            }

            if (ReferenceEquals(_swapDropChampion, targetChampion)
                && ReferenceEquals(_dragHoverRow, targetChampion.Row)
                && _dragHoverIsPick == targetChampion.IsPick
                && _dragHoverTargetIndex == targetIndex)
            {
                RefreshTargetBrushes();
                return;
            }

            ClearInsertionIndicator();
            _swapDropChampion = targetChampion;
            _dragHoverRow = targetChampion.Row;
            _dragHoverIsPick = targetChampion.IsPick;
            _dragHoverInsertAfter = false;
            _dragHoverTargetIndex = targetIndex;
            ShowDropPreview(targetChampion.Row, targetChampion.IsPick, targetIndex, DropActionKind.Swap);
            RefreshTargetBrushes();
        }

        private void ShowInsertionIndicator(PositionRow row, bool isPick, ChampionSelectionItem? champion, bool insertAfter, int targetIndex)
        {
            int resolvedTargetIndex = targetIndex;

            if (ReferenceEquals(_dragHoverChampion, champion)
                && ReferenceEquals(_dragHoverRow, row)
                && _dragHoverIsPick == isPick
                && _dragHoverInsertAfter == insertAfter
                && _dragHoverTargetIndex == resolvedTargetIndex)
            {
                RefreshTargetBrushes();
                return;
            }

            ClearInsertionIndicator();

            _dragHoverChampion = champion;
            _dragHoverRow = row;
            _dragHoverIsPick = isPick;
            _dragHoverInsertAfter = insertAfter;
            _dragHoverTargetIndex = resolvedTargetIndex;

            ShowDropPreview(row, isPick, resolvedTargetIndex, ResolveDropPreviewEffect(row, isPick, resolvedTargetIndex));
            RefreshTargetBrushes();
        }

        private void ShowDropPreview(PositionRow row, bool isPick, int targetIndex, DropActionKind effect)
        {
            Rect bounds = ResolveDropPreviewBounds(row, isPick, targetIndex, null);
            double minHeight = bounds.Top + bounds.Height;

            if (isPick)
            {
                row.PickEndGhostEffect = effect;
                row.PickEndGhostLeft = bounds.Left;
                row.PickEndGhostTop = bounds.Top;
                row.PickEndGhostMinHeight = minHeight;
                row.PickEndGhostVisibility = Visibility.Visible;
            }
            else
            {
                row.BanEndGhostEffect = effect;
                row.BanEndGhostLeft = bounds.Left;
                row.BanEndGhostTop = bounds.Top;
                row.BanEndGhostMinHeight = minHeight;
                row.BanEndGhostVisibility = Visibility.Visible;
            }
        }

        private static DropActionKind ResolveDropPreviewEffect(PositionRow row, bool isPick, int targetIndex)
        {
            return targetIndex >= GetChampionCollection(row, isPick).Count
                ? DropActionKind.Append
                : DropActionKind.Insert;
        }

        private Rect ResolveDropPreviewBounds(PositionRow row, bool isPick, int targetIndex, ItemsControl? itemsControl)
        {
            int resolvedTargetIndex = Math.Clamp(targetIndex, 0, GetChampionCollection(row, isPick).Count);
            itemsControl ??= FindChampionItemsControl(row, isPick);
            if (itemsControl is null)
                return CreateFallbackDropPreviewBounds(null, resolvedTargetIndex);

            var collection = GetChampionCollection(row, isPick);
            var layouts = GetChampionPillLayouts(itemsControl, collection);
            var targetLayout = layouts.FirstOrDefault(layout => layout.Index == resolvedTargetIndex);
            if (targetLayout is not null)
                return targetLayout.Bounds;

            var lastLayout = layouts.FirstOrDefault(layout => layout.Index == collection.Count - 1);
            if (lastLayout is not null && resolvedTargetIndex == collection.Count)
            {
                double previewWidth = lastLayout.Bounds.Width > 0 ? lastLayout.Bounds.Width : PriorityChampionChipWidth;
                double previewHeight = lastLayout.Bounds.Height > 0 ? lastLayout.Bounds.Height : PriorityChampionChipHeight;
                double left = lastLayout.Bounds.Right;
                double top = lastLayout.Bounds.Top;

                if (itemsControl.ActualWidth > 0 && left + previewWidth > itemsControl.ActualWidth + 0.5)
                {
                    left = 0;
                    top = lastLayout.Bounds.Bottom;
                }

                return new Rect(left, top, previewWidth, previewHeight);
            }

            return CreateFallbackDropPreviewBounds(itemsControl, resolvedTargetIndex);
        }

        private ItemsControl? FindChampionItemsControl(PositionRow row, bool isPick)
        {
            if (PositionList.ItemContainerGenerator.ContainerFromItem(row) is not DependencyObject rowContainer)
                return null;

            var listElement = FindChampionListElement(rowContainer, row, isPick);
            return listElement is null ? null : FindVisualChild<ItemsControl>(listElement);
        }

        private static FrameworkElement? FindChampionListElement(DependencyObject parent, PositionRow row, bool isPick)
        {
            if (parent is FrameworkElement element
                && element.AllowDrop
                && ReferenceEquals(element.DataContext, row)
                && element.Tag is string tag
                && string.Equals(tag, isPick ? "Pick" : "Ban", StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                FrameworkElement? descendant = FindChampionListElement(VisualTreeHelper.GetChild(parent, i), row, isPick);
                if (descendant is not null)
                    return descendant;
            }

            return null;
        }

        private static Rect CreateFallbackDropPreviewBounds(ItemsControl? itemsControl, int targetIndex)
        {
            int resolvedTargetIndex = Math.Max(0, targetIndex);
            double availableWidth = itemsControl?.ActualWidth > 0
                ? itemsControl.ActualWidth
                : PriorityChampionChipWidth * DefaultPriorityChampionChipsPerRow;
            int chipsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / PriorityChampionChipWidth));
            int column = resolvedTargetIndex % chipsPerRow;
            int line = resolvedTargetIndex / chipsPerRow;

            return new Rect(
                column * PriorityChampionChipWidth,
                line * PriorityChampionChipHeight,
                PriorityChampionChipWidth,
                PriorityChampionChipHeight);
        }

        private void ClearInsertionIndicator()
        {
            if (_dragHoverChampion is not null)
            {
                _dragHoverChampion.IsSwapDropTarget = false;
            }

            if (_swapDropChampion is not null)
                _swapDropChampion.IsSwapDropTarget = false;

            if (_dragHoverRow is not null)
            {
                _dragHoverRow.PickEndGhostVisibility = Visibility.Collapsed;
                _dragHoverRow.BanEndGhostVisibility = Visibility.Collapsed;
                _dragHoverRow.PickEndGhostMinHeight = 0;
                _dragHoverRow.BanEndGhostMinHeight = 0;
                _dragHoverRow.PickEndGhostEffect = DropActionKind.None;
                _dragHoverRow.BanEndGhostEffect = DropActionKind.None;
            }

            if (_duplicateDropChampion is not null)
                _duplicateDropChampion.IsDuplicateDropTarget = false;

            _dragHoverChampion = null;
            _swapDropChampion = null;
            _duplicateDropChampion = null;
            _dragHoverRow = null;
            _dragHoverInsertAfter = false;
            _dragHoverTargetIndex = null;
            RefreshTargetBrushes();
        }

        private void RefreshTargetBrushes()
        {
            foreach (var row in _rows)
            {
                bool pickDropHover = IsPriorityEditingEnabled && _isChampionDragActive && ReferenceEquals(_dragHoverRow, row) && _dragHoverIsPick;
                bool banDropHover = IsPriorityEditingEnabled && _isChampionDragActive && ReferenceEquals(_dragHoverRow, row) && !_dragHoverIsPick;
                bool pickActive = IsPriorityEditingEnabled && ReferenceEquals(_activeTargetRow, row) && _activeTargetIsPick;
                bool banActive = IsPriorityEditingEnabled && ReferenceEquals(_activeTargetRow, row) && !_activeTargetIsPick;

                row.PickBorderBrush = pickDropHover ? _dropHoverTargetBrush : pickActive ? _activeTargetBrush : _inactiveTargetBrush;
                row.BanBorderBrush = banDropHover ? _dropHoverTargetBrush : banActive ? _activeTargetBrush : _inactiveTargetBrush;
                row.PickBackgroundBrush = pickDropHover ? _dropHoverBackgroundBrush : pickActive ? _activeTargetBackgroundBrush : _inactiveTargetBackgroundBrush;
                row.BanBackgroundBrush = banDropHover ? _dropHoverBackgroundBrush : banActive ? _activeTargetBackgroundBrush : _inactiveTargetBackgroundBrush;
            }
        }

        private void RefreshTheme()
        {
            Dispatcher.Invoke(() =>
            {
                RefreshThemeBrushes();
                RefreshTargetBrushes();
            });
        }

        private void RefreshThemeBrushes()
        {
            _activeTargetBrush = ResourceBrush("TargetActiveBrush", Brushes.DodgerBlue);
            _inactiveTargetBrush = ResourceBrush("TargetInactiveBrush", Brushes.SlateGray);
            _dropHoverTargetBrush = ResourceBrush("TargetDropHoverBrush", Brushes.DeepSkyBlue);
            _activeTargetBackgroundBrush = ResourceBrush("TargetActiveBackgroundBrush", Brushes.Transparent);
            _inactiveTargetBackgroundBrush = ResourceBrush("TargetInactiveBackgroundBrush", Brushes.Transparent);
            _dropHoverBackgroundBrush = ResourceBrush("TargetDropHoverBackgroundBrush", Brushes.Transparent);
        }

        private void SaveChampionPreferences()
        {
            _settings.Preferences.Remove(Position.None);

            foreach (var row in _rows)
            {
                _settings.Preferences[row.Position] = new PositionPreference
                {
                    PickChampionIds = row.PickChampions.Select(champion => champion.ChampionId).ToList(),
                    BanChampionIds = row.BanChampions.Select(champion => champion.ChampionId).ToList()
                };
            }

            QueuePreferenceSave();
        }

        private void QueuePreferenceSave()
        {
            if (_isPreferenceSavePending)
                return;

            _isPreferenceSavePending = true;
            _pendingPreferenceSaveOperation = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(FlushPendingPreferenceSave));
        }

        private void FlushPendingPreferenceSave()
        {
            if (!_isPreferenceSavePending)
                return;

            _pendingPreferenceSaveOperation = null;
            _isPreferenceSavePending = false;
            _settings.Save();
        }

        private static string CreateChampionImageSelectionSignature(IReadOnlyDictionary<int, string> selections)
        {
            return string.Join(
                "|",
                selections
                    .Where(entry => entry.Key > 0 && !string.IsNullOrWhiteSpace(entry.Value))
                    .OrderBy(entry => entry.Key)
                    .Select(entry => $"{entry.Key}:{Path.GetFileName(entry.Value.Trim()).ToUpperInvariant()}"));
        }

        private static void UpdateRowTextFromCollection(PositionRow row, bool isPick)
        {
            string text = string.Join(", ", GetChampionCollection(row, isPick).Select(champion => champion.DisplayText));
            if (isPick)
                row.PickChampionIds = text;
            else
                row.BanChampionIds = text;
        }

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }

        private sealed class ChampionDragData
        {
            public int ChampionId { get; private init; }
            public string ChampionName { get; private init; } = "";
            public ImageSource? PreviewImageSource { get; private init; }
            public ChampionSelectionItem? SourceItem { get; private init; }
            public string PreviewText => ChampionName;

            public static ChampionDragData FromSelection(ChampionSelectionItem champion)
            {
                return new ChampionDragData
                {
                    ChampionId = champion.ChampionId,
                    ChampionName = champion.DisplayText,
                    PreviewImageSource = champion.PortraitImageSource,
                    SourceItem = champion
                };
            }

            public static ChampionDragData FromReference(ChampionInfo champion, ChampSelectSettings settings)
            {
                return new ChampionDragData
                {
                    ChampionId = champion.Id,
                    ChampionName = champion.Name,
                    PreviewImageSource = ChampionTileCatalog.GetSelectedOption(champion, settings)?.ImageSource
                };
            }

            public ChampionInfo ToChampionInfo()
            {
                return new ChampionInfo(ChampionId, ChampionName);
            }
        }

        private sealed class ChampionPillLayout
        {
            public ChampionPillLayout(int index, Rect bounds)
            {
                Index = index;
                Bounds = bounds;
            }

            public int Index { get; }
            public Rect Bounds { get; }
        }

    }

    internal sealed class ChampionReferenceItem
    {
        private readonly ChampionChipLabel _chipLabel;

        public ChampionReferenceItem(ChampionInfo champion, ChampSelectSettings settings)
        {
            Champion = champion;
            _chipLabel = ChampionChipLabelFormatter.Format(champion.Name);
            PortraitImageSource = ChampionTileCatalog.GetSelectedOption(champion, settings)?.ImageSource;
        }

        public ChampionInfo Champion { get; }
        public string Name => Champion.Name;
        public ImageSource? PortraitImageSource { get; }
        public string ChipDisplayText => _chipLabel.Text;
        public double ChipDisplayFontSize => _chipLabel.FontSize;
        public string ToolTipText => $"{_chipLabel.ToolTipName}\nClick to add to the selected list, or drag into a pick/ban list.";
    }

    internal sealed record ChampionChipLabel(string Text, double FontSize, string ToolTipName);

    internal static class ChampionChipLabelFormatter
    {
        public const double DefaultFontSize = 10;
        private const double SmallFontSize = 9.25;
        private const double MinimumFontSize = 8;
        private const string LabelBreakSeedResourceName = "JoinGameAfk.Assets.champion-chip-label-breaks.json";

        private static readonly Lazy<IReadOnlyDictionary<string, string>> ConfiguredBreaks = new(LoadConfiguredBreaks);

        public static ChampionChipLabel Format(string name)
        {
            string normalizedName = string.IsNullOrWhiteSpace(name) ? "Unknown Champion" : name.Trim();
            string text = ConfiguredBreaks.Value.TryGetValue(normalizedName, out string? configuredText)
                ? configuredText
                : normalizedName;

            return new ChampionChipLabel(text, GetFontSize(text), normalizedName);
        }

        private static IReadOnlyDictionary<string, string> LoadConfiguredBreaks()
        {
            EnsureConfiguredBreaksFileExists();

            string filePath = AppStorage.ChampionChipLabelBreaksFilePath;
            if (!File.Exists(filePath))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string json = File.ReadAllText(filePath);
                var configuredBreaks = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    json,
                    new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    });

                return configuredBreaks?
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    .ToDictionary(
                        entry => entry.Key.Trim(),
                        entry => NormalizeConfiguredBreak(entry.Value),
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void EnsureConfiguredBreaksFileExists()
        {
            if (File.Exists(AppStorage.ChampionChipLabelBreaksFilePath))
                return;

            try
            {
                AppStorage.EnsureDirectoryExists();
                string json = LoadSeedConfiguredBreaksJson();
                File.WriteAllText(AppStorage.ChampionChipLabelBreaksFilePath, CreateConfiguredBreaksFileContents(json));
            }
            catch
            {
            }
        }

        private static string LoadSeedConfiguredBreaksJson()
        {
            using Stream? stream = typeof(ChampionChipLabelFormatter).Assembly.GetManifestResourceStream(LabelBreakSeedResourceName);
            if (stream is null)
                return $"{{{Environment.NewLine}}}";

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string CreateConfiguredBreaksFileContents(string json)
        {
            string header = string.Join(Environment.NewLine,
            [
                "// JoinGameAfk champion chip label breaks.",
                "// Edit this file to control where long champion names wrap in the Champion Priorities UI.",
                "// Keys are exact champion names. Values are displayed labels; use \\n inside a string to force a line break.",
                ""
            ]);

            return $"{header}{json.Trim()}{Environment.NewLine}";
        }

        private static double GetFontSize(string text)
        {
            int longestLineLength = text
                .Split('\n')
                .Max(line => line.Length);

            if (longestLineLength <= 8)
                return DefaultFontSize;

            if (longestLineLength <= 10)
                return SmallFontSize;

            return MinimumFontSize;
        }

        private static string NormalizeConfiguredBreak(string text)
        {
            return text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
        }
    }

    public class RoleFilterOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RoleFilterOption(Position position, string displayText)
        {
            Position = position;
            DisplayText = displayText;
        }

        public Position Position { get; }
        public string DisplayText { get; }
        public string AutomationName => $"Filter {Position} champions";
        public string ToolTipText => $"Filter {Position} champions";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public class ChampionSelectionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isDuplicateDropTarget;
        private bool _isSwapDropTarget;
        private bool _isSelected;
        private bool _isDragging;
        private string _displayText = "";
        private string _chipDisplayText = "";
        private double _chipDisplayFontSize = ChampionChipLabelFormatter.DefaultFontSize;
        private string _toolTipText = "";
        private ImageSource? _portraitImageSource;

        public int ChampionId { get; init; }
        public PositionRow Row { get; init; } = null!;
        public bool IsPick { get; init; }

        public string DisplayText
        {
            get => _displayText;
            set
            {
                if (EqualityComparer<string>.Default.Equals(_displayText, value))
                    return;

                _displayText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
                ApplyChipLabel(value);
            }
        }

        public string ChipDisplayText
        {
            get => _chipDisplayText;
            private set => SetProperty(ref _chipDisplayText, value);
        }

        public double ChipDisplayFontSize
        {
            get => _chipDisplayFontSize;
            private set => SetProperty(ref _chipDisplayFontSize, value);
        }

        public string ToolTipText
        {
            get => _toolTipText;
            private set => SetProperty(ref _toolTipText, value);
        }

        public ImageSource? PortraitImageSource
        {
            get => _portraitImageSource;
            set => SetProperty(ref _portraitImageSource, value);
        }

        public bool IsDuplicateDropTarget
        {
            get => _isDuplicateDropTarget;
            set => SetProperty(ref _isDuplicateDropTarget, value);
        }

        public bool IsSwapDropTarget
        {
            get => _isSwapDropTarget;
            set => SetProperty(ref _isSwapDropTarget, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsDragging
        {
            get => _isDragging;
            set => SetProperty(ref _isDragging, value);
        }

        private void ApplyChipLabel(string displayText)
        {
            var chipLabel = ChampionChipLabelFormatter.Format(displayText);
            ChipDisplayText = chipLabel.Text;
            ChipDisplayFontSize = chipLabel.FontSize;
            ToolTipText = $"{chipLabel.ToolTipName}\nDrag to reorder. Press Enter or Space to select. Press Backspace or Delete to remove champions.";
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PositionRow : INotifyPropertyChanged
    {
        private string _pickChampionIds = "";
        private string _banChampionIds = "";
        private Brush _pickBorderBrush = Brushes.Transparent;
        private Brush _banBorderBrush = Brushes.Transparent;
        private Brush _pickBackgroundBrush = Brushes.Transparent;
        private Brush _banBackgroundBrush = Brushes.Transparent;
        private Visibility _pickEndGhostVisibility = Visibility.Collapsed;
        private Visibility _banEndGhostVisibility = Visibility.Collapsed;
        private double _pickEndGhostLeft;
        private double _pickEndGhostTop;
        private double _pickEndGhostMinHeight;
        private double _banEndGhostLeft;
        private double _banEndGhostTop;
        private double _banEndGhostMinHeight;
        private DropActionKind _pickEndGhostEffect = DropActionKind.None;
        private DropActionKind _banEndGhostEffect = DropActionKind.None;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PositionRow()
        {
            PickChampions.CollectionChanged += PickChampions_CollectionChanged;
            BanChampions.CollectionChanged += BanChampions_CollectionChanged;
        }

        public Position Position { get; set; }
        public string PositionName { get; set; } = "";
        public ObservableCollection<ChampionSelectionItem> PickChampions { get; } = [];
        public ObservableCollection<ChampionSelectionItem> BanChampions { get; } = [];

        public string PickChampionIds
        {
            get => _pickChampionIds;
            set => SetProperty(ref _pickChampionIds, value);
        }

        public string BanChampionIds
        {
            get => _banChampionIds;
            set => SetProperty(ref _banChampionIds, value);
        }

        public Brush PickBorderBrush
        {
            get => _pickBorderBrush;
            set => SetProperty(ref _pickBorderBrush, value);
        }

        public Brush BanBorderBrush
        {
            get => _banBorderBrush;
            set => SetProperty(ref _banBorderBrush, value);
        }

        public Brush PickBackgroundBrush
        {
            get => _pickBackgroundBrush;
            set => SetProperty(ref _pickBackgroundBrush, value);
        }

        public Brush BanBackgroundBrush
        {
            get => _banBackgroundBrush;
            set => SetProperty(ref _banBackgroundBrush, value);
        }

        public Visibility PickEndGhostVisibility
        {
            get => _pickEndGhostVisibility;
            set
            {
                if (EqualityComparer<Visibility>.Default.Equals(_pickEndGhostVisibility, value))
                    return;

                _pickEndGhostVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickEndGhostVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickPlaceholderVisibility)));
            }
        }

        public Visibility BanEndGhostVisibility
        {
            get => _banEndGhostVisibility;
            set
            {
                if (EqualityComparer<Visibility>.Default.Equals(_banEndGhostVisibility, value))
                    return;

                _banEndGhostVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanEndGhostVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanPlaceholderVisibility)));
            }
        }

        public double PickEndGhostLeft
        {
            get => _pickEndGhostLeft;
            set => SetProperty(ref _pickEndGhostLeft, value);
        }

        public double PickEndGhostTop
        {
            get => _pickEndGhostTop;
            set => SetProperty(ref _pickEndGhostTop, value);
        }

        public double PickEndGhostMinHeight
        {
            get => _pickEndGhostMinHeight;
            set => SetProperty(ref _pickEndGhostMinHeight, value);
        }

        public double BanEndGhostLeft
        {
            get => _banEndGhostLeft;
            set => SetProperty(ref _banEndGhostLeft, value);
        }

        public double BanEndGhostTop
        {
            get => _banEndGhostTop;
            set => SetProperty(ref _banEndGhostTop, value);
        }

        public double BanEndGhostMinHeight
        {
            get => _banEndGhostMinHeight;
            set => SetProperty(ref _banEndGhostMinHeight, value);
        }

        public DropActionKind PickEndGhostEffect
        {
            get => _pickEndGhostEffect;
            set => SetProperty(ref _pickEndGhostEffect, value);
        }

        public DropActionKind BanEndGhostEffect
        {
            get => _banEndGhostEffect;
            set => SetProperty(ref _banEndGhostEffect, value);
        }

        public Visibility PickPlaceholderVisibility => PickChampions.Count == 0 && PickEndGhostVisibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility BanPlaceholderVisibility => BanChampions.Count == 0 && BanEndGhostVisibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        private void PickChampions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickPlaceholderVisibility)));
        }

        private void BanChampions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanPlaceholderVisibility)));
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
