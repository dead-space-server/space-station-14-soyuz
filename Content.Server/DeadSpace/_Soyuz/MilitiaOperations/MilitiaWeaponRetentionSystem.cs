// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.Hands.Systems;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Standing;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed class MilitiaWeaponRetentionSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InventoryComponent, DropHandItemsEvent>(OnDropItems, before: [typeof(HandsSystem)]);
    }

    private void OnDropItems(Entity<InventoryComponent> wearer, ref DropHandItemsEvent args)
    {
        if (TryComp<MobStateComponent>(wearer, out var state) && state.CurrentState is MobState.Alive or MobState.PreCritical &&
            _inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit, wearer.Comp) &&
            !TerminatingOrDeleted(suit.Value) && HasComp<MilitiaWeaponRetentionComponent>(suit.Value))
            args.Cancelled = true;
    }
}
