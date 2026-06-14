using JoinGameAfk.Model;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class LeagueClientApiRegionPolicyTests
{
    [TestCase("KR")]
    [TestCase("kr")]
    [TestCase(" KR ")]
    public void IsRestricted_ReturnsTrueForKorea(string platformId)
    {
        Assert.That(LeagueClientApiRegionPolicy.IsRestricted(platformId), Is.True);
    }

    [TestCase("NA1")]
    [TestCase("EUW1")]
    [TestCase("JP1")]
    [TestCase("PBE1")]
    [TestCase("unknown-region")]
    [TestCase(null)]
    public void IsRestricted_ReturnsFalseForOtherOrMissingPlatforms(string? platformId)
    {
        Assert.That(LeagueClientApiRegionPolicy.IsRestricted(platformId), Is.False);
    }
}
