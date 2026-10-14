// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.GameTicking;

namespace Content.Client.DeadSpace._Soyuz.RepairOrders;

public sealed class RepairOrderShopSystem : EntitySystem
{
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), RepairOrderShopDraft> _drafts = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => _drafts.Clear());
    }

    public RepairOrderShopDraft GetDraft(EntityUid console, EntityUid actor)
    {
        var key = (console, actor);
        if (!_drafts.TryGetValue(key, out var draft))
            _drafts.Add(key, draft = new RepairOrderShopDraft());
        return draft;
    }

    public override void Shutdown()
    {
        _drafts.Clear();
        base.Shutdown();
    }
}

public sealed class RepairOrderShopDraft
{
    public readonly Dictionary<string, int> Cart = new();
    public string? RewardPoolId;
    public string Search = string.Empty;
    public int Tab;
    public bool Compact = true;
    public string? PendingRequestId;
    public List<RepairOrderRewardBuiEntry>? PendingLines;
}
