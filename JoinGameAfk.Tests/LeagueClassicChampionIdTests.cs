using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using JoinGameAfk.Phase;
using JoinGameAfk.Tools.MockLeagueClient;
using LcuClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class LeagueClassicChampionIdTests
{
    [TestCase(60103, 103)]
    [TestCase(60081, 81)]
    [TestCase(103, 103)]
    public void ToCanonical_MapsClassicVariants(int leagueClientId, int expectedChampionId)
    {
        Assert.That(LeagueChampionId.ToCanonical(leagueClientId), Is.EqualTo(expectedChampionId));
    }

    [TestCase((int)LeagueQueueId.LeagueClassicPvpDraft, "League Classic")]
    [TestCase((int)LeagueQueueId.LeagueClassicCustomDraft, "League Classic Custom Draft")]
    public async Task ChampSelect_LeagueClassicDraft_UsesVariantForLcuAndCanonicalIdForDashboard(
        int queueId,
        string queueName)
    {
        var logs = new ConcurrentQueue<string>();
        int port = GetFreePort();
        const string token = "classic-id-test";
        var state = new MockLeagueClientState();
        state.UpdateQueue(queueId, queueName);
        state.ApplyScenario(MockLeagueClientScenario.Planning);

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
            rolePlans.Preferences[JoinGameAfk.Enums.Position.Default].PickChampionIds = [103];
            var champSelect = new ChampSelect(http, settings, rolePlans, new SoundSettings(), logs.Enqueue);

            string initialSessionJson = JsonSerializer.Serialize(state.GetChampSelectSessionPayload());
            await champSelect.HandleSessionJsonAsync(initialSessionJson, DateTime.UtcNow, CancellationToken.None);

            string updatedSessionJson = JsonSerializer.Serialize(state.GetChampSelectSessionPayload());
            await champSelect.HandleSessionJsonAsync(updatedSessionJson, DateTime.UtcNow, CancellationToken.None);

            string[] requestLogs = logs.Where(line => line.StartsWith("HTTP ", StringComparison.Ordinal)).ToArray();
            DashboardTeamSlotItem localPlayer = champSelect.LastDashboardStatus.MyTeamSlots.Single(slot => slot.IsLocalPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(requestLogs, Has.Some.Matches<string>(line =>
                    line.Contains("\"championId\":60103", StringComparison.Ordinal)));
                Assert.That(localPlayer.ChampionId, Is.EqualTo(103));
                Assert.That(localPlayer.ChampionName, Does.Contain("Ahri"));
                Assert.That(champSelect.LastDashboardStatus.PickChampionPriority.Single().IsAvailable, Is.True,
                    "Classic 60xxx grid IDs must match canonical role-plan IDs.");
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

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
