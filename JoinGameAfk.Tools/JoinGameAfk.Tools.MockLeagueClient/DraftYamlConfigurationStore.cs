using System.IO;
using JoinGameAfk.Constant;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JoinGameAfk.Tools.MockLeagueClient;

internal static class DraftYamlConfigurationStore
{
    public const string FileName = "mock-league-draft-actions.yaml";

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string DefaultFilePath => Path.Combine(AppStorage.SettingsDirectoryPath, FileName);

    public static DraftYamlConfiguration Load(string filePath)
    {
        string yaml = File.ReadAllText(filePath);
        return Deserializer.Deserialize<DraftYamlConfiguration>(yaml) ?? new DraftYamlConfiguration();
    }

    public static void Save(string filePath, DraftYamlConfiguration configuration)
    {
        string? directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        string yaml = Serializer.Serialize(configuration);
        string temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(temporaryPath, yaml);
            if (File.Exists(filePath))
                File.Replace(temporaryPath, filePath, null);
            else
                File.Move(temporaryPath, filePath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch
        {
        }
    }
}

internal sealed class DraftYamlConfiguration
{
    public int Version { get; set; } = 1;
    public int QueueId { get; set; } = 400;
    public string QueueName { get; set; } = "Normal Draft";
    public int? LocalSlot { get; set; }
    public string? LocalRole { get; set; }
    public int? LocalPlayerCellId { get; set; }
    public string? LocalPlayerRole { get; set; }
    public bool RevealEnemyPickIntents { get; set; }
    public string ActivePhase { get; set; } = DraftPickStep.Planning.ToString();
    public List<DraftYamlTeamSlot> BlueTeam { get; set; } = [];
    public List<DraftYamlTeamSlot> RedTeam { get; set; } = [];
    public Dictionary<string, DraftYamlPhaseConfiguration> Phases { get; set; } = [];
}

internal sealed class DraftYamlTeamSlot
{
    public int Cell { get; set; }
    public string Role { get; set; } = MockLeagueClientRoles.DefaultRole;
}

internal sealed class DraftYamlPhaseConfiguration
{
    public int? TimeLeftSeconds { get; set; }
    public List<DraftYamlTimedAction> TimedActions { get; set; } = [];
    public List<DraftYamlOptionalTimedAction> OptionalTimedActions { get; set; } = [];
}

internal sealed class DraftYamlTimedAction
{
    public int Cell { get; set; }
    public string Type { get; set; } = "pick";
    public string? Champion { get; set; }
    public int? HoverAtSeconds { get; set; }
    public int? LockAtSeconds { get; set; }
}

internal sealed class DraftYamlOptionalTimedAction
{
    public int? Id { get; set; }
    public string Type { get; set; } = TimedCustomActionType.RoleSwap.ToString();
    public int SourceCell { get; set; } = 1;
    public int TargetCell { get; set; } = 2;
    public int TriggerAtSeconds { get; set; }
}
