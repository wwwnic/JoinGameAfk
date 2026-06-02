namespace JoinGameAfk.Tools.MockLeagueClient;

internal enum MockQueueMode
{
    BlindPick,
    DraftPick
}

internal enum DraftPickStep
{
    Planning,
    Ban,
    BlueFirstPick,
    RedFirstRotation,
    BlueSecondRotation,
    RedSecondRotation,
    BlueFinalRotation,
    RedLastPick,
    Finalization,
    InGame
}

internal sealed record DraftPickStepOption(DraftPickStep Step, string DisplayName, string DetailText)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

internal static class DraftPickSteps
{
    public static IReadOnlyList<DraftPickStepOption> All { get; } =
    [
        new(DraftPickStep.Planning, "Planning", "Assigned role and hover intent"),
        new(DraftPickStep.Ban, "Ban", "All 10 players ban at once"),
        new(DraftPickStep.BlueFirstPick, "Blue 1", "Blue first pick"),
        new(DraftPickStep.RedFirstRotation, "Red 2", "Red picks 6 and 7"),
        new(DraftPickStep.BlueSecondRotation, "Blue 2", "Blue picks 2 and 3"),
        new(DraftPickStep.RedSecondRotation, "Red 2", "Red picks 8 and 9"),
        new(DraftPickStep.BlueFinalRotation, "Blue 2", "Blue picks 4 and 5"),
        new(DraftPickStep.RedLastPick, "Red 1", "Red last pick"),
        new(DraftPickStep.Finalization, "Finalization", "All picks locked"),
        new(DraftPickStep.InGame, "In Game", "Gameflow in progress")
    ];

    public static DraftPickStep Previous(DraftPickStep step)
    {
        int index = Math.Max(0, (int)step - 1);
        return (DraftPickStep)index;
    }

    public static DraftPickStep Next(DraftPickStep step)
    {
        int index = Math.Min(All.Count - 1, (int)step + 1);
        return (DraftPickStep)index;
    }

    public static string GetDisplayName(DraftPickStep step)
    {
        return All.FirstOrDefault(option => option.Step == step)?.DisplayName ?? step.ToString();
    }
}
