using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JoinGameAfk.Model;
using JoinGameAfk.Plugin.Services;
using JoinGameAfk.Tools.MockLeagueClient;
using LcuClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class ChampionDataSourceTests
{
    [TestCase(true, ChampionDataSourceMode.LeagueClient)]
    [TestCase(false, ChampionDataSourceMode.DataDragon)]
    public void Resolve_AutomaticallyUsesAvailableLocalSource(
        bool isLeagueClientConnected,
        ChampionDataSourceMode expected)
    {
        Assert.That(ChampionDataSourcePolicy.Resolve(isLeagueClientConnected), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ChampionDataSettings_IgnoresLegacyPermanentSourceChoice(int legacySourceMode)
    {
        string filePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"champion-data-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                filePath,
                $"{{\"Version\":2,\"PlatformId\":\"NA1\",\"Locale\":\"en_US\",\"SourceMode\":{legacySourceMode}}}");

            ChampionDataSettings settings = ChampionDataSettings.Load(filePath);
            settings.Save(filePath);
            using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(filePath));

            Assert.Multiple(() =>
            {
                Assert.That(settings.PlatformId, Is.EqualTo("NA1"));
                Assert.That(settings.Locale, Is.EqualTo("en_US"));
                Assert.That(
                    typeof(ChampionDataSettings).GetProperty("SourceMode"),
                    Is.Null);
                Assert.That(saved.RootElement.TryGetProperty("SourceMode", out _), Is.False);
            });
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Test]
    public void ChampionDataSettings_LeagueClientRegionLocaleAlwaysBecomesFallback()
    {
        var settings = new ChampionDataSettings
        {
            PlatformId = "EUW1",
            Locale = "en_GB"
        };

        bool changed = settings.ApplyLeagueClientRegionLocale("na1", "fr-fr");
        bool unchanged = settings.ApplyLeagueClientRegionLocale("NA1", "fr_FR");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(unchanged, Is.False);
            Assert.That(settings.PlatformId, Is.EqualTo("NA1"));
            Assert.That(settings.Locale, Is.EqualTo("fr_FR"));
        });
    }

    [TestCase(ChampionCatalogSourceIds.LeagueClient, 11, false)]
    [TestCase(ChampionCatalogSourceIds.LeagueClient, 12, true)]
    [TestCase("16.16.1", 1, true)]
    public void LeagueClientRefreshDue_UsesSourceAwareTwelveHourWindow(
        string sourceVersion,
        int hoursSinceSync,
        bool expected)
    {
        DateTime now = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var syncInfo = new ChampionCatalogSyncInfo(
            sourceVersion,
            "en_US",
            170,
            "champions.json",
            now.AddHours(-hoursSinceSync));

        Assert.That(
            ChampionDataSourcePolicy.IsLeagueClientRefreshDue(syncInfo, now),
            Is.EqualTo(expected));
    }

    [Test]
    public void LeagueClientRefreshDue_LanguageChangeBypassesTwelveHourWindow()
    {
        DateTime now = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var syncInfo = new ChampionCatalogSyncInfo(
            ChampionCatalogSourceIds.LeagueClient,
            "en_US",
            170,
            "champions.json",
            now.AddMinutes(-5));

        Assert.That(
            ChampionDataSourcePolicy.IsLeagueClientRefreshDue(syncInfo, now, "fr_FR"),
            Is.True);
        Assert.That(
            ChampionDataSourcePolicy.IsLeagueClientRefreshDue(syncInfo, now, "en_US"),
            Is.False);
    }

    [Test]
    public async Task LeagueClientCatalog_ReadsLocalSummaryAndFiltersModeVariants()
    {
        var state = new MockLeagueClientState();
        var logs = new ConcurrentQueue<string>();
        int port = GetFreePort();
        const string token = "champion-data-test";
        await using var server = new MockLeagueClientServer(state, port, token, logs.Enqueue);
        await server.StartAsync();

        string base64Token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token}"));
        using var http = new Lcu.LeagueClientHttp(
            new AuthModel(port.ToString(), base64Token),
            logs.Enqueue);
        var service = new LeagueClientChampionCatalogService(http);

        ChampionCatalogRemoteData result = await service.FetchLatestChampionCatalogAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.DataDragonVersion, Is.EqualTo(LeagueClientChampionCatalogService.LocalCatalogVersion));
            Assert.That(result.Locale, Is.EqualTo("en_US"));
            Assert.That(result.Champions, Is.Not.Empty);
            Assert.That(result.Champions.All(champion => champion.Key < 60000), Is.True);
            Assert.That(result.Champions.Any(champion => champion.Key == 60001), Is.False);
            Assert.That(result.Champions.Single(champion => champion.Key == 103).SupportsLeagueClassic, Is.True);
            Assert.That(result.Champions.Single(champion => champion.Key == 86).SupportsLeagueClassic, Is.True);
            Assert.That(result.Champions.Single(champion => champion.Key == 1).SupportsLeagueClassic, Is.False);
            Assert.That(logs.Any(line => line.Contains("/lol-game-data/assets/v1/champion-summary.json", StringComparison.Ordinal)), Is.True);
            Assert.That(logs.Any(line => line.Contains("/riotclient/region-locale", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task LeagueClientBinaryAssetRequest_UsesReportedTilePathAndAcceptsImageResponse()
    {
        var state = new MockLeagueClientState();
        var logs = new ConcurrentQueue<string>();
        int port = GetFreePort();
        const string token = "champion-image-test";
        await using var server = new MockLeagueClientServer(state, port, token, logs.Enqueue);
        await server.StartAsync();

        string base64Token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token}"));
        using var http = new Lcu.LeagueClientHttp(
            new AuthModel(port.ToString(), base64Token),
            logs.Enqueue);

        string detailsJson = await http.GetChampionDetailsAsync(1);
        using JsonDocument details = JsonDocument.Parse(detailsJson);
        string? tilePath = details.RootElement
            .GetProperty("skins")[0]
            .GetProperty("tilePath")
            .GetString();
        Assert.That(tilePath, Is.Not.Null.And.Contains("/ASSETS/Characters/"));

        byte[] result = await http.GetGameDataAssetAsync(tilePath!);

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Length.GreaterThanOrEqualTo(4));
            Assert.That(result[0], Is.EqualTo(0xFF));
            Assert.That(result[1], Is.EqualTo(0xD8));
            Assert.That(
                logs.Any(line => line.Contains(
                    tilePath!,
                    StringComparison.Ordinal)),
                Is.True);
        });
    }

    [Test]
    public void LeagueClientRegionLocale_UsesOnlyClientResponse()
    {
        LeagueClientRegionLocaleInfo result = LeagueClientRegionLocaleService.Parse(
            "{\"locale\":\"fr_FR\",\"region\":\"NA\",\"webLanguage\":\"fr\",\"webRegion\":\"na\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.PlatformId, Is.EqualTo("NA1"));
            Assert.That(result.Region, Is.EqualTo("NA"));
            Assert.That(result.Locale, Is.EqualTo("fr_FR"));
            Assert.That(result.WebLanguage, Is.EqualTo("fr"));
            Assert.That(result.WebRegion, Is.EqualTo("na"));
        });
    }

    [TestCase("EUW", "EUW1")]
    [TestCase("EUNE", "EUN1")]
    [TestCase("OCE", "OC1")]
    [TestCase("KR", "KR")]
    [TestCase("PBE1", "PBE1")]
    public void LeagueClientRegionLocale_MapsClientRegionToPlatform(
        string region,
        string expectedPlatformId)
    {
        LeagueClientRegionLocaleInfo result = LeagueClientRegionLocaleService.Parse(
            $"{{\"locale\":\"en_GB\",\"region\":\"{region}\"}}");

        Assert.That(result.PlatformId, Is.EqualTo(expectedPlatformId));
    }

    [TestCase("{\"locale\":\"fr_FR\"}")]
    [TestCase("{\"region\":\"NA\"}")]
    [TestCase("{\"locale\":\"not-a-locale\",\"region\":\"NA\"}")]
    public void LeagueClientRegionLocale_RejectsMissingOrInvalidClientValues(string json)
    {
        Assert.Throws<InvalidOperationException>(() => LeagueClientRegionLocaleService.Parse(json));
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
