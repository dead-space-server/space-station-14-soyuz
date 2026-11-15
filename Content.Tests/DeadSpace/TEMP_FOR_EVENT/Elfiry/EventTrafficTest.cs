// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using NUnit.Framework;

namespace Content.Tests.DeadSpace.TEMP_FOR_EVENT.Elfiry;

[TestFixture]
public sealed class EventTrafficTest
{
    [Test]
    public void TwoUnitsNeverBothYieldOrBothClaimPriority()
    {
        for (var first = 1; first <= 16; first++)
        for (var second = 1; second <= 16; second++)
        {
            if (first == second)
                continue;
            foreach (var moving in new[] { false, true })
            foreach (var otherMoving in new[] { false, true })
                Assert.That(EventTrafficPolicy.Yields(first, second, moving, otherMoving),
                    Is.Not.EqualTo(EventTrafficPolicy.Yields(second, first, otherMoving, moving)));
        }
    }

    [Test]
    public void EqualPriorityTrafficCannotFormYieldCycle()
    {
        for (var first = 1; first <= 12; first++)
        for (var second = 1; second <= 12; second++)
        for (var third = 1; third <= 12; third++)
        {
            if (first == second || first == third || second == third)
                continue;
            var cycle = EventTrafficPolicy.Yields(first, second, true, true) &&
                        EventTrafficPolicy.Yields(second, third, true, true) &&
                        EventTrafficPolicy.Yields(third, first, true, true);
            Assert.That(cycle, Is.False);
        }
    }

    [Test]
    public void OverlappingUnitsHaveOnlyOneUnitAllowedToAdvance()
    {
        for (var first = 1; first <= 16; first++)
        for (var second = 1; second <= 16; second++)
        {
            if (first == second)
                continue;
            Assert.That(EventTrafficPolicy.OverlapPriority(first, second, 0f),
                Is.Not.EqualTo(EventTrafficPolicy.OverlapPriority(second, first, 0f)));
        }
        Assert.That(EventTrafficPolicy.OverlapPriority(1, 2, 1f), Is.False);
    }

    [Test]
    public void FollowingQueueLeavesLeaderFreeAndUsesHysteresis()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EventTrafficPolicy.Blocks(-0.9f, 0f, false), Is.False);
            Assert.That(EventTrafficPolicy.Blocks(0.9f, 0f, false), Is.True);
            Assert.That(EventTrafficPolicy.Blocks(1.05f, 0f, false), Is.False);
            Assert.That(EventTrafficPolicy.Blocks(1.05f, 0f, true), Is.True);
            Assert.That(EventTrafficPolicy.Blocks(1.25f, 0f, true), Is.False);
            Assert.That(EventTrafficPolicy.Blocks(0.5f, 0.8f, true), Is.False);
        });
    }
}
