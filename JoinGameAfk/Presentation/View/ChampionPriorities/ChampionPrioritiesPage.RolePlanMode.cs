using System.Windows;
using JoinGameAfk.Enums;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        public void SetChampionSelectGameMode(LeagueGameMode gameMode)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsChampionSelectLockActive
                    || _hasSynchronizedChampionSelectGameMode
                    || _isChampionPictureDownloadInProgress
                    || _isChampionDataOperationInProgress)
                {
                    return;
                }

                _hasSynchronizedChampionSelectGameMode = true;
                SetRolePlanMode(gameMode);
            });
        }

        private void RolePlanModeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isChampionPictureDownloadInProgress || _isChampionDataOperationInProgress)
            {
                RolePlanModeToggle.IsChecked = IsLeagueClassicPlanMode;
                e.Handled = true;
                return;
            }

            LeagueGameMode requestedMode = RolePlanModeToggle.IsChecked == true
                ? LeagueGameMode.Classic
                : LeagueGameMode.Modern;
            SetRolePlanMode(requestedMode);
            e.Handled = true;
        }

        private void SetRolePlanMode(LeagueGameMode gameMode)
        {
            if (_activeRolePlanMode == gameMode)
                return;

            FlushPendingPreferenceSave();
            if (ChampionPicturePickerOverlay.Visibility == Visibility.Visible || IsChampionPictureEditMode)
                SetChampionPictureEditMode(false);

            _activeRolePlanMode = gameMode;
            IsLeagueClassicPlanMode = gameMode == LeagueGameMode.Classic;
            ReloadRolePlanRows();
            UpdateChampionFilter();
            RefreshChampionImages();
            PriorityListScrollViewer.ScrollToTop();
            ChampionReferenceScrollViewer.ScrollToTop();
        }
    }
}
