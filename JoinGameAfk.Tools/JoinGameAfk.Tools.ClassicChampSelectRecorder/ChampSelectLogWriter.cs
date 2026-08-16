using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LcuClient;

namespace JoinGameAfk.Tools.ClassicChampSelectRecorder;

internal sealed class ChampSelectLogWriter : IDisposable
{
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "accountId",
        "displayName",
        "gameName",
        "nameVisibilityType",
        "obfuscatedPuuid",
        "obfuscatedSummonerId",
        "partyId",
        "playerAlias",
        "playerName",
        "profileIconId",
        "puuid",
        "summonerId",
        "summonerInternalName",
        "summonerName",
        "tagLine"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly Lock _writeLock = new();
    private readonly StreamWriter _writer;

    public ChampSelectLogWriter(string outputPath)
    {
        var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public void WriteEvent(Lcu.LeagueClientEvent apiEvent)
    {
        WriteRecord("websocket", apiEvent.Uri, apiEvent.EventType, apiEvent.DataJson, null);
    }

    public void WriteHttpResponse(string uri, string dataJson)
    {
        WriteRecord("http", uri, "Snapshot", dataJson, null);
    }

    public void WriteHttpError(string uri, int statusCode)
    {
        WriteRecord("http", uri, "Error", null, $"HTTP {statusCode}");
    }

    public void WriteNote(string message)
    {
        WriteRecord("recorder", string.Empty, "Note", null, message);
    }

    private void WriteRecord(string source, string uri, string eventType, string? dataJson, string? message)
    {
        var record = new JsonObject
        {
            ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source"] = source,
            ["uri"] = uri,
            ["eventType"] = eventType
        };

        if (!string.IsNullOrWhiteSpace(message))
            record["message"] = message;

        if (!string.IsNullOrWhiteSpace(dataJson))
            record["data"] = ParseAndSanitize(dataJson);

        string line = record.ToJsonString(SerializerOptions);
        lock (_writeLock)
        {
            _writer.WriteLine(line);
        }
    }

    internal static JsonNode? ParseAndSanitize(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create("[unparseable JSON omitted]");
        }

        Sanitize(root);
        return root;
    }

    private static void Sanitize(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (string propertyName in jsonObject.Select(property => property.Key).ToArray())
            {
                if (SensitivePropertyNames.Contains(propertyName))
                    jsonObject[propertyName] = "[redacted]";
                else
                    Sanitize(jsonObject[propertyName]);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? item in jsonArray)
                Sanitize(item);
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer.Dispose();
        }
    }
}
