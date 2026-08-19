using System.Text.Json.Serialization;
using JoinGameAfk.Constant;
using JoinGameAfk.Enums;

namespace JoinGameAfk.Model
{
    [Flags]
    public enum RolePlanProfileSections
    {
        None = 0,
        RolePlans = 1,
        ChampionPictures = 2
    }

    public sealed class RolePlanProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public RolePlanProfileSections IncludedSections { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public int? IconChampionId { get; set; }

        public string? IconFileName { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<Position, PositionPreference>? RolePlans { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<int, string>? ChampionPictures { get; set; }
    }

    public sealed class RolePlanProfilesFile
    {
        public int Version { get; set; } = AppStorage.RolePlanProfilesFileVersion;

        public List<RolePlanProfile> Profiles { get; set; } = [];
    }
}
