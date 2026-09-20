// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairTechnicalExclusionTest
{
    [TestCase(0, 1000)]
    [TestCase(1, 990)]
    [TestCase(2, 980)]
    [TestCase(3, 970)]
    [TestCase(5, 950)]
    [TestCase(10, 900)]
    [TestCase(100, 0)]
    [TestCase(101, 0)]
    public void LinearPenaltyAndRewardBudget(int count, int expected)
    {
        var totals = new RepairExclusionTotals(count, count * 2, 500, 1000);

        Assert.That(totals.PenaltyPercent, Is.EqualTo(count));
        Assert.That(totals.FinalPoints, Is.EqualTo(expected));
        Assert.That(
            RepairOrderRewardBudget.ForSuccessfulCompletion(totals.FinalPoints),
            Is.EqualTo(expected));
        Assert.That(
            RepairOrderRewardBudget.ForExpiration(totals.FinalPoints),
            Is.EqualTo(expected / 2));
    }

    [TestCase(800, 3, 776)]
    [TestCase(820, 4, 787)]
    public void PenaltyUsesExactIntegerFloor(int raw, int count, int expected)
        => Assert.That(
            RepairTechnicalExclusion.FinalPoints(raw, count),
            Is.EqualTo(expected));

    [Test]
    public void CostCapIsIndependentFromCount()
    {
        var max = RepairTechnicalExclusion.MaxWaivedPoints(1001);

        Assert.That(max, Is.EqualTo(500));
        Assert.That(RepairTechnicalExclusion.CanAdd(0, max, 100), Is.True);
        Assert.That(RepairTechnicalExclusion.CanAdd(450, max, 50), Is.True);
        Assert.That(RepairTechnicalExclusion.CanAdd(450, max, 51), Is.False);
        Assert.That(RepairTechnicalExclusion.CanAdd(0, max, 0), Is.False);
        Assert.That(RepairTechnicalExclusion.CanAdd(int.MaxValue, max, 1), Is.False);

        Assert.That(
            new RepairExclusionTotals(1, 500, max, 1000).FinalPoints,
            Is.EqualTo(990));

        Assert.That(
            new RepairExclusionTotals(10, 100, max, 1000).FinalPoints,
            Is.EqualTo(900));
    }
}
