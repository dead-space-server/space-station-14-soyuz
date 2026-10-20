// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Defines the reward budget for terminal repair order results.
/// </summary>
public static class RepairOrderRewardBudget
{
    public static int ForSuccessfulCompletion(int currentPoints)
    {
        return currentPoints;
    }

    public static int ForExpiration(int currentPoints)
    {
        return Math.Max(0, (int) Math.Floor(currentPoints * 0.5d));
    }
}
