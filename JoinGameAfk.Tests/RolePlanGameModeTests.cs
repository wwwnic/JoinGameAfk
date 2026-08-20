using System.Collections.Generic;
using JoinGameAfk.Enums;
using JoinGameAfk.Model;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class RolePlanGameModeTests
{
    [Test]
    public void Preferences_AreIndependentAcrossGameModes()
    {
        var settings = new RolePlanSettings();
        settings.Preferences[Position.Mid].PickChampionIds = [84];
        settings.ClassicPreferences[Position.Mid].PickChampionIds = [103];

        Assert.Multiple(() =>
        {
            Assert.That(
                settings.GetMergedPickChampionIds(Position.Mid, LeagueGameMode.Modern),
                Is.EqualTo(new[] { 84 }));
            Assert.That(
                settings.GetMergedPickChampionIds(Position.Mid, LeagueGameMode.Classic),
                Is.EqualTo(new[] { 103 }));
        });
    }

    [Test]
    public void ReplacePreferences_OnlyReplacesRequestedMode()
    {
        var settings = new RolePlanSettings();
        settings.Preferences[Position.Top].BanChampionIds = [266];

        settings.ReplacePreferences(
            new Dictionary<Position, PositionPreference>
            {
                [Position.Top] = new() { BanChampionIds = [86] }
            },
            LeagueGameMode.Classic);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Preferences[Position.Top].BanChampionIds, Is.EqualTo(new[] { 266 }));
            Assert.That(settings.ClassicPreferences[Position.Top].BanChampionIds, Is.EqualTo(new[] { 86 }));
        });
    }
}
