using System.Text.Json;
using JoinGameAfk.Tools.MockLeagueClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class MockLeagueClientRegionLocaleTests
{
    [Test]
    public void RegionLocalePayload_ReflectsConfiguredEndpointValues()
    {
        var state = new MockLeagueClientState();

        AssertRegionLocale(state, "NA1", "en_US");

        state.UpdateRegionLocale(" EUW1 ", " fr_FR ");

        AssertRegionLocale(state, "EUW1", "fr_FR");
    }

    private static void AssertRegionLocale(
        MockLeagueClientState state,
        string expectedPlatformId,
        string expectedLocale)
    {
        string json = JsonSerializer.Serialize(state.GetRegionLocalePayload());
        using var document = JsonDocument.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("platformId").GetString(), Is.EqualTo(expectedPlatformId));
            Assert.That(document.RootElement.GetProperty("locale").GetString(), Is.EqualTo(expectedLocale));
        });
    }
}
