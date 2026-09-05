using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Converts a final repair score into a weighted, budget-bounded declarative reward result.
/// Physical delivery consumes this immutable result as the completed-order snapshot.
/// </summary>
public sealed class RepairOrderRewardSystem : EntitySystem
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("repair_orders");
    }

    public List<RepairOrderRewardResult> GenerateRewards(RepairOrderPrototype order, int budget)
    {
        var result = new List<RepairOrderRewardResult>();
        if (budget <= 0)
            return result;

        if (!_prototype.TryIndex<RepairRewardPoolPrototype>(order.RewardPool, out var pool))
        {
            _sawmill.Warning(
                $"Repair order {order.ID} references missing reward pool {order.RewardPool}; no rewards were calculated.");
            return result;
        }

        var candidates = new List<RewardCandidate>();
        foreach (var rewardId in pool.Rewards)
        {
            if (!_prototype.TryIndex<RepairRewardPrototype>(rewardId, out var reward))
            {
                _sawmill.Warning(
                    $"Repair reward pool {pool.ID} references missing reward {rewardId}; the candidate is ignored.");
                continue;
            }

            if (reward.Cost <= 0 || reward.Weight <= 0f || reward.MaxCount <= 0)
            {
                _sawmill.Warning(
                    $"Repair reward {reward.ID} has non-positive cost, weight, or maxCount; the candidate is ignored.");
                continue;
            }

            candidates.Add(new RewardCandidate(rewardId, reward));
        }

        var eligible = candidates
            .Where(candidate => candidate.Prototype.MinimumDifficulty <= order.Difficulty)
            .ToList();
        if (eligible.Count == 0)
            return result;

        // Expand bounded candidates into individual units, then randomize their priority with an
        // exponential weighted key. The bounded knapsack below always spends the largest reachable
        // part of the budget; weights only choose between equally well-spending reward combinations.
        // MaxCount therefore prevents a large order from degenerating into dozens of cheap material stacks.
        var units = new List<RewardUnit>();
        for (var candidateIndex = 0; candidateIndex < eligible.Count; candidateIndex++)
        {
            var candidate = eligible[candidateIndex];
            for (var count = 0; count < candidate.Prototype.MaxCount; count++)
            {
                var roll = Math.Max(_random.NextDouble(), double.Epsilon);
                var priority = -Math.Log(roll) / candidate.Prototype.Weight;
                units.Add(new RewardUnit(candidateIndex, candidate.Prototype.Cost, priority));
            }
        }

        units.Sort((left, right) => left.Priority.CompareTo(right.Priority));

        var reachable = new bool[budget + 1];
        var previousBudget = new int[budget + 1];
        var previousUnit = new int[budget + 1];
        Array.Fill(previousBudget, -1);
        Array.Fill(previousUnit, -1);
        reachable[0] = true;

        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            var unit = units[unitIndex];
            for (var spent = budget - unit.Cost; spent >= 0; spent--)
            {
                var newSpent = spent + unit.Cost;
                if (!reachable[spent] || reachable[newSpent])
                    continue;

                reachable[newSpent] = true;
                previousBudget[newSpent] = spent;
                previousUnit[newSpent] = unitIndex;
            }
        }

        var bestSpent = budget;
        while (bestSpent > 0 && !reachable[bestSpent])
            bestSpent--;

        var counts = new Dictionary<ProtoId<RepairRewardPrototype>, int>();
        for (var spent = bestSpent; spent > 0; spent = previousBudget[spent])
        {
            var unitIndex = previousUnit[spent];
            if (unitIndex < 0)
                break;

            var selected = eligible[units[unitIndex].CandidateIndex];
            counts.TryGetValue(selected.Id, out var selectedCount);
            counts[selected.Id] = selectedCount + 1;
        }

        if (bestSpent < budget)
        {
            _sawmill.Debug(
                $"Reward pool {pool.ID} spent the maximum reachable {bestSpent}/{budget} points for order {order.ID}; " +
                $"the remaining {budget - bestSpent} points cannot buy another reward within configured limits.");
        }

        // Preserve pool order in the UI and runtime snapshot, independent of random selection order.
        foreach (var candidate in candidates)
        {
            if (counts.Remove(candidate.Id, out var count))
                result.Add(new RepairOrderRewardResult(candidate.Id, count));
        }

        return result;
    }

    private readonly record struct RewardCandidate(
        ProtoId<RepairRewardPrototype> Id,
        RepairRewardPrototype Prototype);

    private readonly record struct RewardUnit(int CandidateIndex, int Cost, double Priority);
}
