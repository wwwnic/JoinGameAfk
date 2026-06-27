using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JoinGameAfk.Model;
using JoinGameAfk.Phase;
using JoinGameAfk.Tools.MockLeagueClient;
using LcuClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class ChampionEligibilityTests
{
    [Test]
    public void ChampSelectGrid_RecognizesGlobalAndQueueFreeToPlayChampions()
    {
        const string json = """
            [
              { "id": 11, "owned": false, "freeToPlay": true, "freeToPlayForQueue": false, "disabled": false },
              { "id": 12, "owned": false, "freeToPlay": false, "freeToPlayForQueue": true, "disabled": false },
              { "id": 13, "owned": false, "freeToPlay": false, "freeToPlayForQueue": false, "disabled": false },
              { "id": 14, "owned": true, "freeToPlay": false, "freeToPlayForQueue": false, "disabled": true },
              { "id": 15, "loyaltyReward": true },
              { "id": 16, "xboxGPReward": true },
              { "id": 17, "rented": true },
              { "id": 18, "ownership": { "owned": true } },
              { "id": 19, "ownership": { "rental": { "rented": true } } },
              { "id": 20, "name": "Unknown Access Signal" },
              { "id": -1, "owned": false, "freeToPlay": false, "freeToPlayForQueue": false, "disabled": false }
            ]
            """;

        ChampionEligibilitySnapshot snapshot = LeagueChampionEligibilityService.ParseChampSelectGrid(json);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.GetUnavailableStatus(11), Is.Null, "Global free-to-play champions must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(12), Is.Null, "Queue-specific free-to-play champions must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(13), Is.EqualTo("Not owned"));
            Assert.That(snapshot.GetUnavailableStatus(14), Is.EqualTo("Disabled"));
            Assert.That(snapshot.GetUnavailableStatus(15), Is.Null, "Loyalty rewards must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(16), Is.Null, "Xbox Game Pass rewards must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(17), Is.Null, "Rentals must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(18), Is.Null, "Nested ownership must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(19), Is.Null, "Nested rental ownership must be selectable.");
            Assert.That(snapshot.GetUnavailableStatus(20), Is.Null, "Missing access fields must remain unknown rather than blocked.");
            Assert.That(snapshot.GetUnavailableStatus(-1), Is.Null, "The real grid includes a sentinel id that must be ignored.");
        });
    }

    [Test]
    public void MockLeagueClient_AddOwnedChampion_UpdatesGridAndInventoryPayloads()
    {
        var state = new MockLeagueClientState();

        bool added = state.AddOwnedChampion(6);
        bool addedAgain = state.AddOwnedChampion(6);
        using JsonDocument gridDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            state.GetChampSelectGridChampionsPayload(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using JsonDocument inventoryDocument = JsonDocument.Parse(JsonSerializer.Serialize(state.GetChampionInventoryPayload()));
        JsonElement gridChampion = gridDocument.RootElement
            .EnumerateArray()
            .Single(champion => champion.GetProperty("id").GetInt32() == 6);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.True);
            Assert.That(addedAgain, Is.False);
            Assert.That(gridChampion.GetProperty("owned").GetBoolean(), Is.True);
            Assert.That(
                inventoryDocument.RootElement.EnumerateArray().Select(champion => champion.GetProperty("id").GetInt32()),
                Does.Contain(6));
        });
    }

    [Test]
    public void MockLeagueClient_AllChampionOwnershipMode_ReportsTheWholeCatalogAsOwned()
    {
        var state = new MockLeagueClientState();
        state.UpdateChampionOwnershipMode(MockChampionOwnershipMode.AllChampions);

        using JsonDocument gridDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            state.GetChampSelectGridChampionsPayload(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using JsonDocument inventoryDocument = JsonDocument.Parse(JsonSerializer.Serialize(state.GetChampionInventoryPayload()));
        JsonElement[] gridChampions = gridDocument.RootElement.EnumerateArray().ToArray();
        JsonElement[] inventoryChampions = inventoryDocument.RootElement.EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(state.GetOwnedChampionCount(), Is.EqualTo(ChampionCatalog.All.Count));
            Assert.That(gridChampions, Has.Length.EqualTo(ChampionCatalog.All.Count));
            Assert.That(inventoryChampions, Has.Length.EqualTo(ChampionCatalog.All.Count));
            Assert.That(gridChampions.All(champion => champion.GetProperty("owned").GetBoolean()), Is.True);
            Assert.That(
                inventoryChampions.Select(champion => champion.GetProperty("id").GetInt32()),
                Is.EquivalentTo(ChampionCatalog.All.Select(champion => champion.Key)));
        });
    }

    [Test]
    public void MockLeagueClient_DraftYamlChampionOwnership_SupportsListAndAllModes()
    {
        var state = new MockLeagueClientState();

        state.ImportDraftYamlConfiguration(new DraftYamlConfiguration
        {
            ChampionOwnership = new DraftYamlChampionOwnershipConfiguration
            {
                Default = "none",
                Owned = ["Ahri", "Ashe"],
                NotOwned = ["Ashe"]
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(state.GetChampionOwnershipMode(), Is.EqualTo(MockChampionOwnershipMode.ConfiguredInventory));
            Assert.That(state.GetOwnedChampionCount(), Is.EqualTo(1));
            Assert.That(GetOwnedChampionIds(state), Is.EquivalentTo(new[] { 103 }));
            Assert.That(GetGridChampionOwned(state, 103), Is.True);
            Assert.That(GetGridChampionOwned(state, 22), Is.False);
            Assert.That(state.ExportDraftYamlConfiguration().ChampionOwnership?.Default, Is.EqualTo("none"));
            Assert.That(
                state.ExportDraftYamlConfiguration().ChampionOwnership?.Owned,
                Is.EquivalentTo(new[] { "Ahri" }));
            Assert.That(
                state.ExportDraftYamlConfiguration().ChampionOwnership?.NotOwned,
                Is.EquivalentTo(new[] { "Ashe" }));
        });

        state.ImportDraftYamlConfiguration(new DraftYamlConfiguration
        {
            ChampionOwnership = new DraftYamlChampionOwnershipConfiguration
            {
                Default = "all",
                NotOwned = ["Yasuo"]
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(state.GetChampionOwnershipMode(), Is.EqualTo(MockChampionOwnershipMode.AllChampions));
            Assert.That(state.GetOwnedChampionCount(), Is.EqualTo(ChampionCatalog.All.Count - 1));
            Assert.That(
                GetOwnedChampionIds(state),
                Is.EquivalentTo(ChampionCatalog.All.Select(champion => champion.Key).Except(new[] { 157 })));
            Assert.That(GetGridChampionOwned(state, 157), Is.False);
            Assert.That(state.ExportDraftYamlConfiguration().ChampionOwnership?.Default, Is.EqualTo("all"));
            Assert.That(state.ExportDraftYamlConfiguration().ChampionOwnership?.Owned, Is.Null);
            Assert.That(
                state.ExportDraftYamlConfiguration().ChampionOwnership?.NotOwned,
                Is.EquivalentTo(new[] { "Yasuo" }));
        });
    }

    [Test]
    public void MockLeagueClient_DraftYamlChampionGrid_SupportsQueueFreeRotationAndDisabledFlags()
    {
        var state = new MockLeagueClientState();

        state.ImportDraftYamlConfiguration(new DraftYamlConfiguration
        {
            ChampionOwnership = new DraftYamlChampionOwnershipConfiguration
            {
                Default = "none",
                Owned = ["Darius"]
            },
            ChampionGrid = new DraftYamlChampionGridConfiguration
            {
                FreeToPlayForQueue = ["Gwen"],
                FreeToPlay = ["Garen"],
                Rented = ["Ahri"],
                LoyaltyReward = ["Ashe"],
                XboxGPReward = ["Yasuo"],
                Disabled = ["Darius"]
            }
        });

        using JsonDocument gridDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            state.GetChampSelectGridChampionsPayload(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        ChampionEligibilitySnapshot snapshot = LeagueChampionEligibilityService.ParseChampSelectGrid(
            JsonSerializer.Serialize(state.GetChampSelectGridChampionsPayload(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        DraftYamlConfiguration exported = state.ExportDraftYamlConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(GetGridChampionBoolean(gridDocument, 887, "freeToPlayForQueue"), Is.True, "Gwen should be queue free-to-play in the mock grid.");
            Assert.That(snapshot.GetUnavailableStatus(887), Is.Null, "Queue free-to-play champions should remain selectable even when not owned.");
            Assert.That(GetGridChampionBoolean(gridDocument, 86, "freeToPlay"), Is.True);
            Assert.That(GetGridChampionBoolean(gridDocument, 103, "rented"), Is.True);
            Assert.That(GetGridChampionBoolean(gridDocument, 22, "loyaltyReward"), Is.True);
            Assert.That(GetGridChampionBoolean(gridDocument, 157, "xboxGPReward"), Is.True);
            Assert.That(GetGridChampionBoolean(gridDocument, 122, "disabled"), Is.True);
            Assert.That(snapshot.GetUnavailableStatus(122), Is.EqualTo("Disabled"), "Disabled should win over ownership.");
            Assert.That(exported.ChampionGrid?.FreeToPlayForQueue, Is.EquivalentTo(new[] { "Gwen" }));
            Assert.That(exported.ChampionGrid?.Disabled, Is.EquivalentTo(new[] { "Darius" }));
        });
    }

    [Test]
    public async Task ChampSelect_UsesGridEligibilityOncePerSession_AndHoversQueueFreeChampion()
    {
        var logs = new ConcurrentQueue<string>();
        int port = GetFreePort();
        const string token = "eligibility-test";
        var state = new MockLeagueClientState();
        state.ApplyScenario(MockLeagueClientScenario.Planning);
        state.SetChampSelectGridChampion(999, owned: false);
        state.SetChampSelectGridChampion(6, freeToPlayForQueue: true);

        await using var server = new MockLeagueClientServer(state, port, token, logs.Enqueue);
        await server.StartAsync();

        try
        {
            using var http = new Lcu.LeagueClientHttp(CreateAuth(port, token));
            var settings = new GeneralSettings
            {
                ChampionSelectAutomationEnabled = true,
                AutoHoverChampionEnabled = true,
                ChampionHoverDelaySeconds = 0,
                PlanningHoverDelaySeconds = 0,
                AutoLockSelectionEnabled = false
            };
            var rolePlans = new RolePlanSettings();
            rolePlans.Preferences[JoinGameAfk.Enums.Position.Default].PickChampionIds = [999, 6];
            var champSelect = new ChampSelect(http, settings, rolePlans, new SoundSettings(), logs.Enqueue);
            string sessionJson = JsonSerializer.Serialize(state.GetChampSelectSessionPayload());

            await champSelect.HandleSessionJsonAsync(sessionJson, DateTime.UtcNow, CancellationToken.None);
            await champSelect.HandleSessionJsonAsync(sessionJson, DateTime.UtcNow, CancellationToken.None);

            string[] requestLogs = logs.Where(line => line.StartsWith("HTTP ", StringComparison.Ordinal)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(requestLogs.Count(line => line == "HTTP GET /lol-champ-select/v1/all-grid-champions"), Is.EqualTo(1));
                Assert.That(requestLogs, Has.None.EqualTo("HTTP GET /lol-summoner/v1/current-summoner"));
                Assert.That(requestLogs, Has.None.Matches<string>(line => line.StartsWith("HTTP GET /lol-champions/v1/inventories/", StringComparison.Ordinal)));
                Assert.That(requestLogs, Has.None.Matches<string>(line => line.Contains("\"championId\":999", StringComparison.Ordinal)));
                Assert.That(requestLogs, Has.Some.Matches<string>(line => line.Contains("\"championId\":6", StringComparison.Ordinal)), string.Join(Environment.NewLine, logs));
            });
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task ChampSelect_WithoutPickPlan_DoesNotRequestEligibility()
    {
        var logs = new ConcurrentQueue<string>();
        int port = GetFreePort();
        const string token = "no-pick-plan-test";
        var state = new MockLeagueClientState();
        state.ApplyScenario(MockLeagueClientScenario.Planning);

        await using var server = new MockLeagueClientServer(state, port, token, logs.Enqueue);
        await server.StartAsync();

        try
        {
            using var http = new Lcu.LeagueClientHttp(CreateAuth(port, token));
            var champSelect = new ChampSelect(
                http,
                new GeneralSettings(),
                new RolePlanSettings(),
                new SoundSettings());
            string sessionJson = JsonSerializer.Serialize(state.GetChampSelectSessionPayload());

            await champSelect.HandleSessionJsonAsync(sessionJson, DateTime.UtcNow, CancellationToken.None);

            Assert.That(
                logs.Where(line => line.StartsWith("HTTP GET ", StringComparison.Ordinal)),
                Is.Empty,
                "A session without configured pick candidates does not need eligibility data.");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task ChampSelect_WhenGridIsUnavailable_UsesOwnedFallbackWithoutBlockingUnknownChampion()
    {
        var logs = new ConcurrentQueue<string>();
        int port = GetFreePort();
        const string token = "eligibility-fallback-test";
        var state = new MockLeagueClientState();
        state.ApplyScenario(MockLeagueClientScenario.Planning);
        state.SetChampSelectGridAvailable(false);

        await using var server = new MockLeagueClientServer(state, port, token, logs.Enqueue);
        await server.StartAsync();

        try
        {
            using var http = new Lcu.LeagueClientHttp(CreateAuth(port, token));
            var settings = new GeneralSettings
            {
                ChampionSelectAutomationEnabled = true,
                AutoHoverChampionEnabled = true,
                ChampionHoverDelaySeconds = 0,
                PlanningHoverDelaySeconds = 0,
                AutoLockSelectionEnabled = false
            };
            var rolePlans = new RolePlanSettings();
            rolePlans.Preferences[JoinGameAfk.Enums.Position.Default].PickChampionIds = [6];
            var champSelect = new ChampSelect(http, settings, rolePlans, new SoundSettings());
            string sessionJson = JsonSerializer.Serialize(state.GetChampSelectSessionPayload());

            await champSelect.HandleSessionJsonAsync(sessionJson, DateTime.UtcNow, CancellationToken.None);

            string[] requestLogs = logs.Where(line => line.StartsWith("HTTP ", StringComparison.Ordinal)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(requestLogs, Does.Contain("HTTP GET /lol-champ-select/v1/all-grid-champions"));
                Assert.That(requestLogs, Does.Contain("HTTP GET /lol-champions/v1/owned-champions-minimal"));
                Assert.That(requestLogs, Has.None.Matches<string>(line => line.StartsWith("HTTP GET /lol-summoner/v1/current-summoner", StringComparison.Ordinal)));
                Assert.That(requestLogs, Has.None.Matches<string>(line => line.StartsWith("HTTP GET /lol-champions/v1/inventories/", StringComparison.Ordinal)));
                Assert.That(requestLogs, Has.Some.Matches<string>(line => line.Contains("\"championId\":6", StringComparison.Ordinal)));
            });
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static AuthModel CreateAuth(int port, string token)
    {
        string encodedToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"riot:{token}"));
        return new AuthModel(port.ToString(), encodedToken);
    }

    private static int[] GetOwnedChampionIds(MockLeagueClientState state)
    {
        using JsonDocument inventoryDocument = JsonDocument.Parse(JsonSerializer.Serialize(state.GetChampionInventoryPayload()));
        return inventoryDocument.RootElement
            .EnumerateArray()
            .Select(champion => champion.GetProperty("id").GetInt32())
            .ToArray();
    }

    private static bool GetGridChampionOwned(MockLeagueClientState state, int championId)
    {
        using JsonDocument gridDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            state.GetChampSelectGridChampionsPayload(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return GetGridChampionBoolean(gridDocument, championId, "owned");
    }

    private static bool GetGridChampionBoolean(JsonDocument gridDocument, int championId, string propertyName)
    {
        return gridDocument.RootElement
            .EnumerateArray()
            .Single(champion => champion.GetProperty("id").GetInt32() == championId)
            .GetProperty(propertyName)
            .GetBoolean();
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
