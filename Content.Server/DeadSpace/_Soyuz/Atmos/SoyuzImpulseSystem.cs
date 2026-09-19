// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared.Atmos;

namespace Content.Server.DeadSpace._Soyuz.Atmos;

public sealed class SoyuzImpulseSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;

    private readonly Dictionary<EntityUid, PowerSupplierComponent> _suppliers = new();
    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<PowerSupplierComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, PowerSupplierComponent component, ComponentStartup args)
    {
        _suppliers[uid] = component;
    }

    // Called by PowerNetSystem's existing shutdown subscription, before freeing the network supply.
    public void RemoveSupplier(EntityUid uid, PowerSupplierComponent component)
    {
        _suppliers.Remove(uid);
        if (component.SoyuzOutputMultiplier != 1f)
            component.SetSoyuzOutputMultiplier(1f);
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 0.5f)
            return;
        _elapsed = 0f;

        foreach (var (uid, supplier) in _suppliers)
        {
            var multiplier = 1f;
            if (TryComp(uid, out TransformComponent? xform) &&
                xform.GridUid != null && xform.ParentUid == xform.GridUid &&
                _atmos.GetTileMixture((uid, xform))?.GetMoles(Gas.ImpulseGas) >= 5f)
                multiplier = 1.25f;

            if (supplier.SoyuzOutputMultiplier != multiplier)
                supplier.SetSoyuzOutputMultiplier(multiplier);
        }
    }
}
