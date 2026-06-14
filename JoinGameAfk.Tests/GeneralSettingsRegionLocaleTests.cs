using JoinGameAfk.Model;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class GeneralSettingsRegionLocaleTests
{
    [TestCase("euw1", "EUW1")]
    [TestCase(" NA1 ", "NA1")]
    [TestCase("new-region-9", "NEW-REGION-9")]
    public void NormalizePlatformId_UsesLeaguePlatformFormat(string input, string expected)
    {
        Assert.That(RegionLocale.NormalizePlatformId(input), Is.EqualTo(expected));
    }

    [TestCase("NA1", "na")]
    [TestCase("BR1", "br")]
    [TestCase("EUN1", "eune")]
    [TestCase("EUW1", "euw")]
    [TestCase("JP1", "jp")]
    [TestCase("LA1", "lan")]
    [TestCase("LA2", "las")]
    [TestCase("ME1", "me")]
    [TestCase("OC1", "oce")]
    [TestCase("PBE1", "pbe")]
    [TestCase("PH2", "ph")]
    [TestCase("RU", "ru")]
    [TestCase("SG2", "sg")]
    [TestCase("TH2", "th")]
    [TestCase("TR1", "tr")]
    [TestCase("TW2", "tw")]
    [TestCase("VN2", "vn")]
    public void TryGetDataDragonRealm_MapsLeaguePlatform(string platformId, string expectedRealm)
    {
        bool mapped = RegionLocale.TryGetDataDragonRealm(platformId, out string realm);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.True);
            Assert.That(realm, Is.EqualTo(expectedRealm));
        });
    }

    [Test]
    public void TryGetDataDragonRealm_AllowsUnknownPlatformToUseFallback()
    {
        bool mapped = RegionLocale.TryGetDataDragonRealm("NEW1", out string realm);

        Assert.Multiple(() =>
        {
            Assert.That(mapped, Is.False);
            Assert.That(realm, Is.Empty);
        });
    }

    [TestCase("fr-fr", "fr_FR")]
    [TestCase(" EN_us ", "en_US")]
    [TestCase("zh-Hans-cn", "zh_Hans_CN")]
    [TestCase("invalid", "en_US")]
    public void NormalizeLocale_UsesDataDragonLocaleFormat(string input, string expected)
    {
        Assert.That(RegionLocale.NormalizeLocale(input), Is.EqualTo(expected));
    }
}
