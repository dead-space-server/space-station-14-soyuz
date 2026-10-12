using Content.Server.Doors.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.DeadSpace.Sectants;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantFemaleHackSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DoorSystem _door = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _entitySet = new();
    public override void Initialize()
    {
        SubscribeLocalEvent<SectantFemaleHackActionEvent>(OnHack);
    }

    private void OnHack(SectantFemaleHackActionEvent args)
    {
        var xform = Transform(args.Performer);
        _entitySet.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, 3f, _entitySet);

        foreach (var ent in _entitySet)
        {
            if (!TryComp<DoorComponent>(ent, out var door))
                continue;

            var hacked = EnsureComp<SectantHackedComponent>(ent);
            hacked.RevertAt = _timing.CurTime + TimeSpan.FromSeconds(20);
            _door.StartOpening(ent, door, args.Performer);
        }

        args.Handled = true;
    }
}
