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
            Id = "Renata"
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
        JsonElement champion = document.RootElement
            .GetProperty("Champions")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("Key").GetInt32() == expectedKey);

        Assert.Multiple(() =>
        {
            Assert.That(champion.GetProperty("Name").GetString(), Is.EqualTo(expectedName));
            Assert.That(champion.GetProperty("Id").GetString(), Is.EqualTo(expectedId));
            Assert.That(champion.TryGetProperty("AssetId", out _), Is.False);
        });
    }
}
