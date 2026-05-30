using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JoinGameAfk.Model;
using JoinGameAfk.Presentation.View.Controls;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private void ChampionReferenceButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionReferenceItem championReference)
                return;

            if (IsChampionPictureEditMode)
            {
                _draggedReferenceChampion = null;
                _suppressReferenceChampionClick = false;
                OpenChampionPicturePicker(championReference.Champion);
                e.Handled = true;
                return;
            }

            _suppressReferenceChampionClick = false;
            _draggedReferenceChampion = championReference.Champion;
            _dragStartPoint = e.GetPosition(this);
        }

        private void ChampionReferenceButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if (IsChampionPictureEditMode)
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
            StartChampionDrag((DependencyObject)sender, ChampionDragData.FromReference(champion), currentPosition);

            e.Handled = true;
        }

        private bool DeleteDraggedChampion(ChampionDragData champion)
        {
            if (!IsPriorityEditingEnabled)
                return false;

            if (!champion.HasSourceItems)
                return false;

            bool removedAny = false;
            HashSet<(PositionRow Row, bool IsPick)> affectedTargets = [];

            foreach (var sourceItem in champion.SourceItems.Distinct())
            {
                var collection = GetChampionCollection(sourceItem.Row, sourceItem.IsPick);
                if (!collection.Remove(sourceItem))
                    continue;

                affectedTargets.Add((sourceItem.Row, sourceItem.IsPick));
                removedAny = true;

                if (ReferenceEquals(_selectionAnchorChampion, sourceItem))
                    _selectionAnchorChampion = null;
            }

            if (!removedAny)
            {
                RefreshSelectedChampionState();
                return false;
            }

            foreach (var target in affectedTargets)
                UpdateRowTextFromCollection(target.Row, target.IsPick);

            SaveChampionPreferences();
            RefreshSelectedChampionState();
            return true;
        }

        private static bool CanDeleteChampionFromSearchArea(ChampionDragData champion)
        {
            return champion.HasSourceItems;
        }

        private bool IsPointerOverSearchDeleteArea(Point pagePosition)
        {
            return IsPointInsideElement(ChampionSearchCard, TranslatePoint(pagePosition, ChampionSearchCard));
        }

        private void ChampionItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPriorityEditingEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            if (IsChampionPictureEditMode)
            {
                ClearPendingChampionSelection();
                _draggedChampion = null;
                OpenChampionPicturePicker(champion);
                e.Handled = true;
                return;
            }

            if (e.ClickCount >= 2)
            {
                ClearPendingChampionSelection();
                _draggedChampion = null;
                SelectChampionScope(GetChampionCollection(champion.Row, champion.IsPick).ToList(), clearExistingSelection: false);
                SetActiveTarget(champion.Row, champion.IsPick, focusSearch: false);
                e.Handled = true;
                return;
            }

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
            StartChampionDrag((DependencyObject)sender, CreateChampionDragData(champion), currentPosition);

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
                ClearInsertionIndicator();
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

            ShowInsertionIndicatorAtIndex(targetChampion.Row, targetChampion.IsPick, resolvedTargetIndex, champion);
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
                ClearInsertionIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            int targetIndex = ResolveAppendDropIndex(row, isPick);
            ShowInsertionIndicatorAtIndex(row, isPick, targetIndex, champion);

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
            IsSearchDeleteDropHintVisible = _activeChampionDragData?.IsBatchDeleteOnly == true;
            ClearInsertionIndicator();
            if (!UpdateDragFeedback(e))
            {
                IsSearchDeleteDropHintVisible = false;
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
                IsSearchDeleteDropHintVisible = false;
                ClearInsertionIndicator();
                HideDragPreview(clearContent: false);
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
            IsSearchDeleteDropHintVisible = IsSearchDeleteDropTarget;
            if (!UpdateDragFeedback(e))
            {
                IsSearchDeleteDropTarget = false;
                IsSearchDeleteDropHintVisible = false;
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
            IsSearchDeleteDropHintVisible = false;
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

        private ChampionDragData CreateChampionDragData(ChampionSelectionItem champion)
        {
            var selectedChampions = GetSelectedChampions();
            return champion.IsSelected && selectedChampions.Count > 1
                ? ChampionDragData.FromBatchSelection(selectedChampions)
                : ChampionDragData.FromSelection(champion);
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

            if (champion.HasSourceItems && !champion.IsBatchDeleteOnly && HasSelectedChampions)
                ClearChampionSelection();

            _isChampionDragActive = true;
            _activeChampionDragData = champion;
            foreach (var sourceItem in champion.SourceItems)
                sourceItem.IsDragging = true;

            IsSearchDeleteDropHintVisible = champion.IsBatchDeleteOnly;
            RefreshTargetBrushes();
            ShowDragPreview(champion, position);

            if (source is UIElement sourceElement && sourceElement.CaptureMouse())
            {
                _dragCaptureElement = sourceElement;
                _dragCaptureElement.LostMouseCapture += DragCaptureElement_LostMouseCapture;
            }

            UpdateManualChampionDrag(position);
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
            bool shouldClearBatchSelection = _activeChampionDragData?.IsBatchDeleteOnly == true;

            try
            {
                if (drop && _activeChampionDragData is not null)
                {
                    if (pagePosition is Point position)
                        UpdateManualChampionDrag(position);

                    if (IsSearchDeleteDropTarget && CanDeleteChampionFromSearchArea(_activeChampionDragData))
                    {
                        shouldClearBatchSelection = !DeleteDraggedChampion(_activeChampionDragData);
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
                if (shouldClearBatchSelection)
                    ClearChampionSelection();

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
                IsSearchDeleteDropHintVisible = false;
                return;
            }

            if (CanDeleteChampionFromSearchArea(champion) && IsPointerOverSearchDeleteArea(pagePosition))
            {
                ClearInsertionIndicator();
                IsSearchDeleteDropTarget = true;
                IsSearchDeleteDropHintVisible = true;
                return;
            }

            IsSearchDeleteDropTarget = false;
            IsSearchDeleteDropHintVisible = champion.IsBatchDeleteOnly;
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

                ShowInsertionIndicatorAtIndex(targetChampion.Row, targetChampion.IsPick, resolvedTargetIndex, champion);
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

                ShowInsertionIndicatorAtIndex(appendRow, appendIsPick, appendTargetIndex, champion);
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
                ShowInsertionIndicatorAtIndex(row, isPick, targetIndex, champion);
                ShowDuplicateDropWarning(champion, row, isPick);
                SetActiveTarget(row, isPick, focusSearch: false);
                return;
            }

            ClearInsertionIndicator();
        }

        private static bool CanDropChampion(ChampionDragData champion, PositionRow targetRow, bool targetIsPick)
        {
            return !champion.IsBatchDeleteOnly;
        }

        private static DragDropEffects GetDragDropEffect(ChampionDragData champion)
        {
            return champion.HasSourceItems ? DragDropEffects.Move : DragDropEffects.Copy;
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
                HideDragPreview(clearContent: false);
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
            DragPreviewImageFrame.Visibility = champion.IsBatchDeleteOnly ? Visibility.Collapsed : Visibility.Visible;
            DragPreviewText.Text = champion.PreviewText;
            UpdateDragPreviewPosition(position);
            DragPreviewOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateDragPreviewPosition(Point position)
        {
            Canvas.SetLeft(DragPreviewOverlay, position.X + 14);
            Canvas.SetTop(DragPreviewOverlay, position.Y + 14);
            if (DragPreviewOverlay.Visibility != Visibility.Visible)
                DragPreviewOverlay.Visibility = Visibility.Visible;
        }

        private void HideDragPreview(bool clearContent = true)
        {
            DragPreviewOverlay.Visibility = Visibility.Collapsed;
            if (!clearContent)
                return;

            DragPreviewImage.Source = null;
            DragPreviewImageFrame.Visibility = Visibility.Collapsed;
            DragPreviewText.Text = string.Empty;
        }

        private void ClearDragState()
        {
            ClearInsertionIndicator();
            IsSearchDeleteDropTarget = false;
            IsSearchDeleteDropHintVisible = false;
            if (_activeChampionDragData is not null)
            {
                foreach (var sourceItem in _activeChampionDragData.SourceItems)
                    sourceItem.IsDragging = false;
            }

            _isChampionDragActive = false;
            _activeChampionDragData = null;
            _draggedChampion = null;
            ClearPendingChampionSelection();
            _draggedReferenceChampion = null;
            RefreshTargetBrushes();
        }

        private static bool IsSamePriorityLaneDrag(ChampionDragData champion, PositionRow targetRow, bool targetIsPick)
        {
            return champion.SourceItem is not null
                && ReferenceEquals(champion.SourceItem.Row, targetRow)
                && champion.SourceItem.IsPick == targetIsPick;
        }

        private static bool TryResolveSameLaneSource(
            ChampionDragData? champion,
            PositionRow row,
            bool isPick,
            [NotNullWhen(true)] out ChampionSelectionItem? sourceItem,
            [NotNullWhen(true)] out ObservableCollection<ChampionSelectionItem>? collection,
            out int sourceIndex)
        {
            sourceItem = champion?.SourceItem;
            collection = null;
            sourceIndex = -1;

            if (sourceItem is null || !ReferenceEquals(sourceItem.Row, row) || sourceItem.IsPick != isPick)
                return false;

            collection = GetChampionCollection(row, isPick);
            sourceIndex = collection.IndexOf(sourceItem);
            return sourceIndex >= 0;
        }

        private static bool IsSameLaneAppendNoOp(ChampionDragData? champion, PositionRow row, bool isPick, int targetIndex)
        {
            return TryResolveSameLaneSource(champion, row, isPick, out _, out var collection, out int sourceIndex)
                && targetIndex >= collection.Count
                && sourceIndex == collection.Count - 1;
        }

        private static bool TryResolveSameLaneMoveToEnd(
            ChampionDragData? champion,
            PositionRow row,
            bool isPick,
            int targetIndex,
            [NotNullWhen(true)] out ChampionSelectionItem? sourceItem)
        {
            sourceItem = null;

            if (!TryResolveSameLaneSource(champion, row, isPick, out var resolvedSource, out var collection, out int sourceIndex))
                return false;

            if (targetIndex < collection.Count || sourceIndex >= collection.Count - 1)
                return false;

            sourceItem = resolvedSource;
            return true;
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

        private static bool IsDescendantOf(DependencyObject start, DependencyObject ancestor)
        {
            for (DependencyObject? current = start; current is not null; current = GetParent(current))
            {
                if (ReferenceEquals(current, ancestor))
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

        private void ShowInsertionIndicatorAtIndex(PositionRow row, bool isPick, int targetIndex, ChampionDragData? champion = null)
        {
            var collection = GetChampionCollection(row, isPick);
            int index = Math.Clamp(targetIndex, 0, collection.Count);

            if (IsSameLaneAppendNoOp(champion, row, isPick, index))
            {
                ShowSameLaneNoOpPreview(row, isPick);
                return;
            }

            if (collection.Count == 0)
            {
                ShowInsertionIndicator(row, isPick, null, insertAfter: true, 0, champion);
                return;
            }

            if (index == 0)
            {
                ShowInsertionIndicator(row, isPick, collection[0], insertAfter: false, 0, champion);
                return;
            }

            if (index >= collection.Count)
            {
                ShowInsertionIndicator(row, isPick, null, insertAfter: true, collection.Count, champion);
                return;
            }

            ShowInsertionIndicator(row, isPick, collection[index], insertAfter: false, index, champion);
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

        private void ShowInsertionIndicator(
            PositionRow row,
            bool isPick,
            ChampionSelectionItem? champion,
            bool insertAfter,
            int targetIndex,
            ChampionDragData? dragData = null)
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

            ShowDropPreview(row, isPick, resolvedTargetIndex, ResolveDropPreviewEffect(dragData, row, isPick, resolvedTargetIndex));
            ShowMoveOriginPreview(dragData, row, isPick, resolvedTargetIndex);
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

        private static DropActionKind ResolveDropPreviewEffect(ChampionDragData? champion, PositionRow row, bool isPick, int targetIndex)
        {
            if (TryResolveSameLaneMoveToEnd(champion, row, isPick, targetIndex, out _))
                return DropActionKind.MoveAppend;

            return targetIndex >= GetChampionCollection(row, isPick).Count
                ? DropActionKind.Append
                : DropActionKind.Insert;
        }

        private void ShowMoveOriginPreview(ChampionDragData? champion, PositionRow row, bool isPick, int targetIndex)
        {
            if (!TryResolveSameLaneMoveToEnd(champion, row, isPick, targetIndex, out var sourceItem))
                return;

            _moveOriginDropChampion = sourceItem;
            _moveOriginDropChampion.IsMoveOriginDropTarget = true;
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

            if (_moveOriginDropChampion is not null)
                _moveOriginDropChampion.IsMoveOriginDropTarget = false;

            _dragHoverChampion = null;
            _swapDropChampion = null;
            _duplicateDropChampion = null;
            _moveOriginDropChampion = null;
            _dragHoverRow = null;
            _dragHoverInsertAfter = false;
            _dragHoverTargetIndex = null;
            RefreshTargetBrushes();
        }

        private sealed class ChampionDragData
        {
            public int ChampionId { get; private init; }
            public string ChampionName { get; private init; } = "";
            public ImageSource? PreviewImageSource { get; private init; }
            public IReadOnlyList<ChampionSelectionItem> SourceItems { get; private init; } = [];
            public ChampionSelectionItem? SourceItem => SourceItems.Count == 1 ? SourceItems[0] : null;
            public bool HasSourceItems => SourceItems.Count > 0;
            public bool IsBatchDeleteOnly => SourceItems.Count > 1;
            public string PreviewText => IsBatchDeleteOnly ? $"Drag to trash: {SourceItems.Count} selected" : ChampionName;

            public static ChampionDragData FromSelection(ChampionSelectionItem champion)
            {
                return new ChampionDragData
                {
                    ChampionId = champion.ChampionId,
                    ChampionName = champion.DisplayText,
                    PreviewImageSource = champion.PortraitImageSource,
                    SourceItems = [champion]
                };
            }

            public static ChampionDragData FromBatchSelection(IReadOnlyList<ChampionSelectionItem> champions)
            {
                return new ChampionDragData
                {
                    ChampionName = "Selected champions",
                    SourceItems = champions
                };
            }

            public static ChampionDragData FromReference(ChampionInfo champion)
            {
                return new ChampionDragData
                {
                    ChampionId = champion.Id,
                    ChampionName = champion.Name,
                    PreviewImageSource = ChampionTileCatalog.GetSelectedOption(champion)?.ImageSource
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
}