// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Tests.DeadSpace.TEMP_FOR_EVENT.Elfiry;

[TestFixture]
public sealed class EventNavigationTest
{
    [Test]
    public void NearbyGoalsWithDifferentPurposesDoNotReuseRoutes()
    {
        var position = new EntityCoordinates(new EntityUid(1), Vector2.Zero);
        var command = new EventMovementTarget(EventMovementKind.Command, position);
        var medic = new EventMovementTarget(EventMovementKind.Medicine, position, new EntityUid(2));
        var passage = new EventPassageYield(new EntityUid(3), 1, 1, position, [], [],
            new MapCoordinates(Vector2.Zero, new MapId(1)), Vector2.UnitX, TimeSpan.FromSeconds(10));
        var yielding = new EventMovementTarget(EventMovementKind.Passage, position, passage.Passer, passage);
        Assert.Multiple(() =>
        {
            Assert.That(command.Matches(medic), Is.False);
            Assert.That(command.Matches(yielding), Is.False);
            Assert.That(medic.Matches(yielding), Is.False);
            Assert.That(medic.Matches(medic with { Subject = new EntityUid(4) }), Is.False);
            Assert.That(yielding.Matches(yielding with { Passage = passage with { Until = TimeSpan.FromSeconds(20) } }), Is.False);
            Assert.That(command.Matches(command with { Destination = new EntityCoordinates(new EntityUid(5), Vector2.Zero) }), Is.False);
            Assert.That(command.Matches(command), Is.True);
        });
    }

    [Test]
    public void SearchRetainsFrontierAndFindsRouteInitiallyLeadingAwayFromGoal()
    {
        var start = new Vector2i(2, 0);
        var goal = new Vector2i(2, 2);
        var search = Search(start, goal);
        bool Walkable(Vector2i p) => p.X >= 0 && p.X <= 5 && p.Y >= 0 && p.Y <= 3 && !(p.Y == 1 && p.X < 5);
        var result = EventSearchResult.Pending;
        var slices = 0;
        while (result == EventSearchResult.Pending && slices++ < 1000)
        {
            Assert.That(search.Reconstruct(), Is.Null);
            result = search.Advance(node => node == goal, (from, to) => Walkable(from) && Walkable(to),
                node => Distance(node, goal), 1f, budget: 1);
        }
        Assert.That(result, Is.EqualTo(EventSearchResult.Found));
        var route = search.Reconstruct()!;
        Assert.Multiple(() =>
        {
            Assert.That(slices, Is.GreaterThan(1));
            Assert.That(route[0], Is.EqualTo(start));
            Assert.That(route[^1], Is.EqualTo(goal));
            Assert.That(Distance(route[1], goal), Is.GreaterThan(Distance(start, goal)));
            Assert.That(route.All(Walkable), Is.True);
            Assert.That(route.Zip(route.Skip(1)).All(pair => Distance(pair.First, pair.Second) == 1f), Is.True);
        });
    }

    [Test]
    public void UnreachableGoalDoesNotProducePartialRouteIntoDeadEnd()
    {
        var start = new Vector2i(0, 0);
        var goal = new Vector2i(3, 0);
        var search = Search(start, goal);
        var result = search.Advance(node => node == goal,
            (_, to) => to.Y == 0 && to.X >= 0 && to.X <= 1, node => Distance(node, goal), 1f);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(EventSearchResult.Blocked));
            Assert.That(search.Reconstruct(), Is.Null);
        });
    }

    [TestCase("prepare")]
    [TestCase("verify")]
    [TestCase("commit")]
    [TestCase("activate")]
    public void FailedGroupOrderRestoresEveryPreviousOrder(string failureStage)
    {
        var values = new[] { 11, 22, 33 };
        var original = values.ToArray();
        var activated = new List<int>();
        var restored = new List<int>();
        var thrown = false;
        void Fail(string stage, int index)
        {
            if (!thrown && stage == failureStage && index == 1)
            {
                thrown = true;
                throw new InvalidOperationException(stage);
            }
        }
        var recovered = new List<Exception>();
        var result = EventOrderTransaction.Execute(new[] { 0, 1, 2 },
            i => Fail("prepare", i),
            () => Fail("verify", 1),
            i => { values[i] = 100 + i; Fail("commit", i); },
            i => { activated.Add(i); Fail("activate", i); },
            i => { values[i] = original[i]; restored.Add(i); }, recovered.Add, out var error);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(values, Is.EqualTo(original));
            Assert.That(restored, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(activated, Is.EqualTo(failureStage == "activate" ? new[] { 0, 1 } : Array.Empty<int>()));
            Assert.That(recovered, Is.Empty);
        });
    }

    [Test]
    public void GroupActivationOnlySeesFullyCommittedOrders()
    {
        var values = new[] { 0, 0, 0 };
        var result = EventOrderTransaction.Execute(new[] { 0, 1, 2 }, _ => { }, () => { },
            i => values[i] = 1, _ => Assert.That(values, Is.All.EqualTo(1)), i => values[i] = 0,
            _ => Assert.Fail("Unexpected recovery failure"), out var error);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(error, Is.Null);
        });
    }

    private static EventWalkSearch Search(Vector2i start, Vector2i goal)
    {
        var grid = new EntityUid(1);
        var map = new MapId(1);
        var position = new Vector2(goal.X, goal.Y);
        return new EventWalkSearch(grid, map, 1,
            new EventMovementTarget(EventMovementKind.Command, new EntityCoordinates(grid, position)),
            new MapCoordinates(position, map), Vector2.Zero, Angle.Zero, start, TimeSpan.FromMinutes(1));
    }

    private static float Distance(Vector2i first, Vector2i second)
        => new Vector2(first.X - second.X, first.Y - second.Y).Length();
}
