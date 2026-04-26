namespace JoinGameAfk.Model
{
    public sealed record DashboardStatus
    {
        public IReadOnlyList<string> PickChampionPriority { get; init; } = [];
        public IReadOnlyList<string> BanChampionPriority { get; init; } = [];
        public string BanChampionText { get; init; } = "Waiting for champion select.";
        public string BanLockText { get; init; } = "—";
        public string PickChampionText { get; init; } = "Waiting for champion select.";
        public string PickLockText { get; init; } = "—";
        public string ChampSelectSubPhase { get; init; } = "";
        public int TimeLeftSeconds { get; init; } = -1;
    }
}
