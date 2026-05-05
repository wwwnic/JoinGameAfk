using JoinGameAfk.Enums;

namespace JoinGameAfk.Model
{
    public sealed record DashboardChampionPlanItem
    {
        public int ChampionId { get; init; }
        public string Name { get; init; } = string.Empty;
        public Position SourcePosition { get; init; } = Position.Default;
        public bool IsAvailable { get; init; } = true;
        public string StatusText { get; init; } = string.Empty;
        public bool IsPlanReference { get; init; }
        public string PlanReferenceText { get; init; } = string.Empty;
        public bool IsOwnAction { get; init; }
    }

    public sealed record DashboardTeamSlotItem
    {
        public int ChampionId { get; init; }
        public string ChampionInitial { get; init; } = "?";
        public string ChampionName { get; init; } = "No champion";
        public string RoleName { get; init; } = "None";
        public bool IsLocalPlayer { get; init; }
        public bool IsPlanReference { get; init; }
        public string PlanReferenceText { get; init; } = string.Empty;
        public bool IsOwnAction { get; init; }
    }

    public sealed record DashboardStatus
    {
        public Position CurrentPosition { get; init; } = Position.None;
        public IReadOnlyList<DashboardTeamSlotItem> MyTeamSlots { get; init; } = [];
        public IReadOnlyList<DashboardTeamSlotItem> TheirTeamSlots { get; init; } = [];
        public IReadOnlyList<DashboardChampionPlanItem> MyTeamBans { get; init; } = [];
        public IReadOnlyList<DashboardChampionPlanItem> TheirTeamBans { get; init; } = [];
        public IReadOnlyList<DashboardChampionPlanItem> PickChampionPriority { get; init; } = [];
        public IReadOnlyList<DashboardChampionPlanItem> BanChampionPriority { get; init; } = [];
        public string BanChampionText { get; init; } = "Waiting for champion select.";
        public string BanLockText { get; init; } = "—";
        public string PickChampionText { get; init; } = "Waiting for champion select.";
        public string PickLockText { get; init; } = "—";
        public string ChampSelectSubPhase { get; init; } = "";
        public int TimeLeftSeconds { get; init; } = -1;
        public long TimeLeftMilliseconds { get; init; } = -1;
        public DateTime TimeLeftObservedAtUtc { get; init; } = DateTime.MinValue;
    }
}
