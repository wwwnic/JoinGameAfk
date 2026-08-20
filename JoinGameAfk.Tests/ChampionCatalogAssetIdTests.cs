using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using JoinGameAfk.Model;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class ChampionCatalogSchemaTests
{
    [Test]
    public void ChampionInfo_RoundTripsCorrectRiotFieldNames()
    {
        var champion = new ChampionInfo(888, "Renata Glasc")
        {
            EnglishName = "Renata Glasc",
            Id = "Renata",
            SupportsLeagueClassic = true
        };

        string json = JsonSerializer.Serialize(champion);
        ChampionInfo? restored = JsonSerializer.Deserialize<ChampionInfo>(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"Key\":888"));
            Assert.That(json, Does.Contain("\"Id\":\"Renata\""));
            Assert.That(json, Does.Not.Contain("AssetId"));
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Key, Is.EqualTo(888));
            Assert.That(restored.Name, Is.EqualTo("Renata Glasc"));
            Assert.That(restored.EnglishName, Is.EqualTo("Renata Glasc"));
            Assert.That(restored.Id, Is.EqualTo("Renata"));
            Assert.That(restored.SupportsLeagueClassic, Is.True);
        });
    }

    [Test]
    public void LegacyChampionInfo_MigratesNumericIdAndAssetId()
    {
        const string json = """{"Id":20,"Name":"Nunu & Willump","AssetId":"Nunu","Roles":[]}""";

        ChampionInfo? restored = JsonSerializer.Deserialize<ChampionInfo>(json);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Key, Is.EqualTo(20));
            Assert.That(restored.Id, Is.EqualTo("Nunu"));
        });
    }

    [Test]
    public void LegacyChampionInfo_WithoutAssetIdStillLoads()
    {
        const string json = """{"Id":62,"Name":"Wukong","Roles":[]}""";

        ChampionInfo? restored = JsonSerializer.Deserialize<ChampionInfo>(json);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Key, Is.EqualTo(62));
            Assert.That(restored.Id, Is.Null);
        });
    }

    [TestCase(20, "Nunu & Willump", "Nunu")]
    [TestCase(62, "Wukong", "MonkeyKing")]
    [TestCase(888, "Renata Glasc", "Renata")]
    public void BundledCatalog_UsesRiotKeyNameAndId(int expectedKey, string expectedName, string expectedId)
    {
        using Stream? stream = typeof(ChampionInfo).Assembly
            .GetManifestResourceStream("JoinGameAfk.Assets.champions.json");
        Assert.That(stream, Is.Not.Null);

        using JsonDocument document = JsonDocument.Parse(stream!);
        JsonElement root = document.RootElement;
        JsonElement champion = root
            .GetProperty("Champions")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("Key").GetInt32() == expectedKey);

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("Version").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("Locale").GetString(), Is.EqualTo("en_US"));
            Assert.That(champion.GetProperty("Name").GetString(), Is.EqualTo(expectedName));
            Assert.That(champion.GetProperty("Id").GetString(), Is.EqualTo(expectedId));
            Assert.That(champion.TryGetProperty("AssetId", out _), Is.False);
        });
    }

    [Test]
    public void RefreshFileFromDataDragon_WritesTargetCatalogAndPreservesKnownRoles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"JoinGameAfkCatalogTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string catalogFilePath = Path.Combine(tempDirectory, "champions.json");

        try
        {
            File.WriteAllText(
                catalogFilePath,
                """
                {
                  "Version": 2,
                  "DataDragonVersion": "1.0.0",
                  "Locale": "en_US",
                  "Champions": [
                    {
                      "Key": 103,
                      "Name": "Ahri",
                      "Id": "Ahri",
                      "Roles": [ "Support" ]
                    }
                  ]
                }
                """);

            var remoteCatalog = new ChampionCatalogRemoteData(
                "99.1.1",
                "en_US",
                [
                    new ChampionCatalogRemoteChampion(103, "Ahri", "Ahri", "Ahri"),
                    new ChampionCatalogRemoteChampion(999, "New Champ", "New Champ", "NewChamp")
                ]);

            var result = ChampionCatalog.RefreshFileFromDataDragon(remoteCatalog, catalogFilePath);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogFilePath));
            JsonElement root = document.RootElement;
            JsonElement[] champions = root.GetProperty("Champions").EnumerateArray().ToArray();
            JsonElement ahri = champions.Single(champion => champion.GetProperty("Key").GetInt32() == 103);
            JsonElement newChampion = champions.Single(champion => champion.GetProperty("Key").GetInt32() == 999);

            Assert.Multiple(() =>
            {
                Assert.That(result.FilePath, Is.EqualTo(catalogFilePath));
                Assert.That(result.DataDragonVersion, Is.EqualTo("99.1.1"));
                Assert.That(result.ChampionCount, Is.EqualTo(2));
                Assert.That(root.GetProperty("DataDragonVersion").GetString(), Is.EqualTo("99.1.1"));
                Assert.That(
                    ahri.GetProperty("Roles").EnumerateArray().Select(role => role.GetString()),
                    Is.EquivalentTo(new[] { "Support" }));
                Assert.That(newChampion.GetProperty("Name").GetString(), Is.EqualTo("New Champ"));
                Assert.That(newChampion.GetProperty("Id").GetString(), Is.EqualTo("NewChamp"));
                Assert.That(ahri.GetProperty("SupportsLeagueClassic").GetBoolean(), Is.True);
                Assert.That(newChampion.GetProperty("SupportsLeagueClassic").GetBoolean(), Is.False);
            });
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
