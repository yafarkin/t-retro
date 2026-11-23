using Domain;

namespace Domain.Tests;

public sealed class DiscussionQueueTests
{
    [Fact]
    public void QueueSortsVotesColumnsAndStableIdsAndKeepsZeroVotes()
    {
        var meetingId = Guid.NewGuid();
        var firstById = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondById = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var mostVotes = Guid.NewGuid();
        var otherColumn = Guid.NewGuid();
        var zeroVotes = Guid.NewGuid();

        var queue = DiscussionQueue.Create(
            meetingId,
            [
                new DiscussionRanking(secondById, 2, 0),
                new DiscussionRanking(otherColumn, 2, 1),
                new DiscussionRanking(firstById, 2, 0),
                new DiscussionRanking(mostVotes, 3, 2),
                new DiscussionRanking(zeroVotes, 0, 0),
            ]);

        Assert.Equal(5, queue.Count);
        Assert.Equal(mostVotes, queue[0].TopicId);
        Assert.Equal(firstById, queue[1].TopicId);
        Assert.Equal(secondById, queue[2].TopicId);
        Assert.Equal(otherColumn, queue[3].TopicId);
        Assert.Equal(zeroVotes, queue[4].TopicId);
        Assert.Equal(Enumerable.Range(0, 5), queue.Select(topic => topic.Position));
        Assert.All(queue, topic => Assert.Equal(DiscussionStatus.Pending, topic.Status));
    }

    [Fact]
    public void EmptyQueueHasNoTopics()
    {
        Assert.Empty(DiscussionQueue.Create(Guid.NewGuid(), []));
    }
}
