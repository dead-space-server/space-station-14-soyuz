using Content.Shared.DeadSpace.Soyuz.IpcPower;
using Content.Shared.Inventory;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;

namespace Content.Server.DeadSpace.Soyuz.IpcPower;

public sealed class IpcPowerSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;

    private const string PortableRechargerProto = "PortableRecharger";
    private const string RoboBurgerProto = "FoodBurgerRobot";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EdibleComponent, IngestedEvent>(OnIngested);
    }

    private void OnIngested(Entity<EdibleComponent> ent, ref IngestedEvent args)
    {
        if (MetaData(ent).EntityPrototype?.ID != RoboBurgerProto)
            return;

        if (!TryComp<IpcPowerComponent>(args.Target, out _) ||
            !TryComp<BatteryComponent>(args.Target, out var battery))
            return;

        var chargeAmount = battery.MaxCharge * 0.5f;
        _battery.ChangeCharge((args.Target, battery), chargeAmount);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<IpcPowerComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out var ipcPower, out var battery))
        {
            var delta = -ipcPower.PassiveDrainRate * frameTime;

            if (_inventory.TryGetSlotEntity(uid, "back", out var backItem) &&
                MetaData(backItem.Value).EntityPrototype?.ID == PortableRechargerProto)
            {
                delta += ipcPower.BackRechargerRate * frameTime;
            }

            if (delta != 0f)
                _battery.ChangeCharge((uid, battery), delta);
        }
    }
}
