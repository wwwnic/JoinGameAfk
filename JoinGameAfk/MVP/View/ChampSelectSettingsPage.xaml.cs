using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.View
{
    public partial class ChampSelectSettingsPage : Page
    {
        private const double DragAutoScrollEdgeDistance = 56;
        private const double DragAutoScrollStep = 28;
        private const string ChampionPillTag = "ChampionPill";

        private static readonly SolidColorBrush ActiveTargetBrush = new((Color)ColorConverter.ConvertFromString("#3B82F6"));
        private static readonly SolidColorBrush InactiveTargetBrush = new((Color)ColorConverter.ConvertFromString("#334155"));
        private static readonly SolidColorBrush DropHoverTargetBrush = new((Color)ColorConverter.ConvertFromString("#60A5FA"));
        private static readonly SolidColorBrush ActiveTargetBackgroundBrush = new((Color)ColorConverter.ConvertFromString("#0F1B2D"));
        private static readonly SolidColorBrush InactiveTargetBackgroundBrush = new((Color)ColorConverter.ConvertFromString("#111827"));
        private static readonly SolidColorBrush DropHoverBackgroundBrush = new((Color)ColorConverter.ConvertFromString("#102A43"));
        private readonly ChampSelectSettings _settings;
        private readonly List<ChampionInfo> _allChampions;
        private List<ChampionInfo> _filteredChampions;
        private readonly List<PositionRow> _rows;
        private PositionRow? _activeTargetRow;
        private bool _activeTargetIsPick;
        private Point _dragStartPoint;
        private ChampionSelectionItem? _draggedChampion;
        private ChampionInfo? _draggedReferenceChampion;
        private bool _suppressReferenceChampionClick;
        private bool _isChampionDragActive;
        private ChampionDragData? _activeChampionDragData;
        private UIElement? _dragCaptureElement;
        private ChampionSelectionItem? _dragHoverChampion;
        private ChampionSelectionItem? _duplicateDropChampion;
        private PositionRow? _dragHoverRow;
        private bool _dragHoverIsPick;
        private bool _dragHoverInsertAfter;
        private int? _dragHoverTargetIndex;

        public static readonly DependencyProperty IsDeleteModeEnabledProperty = DependencyProperty.Register(
            nameof(IsDeleteModeEnabled),
            typeof(bool),
            typeof(ChampSelectSettingsPage),
            new PropertyMetadata(false));

        public bool IsDeleteModeEnabled
        {
            get => (bool)GetValue(IsDeleteModeEnabledProperty);
            set => SetValue(IsDeleteModeEnabledProperty, value);
        }

        public ChampSelectSettingsPage(ChampSelectSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            _allChampions = ChampionCatalog.All
                .OrderBy(champion => champion.Name)
                .ToList();
            _filteredChampions = [.. _allChampions];
            ChampionReferenceList.ItemsSource = _filteredChampions;

            _rows = [];
            foreach (Position position in Enum.GetValues<Position>())
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
                row.PickBorderBrush = InactiveTargetBrush;
                row.BanBorderBrush = InactiveTargetBrush;
                row.PickBackgroundBrush = InactiveTargetBackgroundBrush;
                row.BanBackgroundBrush = InactiveTargetBackgroundBrush;
                _rows.Add(row);
            }

            PositionList.ItemsSource = _rows;
            if (_rows.Count > 0)
            {
                SetActiveTarget(_rows[0], isPick: true);
            }

            Loaded += (_, _) => UpdateChampionReferenceListHeight();
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateChampionReferenceListHeight();
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

            if (e.Key != Key.Enter || _filteredChampions.Count == 0)
                return;

            if (AddFirstFilteredChampion())
            {
                e.Handled = true;
            }
        }

        private void ChampionItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsDeleteModeEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            var collection = GetChampionCollection(champion.Row, champion.IsPick);
            if (collection.Remove(champion))
            {
                UpdateRowTextFromCollection(champion.Row, champion.IsPick);
                SaveChampionPreferences();
            }

            e.Handled = true;
        }

        private void Page_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_activeTargetRow is null || IsSearchBoxFocused() || string.IsNullOrEmpty(e.Text))
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

            if (e.Key == Key.Enter && AddFirstFilteredChampion())
            {
                e.Handled = true;
            }
        }

        private void ChampionReferenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressReferenceChampionClick)
            {
                _suppressReferenceChampionClick = false;
                e.Handled = true;
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is not ChampionInfo champion)
                return;

            TryAddChampionToActiveTarget(champion);
            _draggedReferenceChampion = null;
        }

        private void ChampionReferenceButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ChampionInfo champion)
                return;

            _suppressReferenceChampionClick = false;
            _draggedReferenceChampion = champion;
            _dragStartPoint = e.GetPosition(this);
        }

        private void ChampionReferenceButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
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

        private void ChampionTarget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not PositionRow row)
                return;

            bool isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            SetActiveTarget(row, isPick);
        }

        private void RemoveChampionButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            var collection = GetChampionCollection(champion.Row, champion.IsPick);
            if (collection.Remove(champion))
            {
                UpdateRowTextFromCollection(champion.Row, champion.IsPick);
                SaveChampionPreferences();
            }
        }

        private void UpdateChampionFilter()
        {
            string search = ChampionSearchBox.Text.Trim();
            _filteredChampions = string.IsNullOrWhiteSpace(search)
                ? [.. _allChampions]
                : [.. _allChampions
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

            ChampionReferenceList.ItemsSource = _filteredChampions;
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

        private void UpdateChampionReferenceListHeight()
        {
            if (!IsLoaded)
                return;

            double viewportHeight = PageScrollViewer.ViewportHeight > 0
                ? PageScrollViewer.ViewportHeight
                : ActualHeight;

            double availableHeight = viewportHeight
                - ChampionReferenceBorder.TranslatePoint(new Point(0, 0), PageScrollViewer).Y
                - ContentGrid.Margin.Bottom
                - 28;

            ChampionReferenceBorder.MaxHeight = Math.Max(140, availableHeight);
        }

        private bool TryAddChampionToActiveTarget(ChampionInfo champion)
        {
            if (_activeTargetRow is null)
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

        private void ChampionItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsDeleteModeEnabled)
            {
                _draggedChampion = null;
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            _draggedChampion = champion;
            _dragStartPoint = e.GetPosition(this);
        }

        private void ChampionItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isChampionDragActive)
                return;

            if (e.LeftButton != MouseButtonState.Pressed || _draggedChampion is null)
                return;

            Point currentPosition = e.GetPosition(this);
            if (!IsPastDragThreshold(_dragStartPoint, currentPosition))
                return;

            var champion = _draggedChampion;
            StartChampionDrag((DependencyObject)sender, ChampionDragData.FromSelection(champion), currentPosition);

            e.Handled = true;
        }

        private void ChampionItem_DragOver(object sender, DragEventArgs e)
        {
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

            int? targetIndex = ResolveDropIndexFromItem((FrameworkElement)sender, targetChampion, e);
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
            if (!TryGetChampionDragData(e, out var champion) || (sender as FrameworkElement)?.DataContext is not ChampionSelectionItem targetChampion)
                return;

            if (!CanDropChampion(champion, targetChampion.Row, targetChampion.IsPick))
                return;

            int? targetIndex = ResolveDropIndexFromHover(targetChampion.Row, targetChampion.IsPick)
                ?? ResolveDropIndexFromItem(sender as FrameworkElement, targetChampion, e);

            DropChampionOnTarget(champion, targetChampion.Row, targetChampion.IsPick, targetIndex);
            ClearInsertionIndicator();
            e.Handled = true;
        }

        private void ChampionList_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetChampionDragData(e, out var champion) || (sender as FrameworkElement)?.DataContext is not PositionRow row)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            bool isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            if (!CanDropChampion(champion, row, isPick))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var collection = GetChampionCollection(row, isPick);
            if (ResolveDropIndexFromHover(row, isPick) is null || !IsPointerOverItemsHost(sender as DependencyObject, e))
                ShowInsertionIndicatorAtIndex(row, isPick, collection.Count);

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
            if (!TryGetChampionDragData(e, out var champion) || (sender as FrameworkElement)?.DataContext is not PositionRow row)
                return;

            bool isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            if (!CanDropChampion(champion, row, isPick))
                return;

            int targetIndex = ResolveDropIndexFromHover(row, isPick) is int hoverIndex && IsPointerOverItemsHost(sender as DependencyObject, e)
                ? hoverIndex
                : GetChampionCollection(row, isPick).Count;

            DropChampionOnTarget(champion, row, isPick, targetIndex);
            ClearInsertionIndicator();
            e.Handled = true;
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetChampionDragData(e, out _))
                return;

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
            if (!TryGetChampionDragData(e, out _))
                return;

            Point position = e.GetPosition(this);
            if (!IsPointInsideElement(this, position))
            {
                ClearInsertionIndicator();
                HideDragPreview();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void SearchArea_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!TryGetChampionDragData(e, out _))
                return;

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

        private void SearchArea_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!TryGetChampionDragData(e, out _))
                return;

            ClearInsertionIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Page_PreviewMouseMove(object sender, MouseEventArgs e)
        {
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

            ClearDragState();
            HideDragPreview();
        }

        private bool IsSearchBoxFocused()
        {
            return ReferenceEquals(Keyboard.FocusedElement, ChampionSearchBox);
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
            var sourceCollection = GetChampionCollection(champion.Row, champion.IsPick);
            var targetCollection = GetChampionCollection(targetRow, targetIsPick);
            int sourceIndex = sourceCollection.IndexOf(champion);
            if (sourceIndex < 0)
                return;

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
                targetCollection.Insert(destinationIndex, movedItem);
                UpdateRowTextFromCollection(targetRow, targetIsPick);
            }

            SaveChampionPreferences();
            SetActiveTarget(targetRow, targetIsPick);
        }

        private void DropChampionOnTarget(ChampionDragData champion, PositionRow targetRow, bool targetIsPick, int? targetIndex)
        {
            if (champion.SourceItem is not null)
                MoveChampionToTarget(champion.SourceItem, targetRow, targetIsPick, targetIndex);
            else
                InsertChampion(targetRow, targetIsPick, champion.ToChampionInfo(), targetIndex);
        }

        private void StartChampionDrag(DependencyObject source, ChampionDragData champion, Point position)
        {
            if (_isChampionDragActive)
                FinishChampionDrag(drop: false);

            _isChampionDragActive = true;
            _activeChampionDragData = champion;
            RefreshTargetBrushes();
            ShowDragPreview(champion.PreviewText, position);

            if (source is UIElement sourceElement && sourceElement.CaptureMouse())
            {
                _dragCaptureElement = sourceElement;
                _dragCaptureElement.LostMouseCapture += DragCaptureElement_LostMouseCapture;
            }

            UpdateManualChampionDrag(position);
        }

        private static ChampionSelectionItem CreateSelectionItem(PositionRow row, int championId, bool isPick)
        {
            return new ChampionSelectionItem
            {
                ChampionId = championId,
                DisplayText = ChampionCatalog.FormatWithName(championId),
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

                    if (_dragHoverRow is not null && _dragHoverTargetIndex is int targetIndex)
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
            if (_activeChampionDragData is not ChampionDragData champion)
                return;

            if (!UpdateDragFeedback(pagePosition))
                return;

            DependencyObject? hitElement = InputHitTest(pagePosition) as DependencyObject;
            if (TryFindChampionItemTarget(hitElement, out var itemElement, out var targetChampion))
            {
                if (!CanDropChampion(champion, targetChampion.Row, targetChampion.IsPick))
                {
                    ClearInsertionIndicator();
                    return;
                }

                int? targetIndex = ResolveDropIndexFromItem(itemElement, targetChampion, pagePosition);
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

            if (TryFindChampionListTarget(hitElement, out var listElement, out var row, out bool isPick))
            {
                if (!CanDropChampion(champion, row, isPick))
                {
                    ClearInsertionIndicator();
                    return;
                }

                var collection = GetChampionCollection(row, isPick);
                if (ResolveDropIndexFromHover(row, isPick) is null || !IsPointerOverItemsHost(listElement, pagePosition))
                    ShowInsertionIndicatorAtIndex(row, isPick, collection.Count);

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
            return UpdateDragFeedback(e.GetPosition(this), e.GetPosition(PageScrollViewer));
        }

        private bool UpdateDragFeedback(Point pagePosition)
        {
            return UpdateDragFeedback(pagePosition, TranslatePoint(pagePosition, PageScrollViewer));
        }

        private bool UpdateDragFeedback(Point pagePosition, Point scrollViewerPosition)
        {
            if (!IsPointInsideElement(this, pagePosition))
            {
                ClearInsertionIndicator();
                HideDragPreview();
                return false;
            }

            AutoScrollPageWhileDragging(scrollViewerPosition);
            UpdateDragPreviewPosition(pagePosition);
            return true;
        }

        private void AutoScrollPageWhileDragging(Point position)
        {
            if (PageScrollViewer.ScrollableHeight <= 0 || PageScrollViewer.ViewportHeight <= 0)
                return;

            if (position.Y < DragAutoScrollEdgeDistance)
            {
                PageScrollViewer.ScrollToVerticalOffset(Math.Max(0, PageScrollViewer.VerticalOffset - DragAutoScrollStep));
                return;
            }

            if (position.Y > PageScrollViewer.ViewportHeight - DragAutoScrollEdgeDistance)
            {
                PageScrollViewer.ScrollToVerticalOffset(Math.Min(PageScrollViewer.ScrollableHeight, PageScrollViewer.VerticalOffset + DragAutoScrollStep));
            }
        }

        private static bool IsPointInsideElement(FrameworkElement element, Point point)
        {
            return point.X >= 0
                && point.Y >= 0
                && point.X <= element.ActualWidth
                && point.Y <= element.ActualHeight;
        }

        private void ShowDragPreview(string text, Point position)
        {
            DragPreviewText.Text = text;
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
        }

        private void ClearDragState()
        {
            ClearInsertionIndicator();
            _isChampionDragActive = false;
            _activeChampionDragData = null;
            _draggedChampion = null;
            _draggedReferenceChampion = null;
            RefreshTargetBrushes();
        }

        private static ObservableCollection<ChampionSelectionItem> GetChampionCollection(PositionRow row, bool isPick)
        {
            return isPick ? row.PickChampions : row.BanChampions;
        }

        private int? ResolveDropIndexFromHover(PositionRow row, bool isPick)
        {
            return ReferenceEquals(_dragHoverRow, row) && _dragHoverIsPick == isPick
                ? _dragHoverTargetIndex
                : null;
        }

        private int? ResolveDropIndexFromItem(FrameworkElement? itemElement, ChampionSelectionItem targetChampion, DragEventArgs e)
        {
            if (itemElement is null)
                return null;

            var pillElement = FindTaggedVisualChild(itemElement, ChampionPillTag) ?? itemElement;
            return ResolveDropIndexFromItemPosition(itemElement, targetChampion, e.GetPosition(pillElement));
        }

        private int? ResolveDropIndexFromItem(FrameworkElement? itemElement, ChampionSelectionItem targetChampion, Point pagePosition)
        {
            if (itemElement is null)
                return null;

            var pillElement = FindTaggedVisualChild(itemElement, ChampionPillTag) ?? itemElement;
            return ResolveDropIndexFromItemPosition(itemElement, targetChampion, TranslatePoint(pagePosition, pillElement));
        }

        private static int? ResolveDropIndexFromItemPosition(FrameworkElement? itemElement, ChampionSelectionItem targetChampion, Point pointerPositionInPill)
        {
            if (itemElement is null)
                return null;

            var collection = GetChampionCollection(targetChampion.Row, targetChampion.IsPick);
            int targetIndex = collection.IndexOf(targetChampion);
            if (targetIndex < 0)
                return null;

            var pillElement = FindTaggedVisualChild(itemElement, ChampionPillTag) ?? itemElement;
            bool insertAfter = pointerPositionInPill.X >= pillElement.ActualWidth / 2;
            return insertAfter ? targetIndex + 1 : targetIndex;
        }

        private static bool IsPointerOverItemsHost(DependencyObject? listContainer, DragEventArgs e)
        {
            var itemsControl = listContainer is null
                ? null
                : FindVisualChild<ItemsControl>(listContainer);

            if (itemsControl is null)
                return false;

            Point pointer = e.GetPosition(itemsControl);
            return pointer.X >= 0
                && pointer.Y >= 0
                && pointer.X <= itemsControl.ActualWidth
                && pointer.Y <= itemsControl.ActualHeight;
        }

        private bool IsPointerOverItemsHost(DependencyObject? listContainer, Point pagePosition)
        {
            var itemsControl = listContainer is null
                ? null
                : FindVisualChild<ItemsControl>(listContainer);

            if (itemsControl is null)
                return false;

            Point pointer = TranslatePoint(pagePosition, itemsControl);
            return pointer.X >= 0
                && pointer.Y >= 0
                && pointer.X <= itemsControl.ActualWidth
                && pointer.Y <= itemsControl.ActualHeight;
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

        private void ShowInsertionIndicator(PositionRow row, bool isPick, ChampionSelectionItem? champion, bool insertAfter, int targetIndex)
        {
            ClearInsertionIndicator();

            int resolvedTargetIndex = targetIndex;
            if (champion is null)
            {
                var collection = GetChampionCollection(row, isPick);
                if (collection.Count > 0)
                {
                    champion = collection[^1];
                    insertAfter = true;
                }
            }

            _dragHoverChampion = champion;
            _dragHoverRow = row;
            _dragHoverIsPick = isPick;
            _dragHoverInsertAfter = insertAfter;
            _dragHoverTargetIndex = resolvedTargetIndex;

            if (champion is not null)
            {
                if (insertAfter)
                    champion.InsertAfterIndicatorVisibility = Visibility.Visible;
                else
                    champion.InsertBeforeIndicatorVisibility = Visibility.Visible;

                RefreshTargetBrushes();
                return;
            }

            if (isPick)
                row.PickDropIndicatorVisibility = Visibility.Visible;
            else
                row.BanDropIndicatorVisibility = Visibility.Visible;

            RefreshTargetBrushes();
        }

        private void ClearInsertionIndicator()
        {
            if (_dragHoverChampion is not null)
            {
                _dragHoverChampion.InsertBeforeIndicatorVisibility = Visibility.Collapsed;
                _dragHoverChampion.InsertAfterIndicatorVisibility = Visibility.Collapsed;
            }

            if (_dragHoverRow is not null)
            {
                _dragHoverRow.PickDropIndicatorVisibility = Visibility.Collapsed;
                _dragHoverRow.BanDropIndicatorVisibility = Visibility.Collapsed;
            }

            if (_duplicateDropChampion is not null)
                _duplicateDropChampion.IsDuplicateDropTarget = false;

            _dragHoverChampion = null;
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
                bool pickDropHover = _isChampionDragActive && ReferenceEquals(_dragHoverRow, row) && _dragHoverIsPick;
                bool banDropHover = _isChampionDragActive && ReferenceEquals(_dragHoverRow, row) && !_dragHoverIsPick;
                bool pickActive = ReferenceEquals(_activeTargetRow, row) && _activeTargetIsPick;
                bool banActive = ReferenceEquals(_activeTargetRow, row) && !_activeTargetIsPick;

                row.PickBorderBrush = pickDropHover ? DropHoverTargetBrush : pickActive ? ActiveTargetBrush : InactiveTargetBrush;
                row.BanBorderBrush = banDropHover ? DropHoverTargetBrush : banActive ? ActiveTargetBrush : InactiveTargetBrush;
                row.PickBackgroundBrush = pickDropHover ? DropHoverBackgroundBrush : pickActive ? ActiveTargetBackgroundBrush : InactiveTargetBackgroundBrush;
                row.BanBackgroundBrush = banDropHover ? DropHoverBackgroundBrush : banActive ? ActiveTargetBackgroundBrush : InactiveTargetBackgroundBrush;
            }
        }

        private void SaveChampionPreferences()
        {
            foreach (var row in _rows)
            {
                _settings.Preferences[row.Position] = new PositionPreference
                {
                    PickChampionIds = row.PickChampions.Select(champion => champion.ChampionId).ToList(),
                    BanChampionIds = row.BanChampions.Select(champion => champion.ChampionId).ToList()
                };
            }

            _settings.Save();
        }

        private static void UpdateRowTextFromCollection(PositionRow row, bool isPick)
        {
            string text = string.Join(", ", GetChampionCollection(row, isPick).Select(champion => champion.DisplayText));
            if (isPick)
                row.PickChampionIds = text;
            else
                row.BanChampionIds = text;
        }

        private sealed class ChampionDragData
        {
            public int ChampionId { get; private init; }
            public string ChampionName { get; private init; } = "";
            public ChampionSelectionItem? SourceItem { get; private init; }
            public string PreviewText => ChampionName;

            public static ChampionDragData FromSelection(ChampionSelectionItem champion)
            {
                return new ChampionDragData
                {
                    ChampionId = champion.ChampionId,
                    ChampionName = champion.DisplayText,
                    SourceItem = champion
                };
            }

            public static ChampionDragData FromReference(ChampionInfo champion)
            {
                return new ChampionDragData
                {
                    ChampionId = champion.Id,
                    ChampionName = champion.Name
                };
            }

            public ChampionInfo ToChampionInfo()
            {
                return new ChampionInfo(ChampionId, ChampionName);
            }
        }

    }

    public class ChampionSelectionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Visibility _insertBeforeIndicatorVisibility = Visibility.Collapsed;
        private Visibility _insertAfterIndicatorVisibility = Visibility.Collapsed;
        private bool _isDuplicateDropTarget;

        public int ChampionId { get; init; }
        public string DisplayText { get; init; } = "";
        public PositionRow Row { get; init; } = null!;
        public bool IsPick { get; init; }

        public Visibility InsertBeforeIndicatorVisibility
        {
            get => _insertBeforeIndicatorVisibility;
            set => SetProperty(ref _insertBeforeIndicatorVisibility, value);
        }

        public Visibility InsertAfterIndicatorVisibility
        {
            get => _insertAfterIndicatorVisibility;
            set => SetProperty(ref _insertAfterIndicatorVisibility, value);
        }

        public bool IsDuplicateDropTarget
        {
            get => _isDuplicateDropTarget;
            set => SetProperty(ref _isDuplicateDropTarget, value);
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
        private Visibility _pickDropIndicatorVisibility = Visibility.Collapsed;
        private Visibility _banDropIndicatorVisibility = Visibility.Collapsed;

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

        public Visibility PickDropIndicatorVisibility
        {
            get => _pickDropIndicatorVisibility;
            set
            {
                if (EqualityComparer<Visibility>.Default.Equals(_pickDropIndicatorVisibility, value))
                    return;

                _pickDropIndicatorVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickDropIndicatorVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickPlaceholderVisibility)));
            }
        }

        public Visibility BanDropIndicatorVisibility
        {
            get => _banDropIndicatorVisibility;
            set
            {
                if (EqualityComparer<Visibility>.Default.Equals(_banDropIndicatorVisibility, value))
                    return;

                _banDropIndicatorVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanDropIndicatorVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BanPlaceholderVisibility)));
            }
        }

        public Visibility PickPlaceholderVisibility => PickChampions.Count == 0 && PickDropIndicatorVisibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility BanPlaceholderVisibility => BanChampions.Count == 0 && BanDropIndicatorVisibility != Visibility.Visible
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
