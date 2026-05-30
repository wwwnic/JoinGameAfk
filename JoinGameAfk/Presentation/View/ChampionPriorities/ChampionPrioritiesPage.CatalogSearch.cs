using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private void ChampionCatalog_CatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshChampionCatalogView);
        }

        private void ChampionTileCatalog_TileCatalogChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshChampionImages);
        }

        private void ChampionImageSelectionStore_SelectionsChanged(object? sender, EventArgs e)
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
            foreach (var champion in _rows.SelectMany(row => row.PickChampions.Concat(row.BanChampions)))
            {
                champion.PortraitImageSource = ChampionTileCatalog.GetSelectedImageSource(champion.ChampionId);
            }

            _filteredChampionReferences = CreateChampionReferenceItems(_filteredChampions);
            ChampionReferenceList.ItemsSource = _filteredChampionReferences;
            RefreshChampionPicturePicker();
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
            QueueChampionSearchScrollCorrection();
        }

        private void ChampionSearchBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueChampionSearchScrollCorrection();
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

            e.Handled = true;
        }

        private void QueueChampionSearchScrollCorrection()
        {
            Dispatcher.InvokeAsync(CorrectChampionSearchScrollOffset, DispatcherPriority.Loaded);
        }

        private void CorrectChampionSearchScrollOffset()
        {
            if (!ChampionSearchBox.IsLoaded)
                return;

            if (ChampionSearchBox.Template.FindName("PART_ContentHost", ChampionSearchBox) is not ScrollViewer contentHost)
                return;

            double availableTextWidth = Math.Max(
                0,
                ChampionSearchBox.ActualWidth
                    - ChampionSearchBox.Padding.Left
                    - ChampionSearchBox.Padding.Right
                    - ChampionSearchBox.BorderThickness.Left
                    - ChampionSearchBox.BorderThickness.Right);

            if (string.IsNullOrEmpty(ChampionSearchBox.Text)
                || MeasureChampionSearchTextWidth() <= availableTextWidth)
            {
                contentHost.ScrollToHorizontalOffset(0);
            }
        }

        private double MeasureChampionSearchTextWidth()
        {
            var typeface = new Typeface(
                ChampionSearchBox.FontFamily,
                ChampionSearchBox.FontStyle,
                ChampionSearchBox.FontWeight,
                ChampionSearchBox.FontStretch);

            return new FormattedText(
                ChampionSearchBox.Text,
                CultureInfo.CurrentUICulture,
                ChampionSearchBox.FlowDirection,
                typeface,
                ChampionSearchBox.FontSize,
                ChampionSearchBox.Foreground,
                VisualTreeHelper.GetDpi(ChampionSearchBox).PixelsPerDip)
                .WidthIncludingTrailingWhitespace;
        }

        private void RoleFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RoleFilterOption filter)
                return;

            SelectRoleFilter(filter);

            UpdateChampionFilter();
            e.Handled = true;
        }

        private void SelectRoleFilter(RoleFilterOption selectedFilter)
        {
            _activeRoleFilters.Clear();

            foreach (var filter in _roleFilters)
                filter.IsSelected = ReferenceEquals(filter, selectedFilter);

            if (selectedFilter.Position != Position.None)
                _activeRoleFilters.Add(selectedFilter.Position);
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
            if (IsChampionPictureEditMode)
            {
                _draggedReferenceChampion = null;
                OpenChampionPicturePicker(champion);
                e.Handled = true;
                return;
            }

            TryAddChampionToActiveTarget(champion);
            _draggedReferenceChampion = null;
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
                .Select(champion => new ChampionReferenceItem(champion, IsChampionPictureEditMode))
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
    }
}