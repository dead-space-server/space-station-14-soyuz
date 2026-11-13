// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;

namespace Content.Shared.Chemistry.EntitySystems;

public sealed partial class InjectorSystem
{
    public bool TrySetTransferAmount(Entity<InjectorComponent> injector, FixedPoint2 amount)
    {
        if (!_prototypeManager.Resolve(injector.Comp.ActiveModeProtoId, out var mode) || !mode.TransferAmounts.Contains(amount))
            return false;
        if (injector.Comp.CurrentTransferAmount != amount)
        {
            injector.Comp.CurrentTransferAmount = amount;
            Dirty(injector);
        }
        return true;
    }
}
