using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
        private static readonly SolidColorBrush ActiveTargetBrush = new((Color)ColorConverter.ConvertFromString("#3B82F6"));
        private static readonly SolidColorBrush InactiveTargetBrush = new((Color)ColorConverter.ConvertFromString("#334155"));
        private static readonly SolidColorBrush ActiveTargetBackgroundBrush = new((Color)ColorConverter.ConvertFromString("#0F1B2D"));
        private static readonly SolidColorBrush InactiveTargetBackgroundBrush = new((Color)ColorConverter.ConvertFromString("#111827"));
        private readonly ChampSelectSettings _settings;
        private readonly List<ChampionInfo> _allChampions;
        private List<ChampionInfo> _filteredChampions;
        private readonly List<PositionRow> _rows;
        private PositionRow? _activeTargetRow;
        private bool _activeTargetIsPick;
        private Point _dragStartPoint;
        private ChampionSelectionItem? _draggedChampion;
        private ChampionSelectionItem? _dragHoverChampion;
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
            if ((sender as FrameworkElement)?.DataContext is not ChampionInfo champion)
                return;

            TryAddChampionToActiveTarget(champion);
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

        private void SetActiveTarget(PositionRow row, bool isPick)
        {
            _activeTargetRow = row;
            _activeTargetIsPick = isPick;

            foreach (var item in _rows)
            {
                item.PickBorderBrush = ReferenceEquals(item, row) && isPick ? ActiveTargetBrush : InactiveTargetBrush;
                item.BanBorderBrush = ReferenceEquals(item, row) && !isPick ? ActiveTargetBrush : InactiveTargetBrush;
                item.PickBackgroundBrush = ReferenceEquals(item, row) && isPick ? ActiveTargetBackgroundBrush : InactiveTargetBackgroundBrush;
                item.BanBackgroundBrush = ReferenceEquals(item, row) && !isPick ? ActiveTargetBackgroundBrush : InactiveTargetBackgroundBrush;
            }

            ChampionSearchBox.Focus();
            ChampionSearchBox.SelectAll();
        }

        private void ChampionItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsDeleteModeEnabled)
                return;

            if ((sender as FrameworkElement)?.DataContext is not ChampionSelectionItem champion)
                return;

            _draggedChampion = champion;
            _dragStartPoint = e.GetPosition(this);
        }

        private void ChampionItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedChampion is null)
                return;

            Point currentPosition = e.GetPosition(this);
            if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var champion = _draggedChampion;
            ShowDragPreview(champion.DisplayText, currentPosition);
            try
            {
                DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(ChampionSelectionItem), champion), DragDropEffects.Move);
            }
            finally
            {
                HideDragPreview();
                ClearDragState();
            }

            e.Handled = true;
        }

        private void ChampionItem_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedChampion(e, out var champion) || (sender as FrameworkElement)?.DataContext is not ChampionSelectionItem targetChampion)
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

            var collection = GetChampionCollection(targetChampion.Row, targetChampion.IsPick);
            int targetIndex = collection.IndexOf(targetChampion);
            if (targetIndex < 0)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            bool insertAfter = e.GetPosition((IInputElement)sender).X >= ((FrameworkElement)sender).ActualWidth / 2;
            if (insertAfter)
            {
                if (targetIndex < collection.Count - 1)
                {
                    ShowInsertionIndicator(targetChampion.Row, targetChampion.IsPick, collection[targetIndex + 1], insertAfter: false, targetIndex + 1);
                }
                else
                {
                    ShowInsertionIndicator(targetChampion.Row, targetChampion.IsPick, null, insertAfter: true, collection.Count);
                }
            }
            else
            {
                ShowInsertionIndicator(targetChampion.Row, targetChampion.IsPick, targetChampion, insertAfter: false, targetIndex);
            }

            SetActiveTarget(targetChampion.Row, targetChampion.IsPick);
            UpdateDragPreviewPosition(e.GetPosition(this));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ChampionItem_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedChampion(e, out var champion) || (sender as FrameworkElement)?.DataContext is not ChampionSelectionItem targetChampion)
                return;

            if (!CanDropChampion(champion, targetChampion.Row, targetChampion.IsPick))
                return;

            int? targetIndex = _dragHoverRow == targetChampion.Row && _dragHoverIsPick == targetChampion.IsPick
                ? _dragHoverTargetIndex
                : null;

            MoveChampionToTarget(champion, targetChampion.Row, targetChampion.IsPick, targetIndex);
            e.Handled = true;
        }

        private void ChampionList_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedChampion(e, out var champion) || (sender as FrameworkElement)?.DataContext is not PositionRow row)
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

            // When dragging over empty space within the list container (not over a specific item),
            // show the insertion indicator at the end of the list.
            // (If the list is empty, this falls back to the row-level drop indicator.)
            if (_dragHoverRow != row || _dragHoverIsPick != isPick)
                ShowInsertionIndicator(row, isPick, null, insertAfter: true, collection.Count);

            SetActiveTarget(row, isPick);
            UpdateDragPreviewPosition(e.GetPosition(this));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void ChampionList_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedChampion(e, out var champion) || (sender as FrameworkElement)?.DataContext is not PositionRow row)
                return;

            bool isPick = string.Equals((sender as FrameworkElement)?.Tag as string, "Pick", StringComparison.OrdinalIgnoreCase);
            if (!CanDropChampion(champion, row, isPick))
                return;

            int? targetIndex = _dragHoverRow == row && _dragHoverIsPick == isPick
                ? _dragHoverTargetIndex
                : null;
            MoveChampionToTarget(champion, row, isPick, targetIndex);
            e.Handled = true;
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedChampion(e, out _))
                return;

            ClearInsertionIndicator();
            UpdateDragPreviewPosition(e.GetPosition(this));
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Page_DragLeave(object sender, DragEventArgs e)
        {
            if (!TryGetDraggedChampion(e, out _))
                return;
        }

        private void Page_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
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
                int destinationIndex = targetIndex ?? (collection.Count - 1);
                destinationIndex = Math.Clamp(destinationIndex, 0, Math.Max(collection.Count - 1, 0));
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

        private static bool TryGetDraggedChampion(DragEventArgs e, out ChampionSelectionItem? champion)
        {
            champion = e.Data.GetData(typeof(ChampionSelectionItem)) as ChampionSelectionItem;
            return champion is not null;
        }

        private static bool CanDropChampion(ChampionSelectionItem champion, PositionRow targetRow, bool targetIsPick)
        {
            return champion is not null && targetRow is not null;
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
            _draggedChampion = null;
            _dragHoverChampion = null;
            _dragHoverRow = null;
            _dragHoverInsertAfter = false;
            _dragHoverTargetIndex = null;
        }

        private static ObservableCollection<ChampionSelectionItem> GetChampionCollection(PositionRow row, bool isPick)
        {
            return isPick ? row.PickChampions : row.BanChampions;
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

                return;
            }

            if (isPick)
                row.PickDropIndicatorVisibility = Visibility.Visible;
            else
                row.BanDropIndicatorVisibility = Visibility.Visible;
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

            _dragHoverTargetIndex = null;
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

    }

    public class ChampionSelectionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Visibility _insertBeforeIndicatorVisibility = Visibility.Collapsed;
        private Visibility _insertAfterIndicatorVisibility = Visibility.Collapsed;

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
