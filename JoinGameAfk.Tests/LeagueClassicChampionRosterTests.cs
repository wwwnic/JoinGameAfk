using JoinGameAfk.Model;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class LeagueClassicChampionRosterTests
{
    [Test]
    public void BundledRoster_MatchesRiotClassicCatalogSize()
    {
        Assert.That(LeagueClassicChampionRoster.Bundled, Has.Count.EqualTo(63));
    }

    [TestCase(103, true)]
    [TestCase(60103, true)]
    [TestCase(86, true)]
    [TestCase(266, false)]
    public void Contains_NormalizesModeChampionIds(int championId, bool expected)
    {
        Assert.That(LeagueClassicChampionRoster.Contains(championId), Is.EqualTo(expected));
    }
}
