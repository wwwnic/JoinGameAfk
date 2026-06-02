using JoinGameAfk.Model;
using System.Windows.Media;

namespace JoinGameAfk.Tools.MockLeagueClient;

internal sealed class ChampionOption
{
    public ChampionOption(int championId, string name)
    {
        ChampionId = championId;
        Name = name;
        DisplayName = name;
        DetailText = championId > 0 ? $"ID {championId}" : string.Empty;
        ToolTipText = championId > 0 ? $"{name} ({championId})" : name;
        Initials = CreateInitials(name);
        PortraitImageSource = ChampionTileImageCatalog.GetSelectedImageSource(championId, name);

        var chipLabel = ChampionChipLabelFormatter.Format(name);
        ChipDisplayText = chipLabel.Text;
        ChipDisplayFontSize = chipLabel.FontSize;
    }

    public int ChampionId { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string DetailText { get; }
    public string ToolTipText { get; }
    public string Initials { get; }
    public ImageSource? PortraitImageSource { get; }
    public string ChipDisplayText { get; }
    public double ChipDisplayFontSize { get; }

    public static ChampionOption NoChampion { get; } = new(0, "No champion");

    public static IReadOnlyList<ChampionOption> LoadAll()
    {
        return new[] { NoChampion }
            .Concat(ChampionCatalog.All.Select(champion => new ChampionOption(champion.Id, champion.Name)))
            .ToList();
    }

    public override string ToString()
    {
        return DisplayName;
    }

    private static string CreateInitials(string name)
    {
        string normalizedName = string.IsNullOrWhiteSpace(name)
            ? "?"
            : name.Trim();

        if (normalizedName.Equals("No champion", StringComparison.OrdinalIgnoreCase))
            return "--";

        var initials = normalizedName
            .Split([' ', '\'', '.', '&', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();

        if (initials.Length > 0)
            return new string(initials);

        return normalizedName.Length == 1
            ? normalizedName.ToUpperInvariant()
            : normalizedName[..2].ToUpperInvariant();
    }
}

internal sealed class ChampionChipItem
{
    public ChampionChipItem(ChampionOption champion)
    {
        ChampionId = champion.ChampionId;
        Name = champion.Name;
        DisplayName = champion.DisplayName;
        DetailText = champion.DetailText;
        ToolTipText = champion.ToolTipText;
        Initials = champion.Initials;
        PortraitImageSource = champion.PortraitImageSource;
        ChipDisplayText = champion.ChipDisplayText;
        ChipDisplayFontSize = champion.ChipDisplayFontSize;
    }

    public int ChampionId { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string DetailText { get; }
    public string ToolTipText { get; }
    public string Initials { get; }
    public ImageSource? PortraitImageSource { get; }
    public string ChipDisplayText { get; }
    public double ChipDisplayFontSize { get; }
}

internal sealed record ChampionChipLabel(string Text, double FontSize);

internal static class ChampionChipLabelFormatter
{
    public const double DefaultFontSize = 10;
    private const double SmallFontSize = 9.25;
    private const double MinimumFontSize = 8;

    public static ChampionChipLabel Format(string name)
    {
        string text = string.IsNullOrWhiteSpace(name)
            ? "Unknown"
            : name.Trim();

        string displayText = ApplyKnownBreaks(text);
        return new ChampionChipLabel(displayText, GetFontSize(displayText));
    }

    private static string ApplyKnownBreaks(string text)
    {
        return text switch
        {
            "Aurelion Sol" => "Aurelion\nSol",
            "Bel'Veth" => "Bel\nVeth",
            "Cho'Gath" => "Cho\nGath",
            "Dr. Mundo" => "Dr.\nMundo",
            "Jarvan IV" => "Jarvan\nIV",
            "Kai'Sa" => "Kai\nSa",
            "Kha'Zix" => "Kha\nZix",
            "Kog'Maw" => "Kog\nMaw",
            "K'Sante" => "K\nSante",
            "LeBlanc" => "Le\nBlanc",
            "Master Yi" => "Master\nYi",
            "Miss Fortune" => "Miss\nFortune",
            "Mordekaiser" => "Morde\nkaiser",
            "Nunu & Willump" => "Nunu\nWillump",
            "Renata Glasc" => "Renata\nGlasc",
            "Tahm Kench" => "Tahm\nKench",
            "Twisted Fate" => "Twisted\nFate",
            "Vel'Koz" => "Vel\nKoz",
            "Xin Zhao" => "Xin\nZhao",
            _ => text
        };
    }

    private static double GetFontSize(string text)
    {
        int longestLineLength = text
            .Split('\n')
            .Max(line => line.Length);

        if (longestLineLength <= 8)
            return DefaultFontSize;

        if (longestLineLength <= 10)
            return SmallFontSize;

        return MinimumFontSize;
    }
}
