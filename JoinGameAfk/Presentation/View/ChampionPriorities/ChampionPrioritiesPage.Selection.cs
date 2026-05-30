using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (DeleteSelectedChampions())
            {
                e.Handled = true;
            }
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && ChampionPicturePickerOverlay.Visibility == Visibility.Visible)
            {
                CloseChampionPicturePicker();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && IsChampionPictureEditMode)
            {
                SetChampionPictureEditMode(false);
                e.Handled = true;
                return;
            }

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
                && e.Key == Key.Escape
                && (HasSelectedChampions || _activeTargetRow is not null))
            {
                ClearChampionSelection();
                ClearActiveTarget();
                e.Handled = true;
                return;
            }

            if (!_isChampionDragActive
                && !IsSearchBoxFocused()
                && Keyboard.Modifiers == ModifierKeys.Control
                && e.Key == Key.A)
            {
                SelectCurrentSelectionScope();
                e.Handled = true;
                return;
            }

            if (!_isChampionDragActive
                && !IsSearchBoxFocused()
                && IsChampionDeleteKey(e.Key)
                && TryDeleteFocusedOrSelectedChampion())
            {
                e.Handled = true;
                return;
            }
        }

        private void ChampionTarget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not PositionRow row)
                return;

            bool isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            SetActiveTarget(row, isPick, focusSearch: false);

            bool isChampionClick = TryFindChampionItemTarget(e.OriginalSource as DependencyObject, out _, out _);
            if (e.ClickCount >= 2 && !isChampionClick)
            {
                SelectChampionScope(GetChampionCollection(row, isPick).ToList(), clearExistingSelection: false);
                e.Handled = true;
            }
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

        private bool TryAddChampionToActiveTarget(ChampionInfo champion)
        {
            if (!IsPriorityEditingEnabled || _activeTargetRow is null)
                return false;

            InsertChampion(_activeTargetRow, _activeTargetIsPick, champion, null);
            return true;
        }

        private bool AddFirstFilteredChampion()
        {
            if (_filteredChampions.Count == 0 || !TryAddChampionToActiveTarget(_filteredChampions[0]))
                return false;

            ChampionSearchBox.Clear();
            return true;
        }

        private void SetActiveTarget(PositionRow row, bool isPick, bool focusSearch = false)
        {
            _activeTargetRow = row;
            _activeTargetIsPick = isPick;

            RefreshTargetBrushes();
            RefreshSelectedChampionState();

            if (!focusSearch)
                return;

            ChampionSearchBox.Focus();
            ChampionSearchBox.SelectAll();
        }

        private void ClearActiveTarget()
        {
            _activeTargetRow = null;
            _activeTargetIsPick = false;
            RefreshTargetBrushes();
            RefreshSelectedChampionState();
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
            return !isShiftPressed;
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

        private void SelectCurrentSelectionScope()
        {
            SelectChampionScope(GetCurrentSelectionScopeChampions(), clearExistingSelection: false);
        }

        private void SelectChampionScope(IReadOnlyList<ChampionSelectionItem> champions, bool clearExistingSelection)
        {
            if (clearExistingSelection)
                ClearChampionSelection(resetAnchor: false);

            foreach (var champion in champions)
                champion.IsSelected = true;

            _selectionAnchorChampion = champions.FirstOrDefault();
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

        private void RefreshSelectedChampionState()
        {
            var champions = GetAllPriorityChampions();
            int selectedChampionCount = champions.Count(champion => champion.IsSelected);

            HasSelectedChampions = selectedChampionCount > 0;
            SelectedChampionCountText = $"({selectedChampionCount})";
        }

        private List<ChampionSelectionItem> GetCurrentSelectionScopeChampions()
        {
            return _activeTargetRow is null
                ? GetAllPriorityChampions()
                : GetChampionCollection(_activeTargetRow, _activeTargetIsPick).ToList();
        }

        private List<ChampionSelectionItem> GetAllPriorityChampions()
        {
            return _rows
                .SelectMany(row => row.PickChampions.Concat(row.BanChampions))
                .ToList();
        }

        private List<ChampionSelectionItem> GetSelectedChampions()
        {
            return GetAllPriorityChampions()
                .Where(champion => champion.IsSelected)
                .ToList();
        }

        private void Page_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled || _isChampionDragActive)
                return;

            var source = e.OriginalSource as DependencyObject;
            if (!IsSearchTextBoxClick(source))
                FocusPriorityPage();

            if (!IsNeutralSelectionClearClick(source))
                return;

            ClearChampionSelection();
            ClearActiveTarget();
            ClearPendingChampionSelection();
        }

        private void FocusPriorityPage()
        {
            Focus();
            Keyboard.Focus(this);
        }

        private bool IsSearchBoxFocused()
        {
            return ReferenceEquals(Keyboard.FocusedElement, ChampionSearchBox);
        }

        private bool IsSearchTextBoxClick(DependencyObject? source)
        {
            return source is not null && IsDescendantOf(source, ChampionSearchBox);
        }

        private bool IsNeutralSelectionClearClick(DependencyObject? source)
        {
            if (source is null)
                return false;

            if (!IsDescendantOf(source, ContentGrid))
                return false;

            if (IsDescendantOf(source, ChampionSearchCard))
                return false;

            if (TryFindChampionItemTarget(source, out _, out _)
                || TryFindChampionListTarget(source, out _, out _, out _))
            {
                return false;
            }

            return true;
        }

        private void FocusSearchBox()
        {
            ChampionSearchBox.Focus();
            ChampionSearchBox.CaretIndex = ChampionSearchBox.Text.Length;
        }

        private ChampionSelectionItem CreateSelectionItem(PositionRow row, int championId, bool isPick)
        {
            return new ChampionSelectionItem
            {
                ChampionId = championId,
                DisplayText = ChampionCatalog.FormatWithName(championId),
                PortraitImageSource = ChampionTileCatalog.GetSelectedImageSource(championId),
                Row = row,
                IsPick = isPick
            };
        }

        private static ObservableCollection<ChampionSelectionItem> GetChampionCollection(PositionRow row, bool isPick)
        {
            return isPick ? row.PickChampions : row.BanChampions;
        }
    }
}