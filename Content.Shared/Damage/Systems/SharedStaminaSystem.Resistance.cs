using Content.Shared.Armor;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared.Damage.Systems;

public partial class SharedStaminaSystem
{
    // DS14-start: apply armor "Stun" coefficients to stamina damage (stun resistance on armor)
    private static readonly ProtoId<DamageTypePrototype> StunArmorDamageType = "Stun";
    // DS14-end

    private void InitializeResistance()
    {
        SubscribeLocalEvent<StaminaResistanceComponent, BeforeStaminaDamageEvent>(OnGetResistance);
        SubscribeLocalEvent<StaminaResistanceComponent, InventoryRelayedEvent<BeforeStaminaDamageEvent>>(RelayedResistance);
        SubscribeLocalEvent<StaminaResistanceComponent, ArmorExamineEvent>(OnArmorExamine);
        // DS14
        SubscribeLocalEvent<StaminaComponent, BeforeStaminaDamageEvent>(OnGetArmorResistance);
    }

    private void OnGetResistance(Entity<StaminaResistanceComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        args.Value *= ent.Comp.DamageCoefficient;
    }

    private void RelayedResistance(Entity<StaminaResistanceComponent> ent, ref InventoryRelayedEvent<BeforeStaminaDamageEvent> args)
    {
        if (ent.Comp.Worn)
            OnGetResistance(ent, ref args.Args);
    }

    private void OnArmorExamine(Entity<StaminaResistanceComponent> ent, ref ArmorExamineEvent args)
    {
        var value = MathF.Round((1f - ent.Comp.DamageCoefficient) * 100, 1);

        if (value == 0)
            return;

        args.Msg.PushNewline();
        args.Msg.AddMarkupOrThrow(Loc.GetString(ent.Comp.Examine, ("value", value)));
    }

    // DS14-start
    private void OnGetArmorResistance(Entity<StaminaComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        var coeffQuery = new CoefficientQueryEvent(~SlotFlags.POCKET);
        RaiseLocalEvent(ent.Owner, coeffQuery);

        if (coeffQuery.DamageModifiers.Coefficients.TryGetValue(StunArmorDamageType, out var coefficient))
            args.Value *= coefficient;
    }
    // DS14-end
}
