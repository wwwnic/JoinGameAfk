using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JoinGameAfk.Model;
using JoinGameAfk.Phase;
using JoinGameAfk.Tools.MockLeagueClient;
using LcuClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class DraftFlowSoundTests
{
    private sealed class TestLog
    {
        public readonly ConcurrentQueue<string> Lines = new();

        public void Write(string line) => Lines.Enqueue(line);
    }

    private sealed class TestSoundSink
    {
        public readonly ConcurrentQueue<SoundAlertPlaybackRequest> Played = new();

        public void Handle(SoundAlertPlaybackRequest r)
        { if (r.Command == SoundAlertPlaybackCommand.PlayAlert) Played.Enqueue(r); }
    }

    [Test]
    public async Task ChampSelect_WhenLocalActionsStart_PlaysPickAndBanStartAlerts()
    {
        // Arrange: start mock server
        var state = new MockLeagueClientState();
        state.ApplyScenario(MockLeagueClientScenario.Ban);
        var log = new TestLog();
        int port = GetFreePort();
        string token = "mock-test";
        await using var server = new MockLeagueClientServer(state, port, token, log.Write);
        await server.StartAsync();
        try
        {
            Environment.SetEnvironmentVariable("JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_PORT", port.ToString());
            Environment.SetEnvironmentVariable("JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_TOKEN", token);

            // Create http client pointing at mock; this mirrors PhaseController's DEBUG path
            var http = new Lcu.LeagueClientHttp(new AuthModel(port.ToString(), Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"riot:{token}"))), _ => { });

            // Settings: automation off to avoid HTTP PATCH calls; only start alerts enabled
            var general = new GeneralSettings
            {
                ChampionSelectAutomationEnabled = false,
                AutoHoverChampionEnabled = false,
                AutoLockSelectionEnabled = false
            };
            var sounds = new SoundSettings();
            EnableOnlyStartAlerts(sounds);

            var roles = new RolePlanSettings();
            var sink = new TestSoundSink();
            var champSelect = new ChampSelect(http, general, roles, sounds, _ => { }, null, sink.Handle);

            // Act 1: Ban phase (local action in-progress) => BanActionStart
            string banJson = System.Text.Json.JsonSerializer.Serialize(state.GetChampSelectSessionPayload());
            await champSelect.HandleSessionJsonAsync(banJson, DateTime.UtcNow, CancellationToken.None);

            // Move to pick scenario
            state.ApplyScenario(MockLeagueClientScenario.Pick);
            string pickJson = System.Text.Json.JsonSerializer.Serialize(state.GetChampSelectSessionPayload());
            await champSelect.HandleSessionJsonAsync(pickJson, DateTime.UtcNow, CancellationToken.None);

            // Assert
            var alerts = sink.Played.Select(p => p.AlertId).ToList();
            Assert.That(alerts, Does.Contain(SoundAlertIds.BanActionStart), "Ban start alert should play");
            Assert.That(alerts, Does.Contain(SoundAlertIds.PickActionStart), "Pick start alert should play");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_PORT", null);
            Environment.SetEnvironmentVariable("JOIN_GAME_AFK_MOCK_LEAGUE_CLIENT_TOKEN", null);
        }
    }

    private static void EnableOnlyStartAlerts(SoundSettings settings)
    {
        settings.ResetSoundAlertOptionsToDefaults();
        foreach (var def in SoundAlertDefaults.Definitions)
        {
            var s = settings.GetSoundAlertSetting(def.Id);
            s.Enabled = def.Id is SoundAlertIds.BanActionStart or SoundAlertIds.PickActionStart;
            s.SoundKey = s.Enabled ? s.SoundKey : null; // disable others by removing sound
        }
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