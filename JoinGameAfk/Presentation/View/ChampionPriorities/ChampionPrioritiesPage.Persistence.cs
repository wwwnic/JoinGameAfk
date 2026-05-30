using System.Windows.Threading;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;

namespace JoinGameAfk.Presentation.View.ChampionPriorities
{
    public partial class ChampionPrioritiesPage
    {
        private void SaveChampionPreferences()
        {
            _rolePlanSettings.Preferences.Remove(Position.None);

            foreach (var row in _rows)
            {
                _rolePlanSettings.Preferences[row.Position] = new PositionPreference
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
            _rolePlanSettings.Save();
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
}
