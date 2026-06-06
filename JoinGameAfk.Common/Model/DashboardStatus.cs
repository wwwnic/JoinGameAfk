using JoinGameAfk.Enums;

namespace JoinGameAfk.Model
{
    public static class DashboardChampionAvailabilityReason
    {
        public const string None = "";
        public const string NotOwned = "NotOwned";
        public const string Banned = "Banned";
        public const string Selected = "Selected";
        public const string Failed = "Failed";
        public const string Blocked = "Blocked";
    }

    public static class DashboardDraftActionType
    {
        public const string None = "";
        public const string Pick = "Pick";
        public const string Ban = "Ban";
    }

    public static class DashboardDraftSelectionState
    {
        public const string None = "";
        public const string Hover = "Hover";
        public const string Locked = "Locked";
    }

    public sealed record DashboardChampionPlanItem
    {
        public int ChampionId { get; init; }
        public string Name { get; init; } = string.Empty;
        public Position SourcePosition { get; init; } = Position.Default;
        public bool IsAvailable { get; init; } = true;
        public string StatusText { get; init; } = string.Empty;
        public string UnavailableReasonKind { get; init; } = DashboardChampionAvailabilityReason.None;
        public bool IsPlanReference { get; init; }
        public string PlanReferenceText { get; init; } = string.Empty;
        public string PlanReferenceReasonKind { get; init; } = DashboardChampionAvailabilityReason.None;
        public bool IsOwnAction { get; init; }
        public string ActionType { get; init; } = DashboardDraftActionType.None;
        public string SelectionState { get; init; } = DashboardDraftSelectionState.None;
        public bool IsActionInProgress { get; init; }
    }

    public sealed record DashboardTeamSlotItem
    {
        public int CellId { get; init; }
        public int ChampionId { get; init; }
        public string ChampionName { get; init; } = "No champion";
        public string RoleName { get; init; } = "None";
        public bool IsLocalPlayer { get; init; }
        public bool IsPlanReference { get; init; }
        public string PlanReferenceText { get; init; } = string.Empty;
        public string PlanReferenceReasonKind { get; init; } = DashboardChampionAvailabilityReason.None;
        public bool IsOwnAction { get; init; }
        public string ActionType { get; init; } = DashboardDraftActionType.None;
        public string SelectionState { get; init; } = DashboardDraftSelectionState.None;
        public bool IsActionInProgress { get; init; }
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
        public bool AllConfiguredOptionsUnavailable { get; init; }
        public bool SkipsReadyCheck { get; init; }
        public bool IsUnsupportedMode { get; init; }
        public string UnsupportedQueueText { get; init; } = "";
        public string UnsupportedModeText { get; init; } = "";
        public string ReadyCheckResponse { get; init; } = "";
        public long ReadyCheckAutoAcceptDelayMilliseconds { get; init; } = -1;
        public long ReadyCheckAutoAcceptTimeLeftMilliseconds { get; init; } = -1;
        public DateTime ReadyCheckAutoAcceptObservedAtUtc { get; init; } = DateTime.MinValue;
        public int TimeLeftSeconds { get; init; } = -1;
        public long TimeLeftMilliseconds { get; init; } = -1;
        public DateTime TimeLeftObservedAtUtc { get; init; } = DateTime.MinValue;
        public string ActiveLockActionType { get; init; } = "";
        public long ActiveLockTimeLeftMilliseconds { get; init; } = -1;
        public DateTime ActiveLockTimeLeftObservedAtUtc { get; init; } = DateTime.MinValue;
    }
}
