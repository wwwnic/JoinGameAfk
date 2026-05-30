using System.Text.Json;
using JoinGameAfk.Model;

namespace JoinGameAfk.Phase;

public partial class ChampSelect
{
    private void Log(string message)
    {
        _log?.Invoke(message);
    }

    private void LogStatus(ref string? lastMessage, string message)
    {
        if (string.Equals(lastMessage, message, StringComparison.Ordinal))
            return;

        lastMessage = message;
        Log(message);
    }

    private static string FormatChampionIds(IReadOnlyCollection<int> championIds)
    {
        return championIds.Count == 0 ? "none" : string.Join(", ", championIds.Select(FormatChampion));
    }

    private static string FormatChampion(int championId)
    {
        return ChampionCatalog.FormatWithName(championId);
    }

    private static string FormatTimeLeft(long timeLeftMs)
    {
        if (timeLeftMs <= 0)
            return "0.0s";

        return $"{timeLeftMs / 1000d:F1}s";
    }

    private static int GetDisplayTimeLeftSeconds(long timeLeftMs)
    {
        if (timeLeftMs <= 0)
            return 0;

        return (int)(timeLeftMs / 1000L);
    }

    private static string? GetSessionId(JsonElement root)
    {
        if (root.TryGetProperty("multiUserChatId", out var chatIdProperty))
            return chatIdProperty.GetString();

        return null;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt32(out value);
    }

    private static bool TryGetNumberAsInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (property.TryGetInt64(out value))
            return true;

        value = (long)property.GetDouble();
        return true;
    }

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}