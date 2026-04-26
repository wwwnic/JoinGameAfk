namespace JoinGameAfk.Model
{
    public sealed record DashboardStatus
    {
        public IReadOnlyList<string> PickChampionPriority { get; init; } = [];
        public IReadOnlyList<string> BanChampionPriority { get; init; } = [];
        public string BanChampionText { get; init; } = "—";
        public string BanStatusText { get; init; } = "Waiting for champion select.";
        public string BanLockText { get; init; } = "—";
        public string PickChampionText { get; init; } = "—";
        public string PickStatusText { get; init; } = "Waiting for champion select.";
        public string PickLockText { get; init; } = "—";
    }
}
