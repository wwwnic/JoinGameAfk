using System.Windows.Media;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Services;

namespace JoinGameAfk.Presentation.View
{
    internal sealed class DashboardChampionPlanDisplayItem
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
        public ImageSource? PortraitImageSource { get; init; }
        public string ChipDisplayText { get; init; } = string.Empty;
        public double ChipDisplayFontSize { get; init; } = ChampionChipLabelFormatter.DefaultFontSize;
        public string ToolTipText { get; init; } = string.Empty;
    }

    internal static class DashboardChampionPlanDisplay
    {
        public static IReadOnlyList<DashboardChampionPlanDisplayItem> CreateList(
            IEnumerable<DashboardChampionPlanItem> champions)
        {
            return champions
                .Select(CreateItem)
                .ToList();
        }

        private static DashboardChampionPlanDisplayItem CreateItem(DashboardChampionPlanItem champion)
        {
            string championName = GetChampionDisplayName(champion.ChampionId, champion.Name);
            ChampionChipLabel chipLabel = ChampionChipLabelFormatter.Format(championName);

            return new DashboardChampionPlanDisplayItem
            {
                ChampionId = champion.ChampionId,
                Name = championName,
                SourcePosition = champion.SourcePosition,
                IsAvailable = champion.IsAvailable,
                StatusText = champion.StatusText,
                UnavailableReasonKind = champion.UnavailableReasonKind,
                IsPlanReference = champion.IsPlanReference,
                PlanReferenceText = champion.PlanReferenceText,
                PlanReferenceReasonKind = champion.PlanReferenceReasonKind,
                IsOwnAction = champion.IsOwnAction,
                PortraitImageSource = GetChampionPortrait(champion.ChampionId, championName),
                ChipDisplayText = chipLabel.Text,
                ChipDisplayFontSize = chipLabel.FontSize,
                ToolTipText = BuildToolTip(chipLabel.ToolTipName, champion.StatusText, champion.PlanReferenceText)
            };
        }

        private static ImageSource? GetChampionPortrait(int championId, string championName)
        {
            if (championId > 0)
                return ChampionTileCatalog.GetSelectedImageSource(championId);

            return ChampionCatalog.TryGetByName(championName, out var champion)
                ? ChampionTileCatalog.GetSelectedImageSource(champion!.Id)
                : null;
        }

        private static string GetChampionDisplayName(int championId, string fallbackName)
        {
            if (championId > 0 && ChampionCatalog.TryGetById(championId, out var champion))
                return champion!.Name;

            return string.IsNullOrWhiteSpace(fallbackName)
                ? "No champion"
                : fallbackName;
        }

        private static string BuildToolTip(string championName, string statusText, string planReferenceText)
        {
            var lines = new List<string> { championName };

            if (!string.IsNullOrWhiteSpace(statusText))
                lines.Add(statusText);

            if (!string.IsNullOrWhiteSpace(planReferenceText))
                lines.Add(planReferenceText);

            return string.Join(Environment.NewLine, lines);
        }
    }
}
