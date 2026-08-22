using LcuClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class LeagueClientEventStreamTests
{
    private static readonly string[] ExpectedEventNames =
    [
        "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",
        "OnJsonApiEvent_lol-gameflow_v1_session",
        "OnJsonApiEvent_lol-lobby_v2_lobby",
        "OnJsonApiEvent_lol-matchmaking_v1_ready-check",
        "OnJsonApiEvent_lol-champ-select_v1_session"
    ];

    [Test]
    public void EventNames_ContainOnlyChampionFlowTopics()
    {
        Assert.That(Lcu.LeagueClientEventStream.EventNames, Is.EqualTo(ExpectedEventNames));
        Assert.That(Lcu.LeagueClientEventStream.EventNames, Does.Not.Contain("OnJsonApiEvent"));
    }

    [TestCaseSource(nameof(ExpectedEventNames))]
    public void TryParseJsonApiEvent_FocusedTopic_ParsesPayload(string eventName)
    {
        string message =
            $"[8,\"{eventName}\",{{\"data\":{{\"phase\":\"ChampSelect\"}},\"eventType\":\"Update\",\"uri\":\"/lol-gameflow/v1/session\"}}]";

        bool parsed = Lcu.LeagueClientEventStream.TryParseJsonApiEvent(message, out var apiEvent);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(apiEvent.Uri, Is.EqualTo("/lol-gameflow/v1/session"));
            Assert.That(apiEvent.EventType, Is.EqualTo("Update"));
            Assert.That(apiEvent.DataJson, Is.EqualTo("{\"phase\":\"ChampSelect\"}"));
        });
    }

    [TestCase("OnJsonApiEvent")]
    [TestCase("OnJsonApiEvent_lol-champ-select_v1_skin-carousel-skins")]
    [TestCase("UnrelatedEvent")]
    public void TryParseJsonApiEvent_UnsubscribedTopic_IsIgnored(string eventName)
    {
        string message =
            $"[8,\"{eventName}\",{{\"data\":{{}},\"eventType\":\"Update\",\"uri\":\"/ignored\"}}]";

        bool parsed = Lcu.LeagueClientEventStream.TryParseJsonApiEvent(message, out _);

        Assert.That(parsed, Is.False);
    }
}
