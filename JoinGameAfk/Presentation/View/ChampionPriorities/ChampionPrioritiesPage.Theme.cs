using System.Windows.Media;
using System.Windows.Threading;

namespace JoinGameAfk.Presentation.View
{
    public partial class ChampionPrioritiesPage
    {
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

        private Brush ResourceBrush(string key, Brush fallback)
        {
            return TryFindResource(key) as Brush ?? fallback;
        }
    }
}