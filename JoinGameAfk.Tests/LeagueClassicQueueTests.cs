using JoinGameAfk.Enums;
using JoinGameAfk.Tools.MockLeagueClient;
using NUnit.Framework;

namespace JoinGameAfk.Tests;

[TestFixture]
public class LeagueClassicQueueTests
{
    [TestCase((int)LeagueQueueId.LeagueClassicPvpDraft, "League Classic")]
    [TestCase((int)LeagueQueueId.LeagueClassicCustomDraft, "League Classic Custom Draft")]
    public void UpdateQueue_LeagueClassicDraft_UsesDraftPickFlow(int queueId, string queueName)
    {
        var state = new MockLeagueClientState();
        state.UpdateQueueMode(MockQueueMode.BlindPick);

        state.UpdateQueue(queueId, queueName);

        MockLeagueClientSnapshot snapshot = state.GetSnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.QueueId, Is.EqualTo(queueId));
            Assert.That(snapshot.QueueName, Is.EqualTo(queueName));
            Assert.That(snapshot.QueueMode, Is.EqualTo(MockQueueMode.DraftPick));
        });
    }
}
