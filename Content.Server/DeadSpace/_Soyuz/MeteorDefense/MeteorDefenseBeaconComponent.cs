// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

namespace Content.Server.DeadSpace._Soyuz.MeteorDefense;

[RegisterComponent, Access(typeof(MeteorDefenseSystem))]
public sealed partial class MeteorDefenseBeaconComponent : Component
{
    [DataField(required: true)]
    public float MaxAllowedCharge;

    [DataField(required: true)]
    public float EnergyPerIntercept;

    // The enabled station is also the enabled state. Revalidated against current ownership on every use.
    [ViewVariables]
    public EntityUid? EnabledStation;
}
