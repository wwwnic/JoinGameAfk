namespace JoinGameAfk.Tools.MockLeagueClient;

internal static class MockLeagueClientRoles
{
    public const string DefaultRole = "top";

    public static string NormalizeDisplayRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "top" => "top",
            "jungle" or "jg" => "jungle",
            "middle" or "mid" => "mid",
            "bottom" or "adc" => "adc",
            "utility" or "support" => "support",
            _ => DefaultRole
        };
    }

    public static string ToLeagueAssignedPosition(string? role)
    {
        return NormalizeDisplayRole(role) switch
        {
            "mid" => "middle",
            "adc" => "bottom",
            "support" => "utility",
            string normalizedRole => normalizedRole
        };
    }

    public static string ToDisplayName(string? role)
    {
        return NormalizeDisplayRole(role) switch
        {
            "top" => "Top",
            "jungle" => "Jungle",
            "mid" => "Mid",
            "adc" => "ADC",
            "support" => "Support",
            _ => "Top"
        };
    }
}
