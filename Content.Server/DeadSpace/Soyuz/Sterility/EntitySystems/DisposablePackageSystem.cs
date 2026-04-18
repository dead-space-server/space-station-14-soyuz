using Content.Shared.DeadSpace.Soyuz.Sterility.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Robust.Shared.Audio.Systems;

namespace Content.Server.DeadSpace.Soyuz.Sterility.EntitySystems;

public sealed class DisposablePackageSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SterilitySystem _sterility = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DisposablePackageComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<DisposablePackageComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var coordinates = Transform(ent.Owner).Coordinates;
        var injector = Spawn(ent.Comp.SpawnInjectorProto, coordinates);
        var trash = Spawn(ent.Comp.SpawnTrashProto, coordinates);

        if (TryComp(injector, out SterilityComponent? sterility))
            _sterility.OpenSterility((injector, sterility));

        if (TryComp(args.User, out HandsComponent? hands) && hands.ActiveHandId != null)
        {
            if (!_hands.TryForcePickup((args.User, hands), injector, hands.ActiveHandId, checkActionBlocker: false))
                _hands.PickupOrDrop(args.User, injector, checkActionBlocker: false, dropNear: true, handsComp: hands);

            var trashPlaced = false;
            foreach (var handId in hands.SortedHands)
            {
                if (handId == hands.ActiveHandId)
                    continue;

                if (_hands.TryPickup(args.User, trash, handId, checkActionBlocker: false, handsComp: hands))
                {
                    trashPlaced = true;
                    break;
                }
            }

            if (!trashPlaced)
                _hands.PickupOrDrop(args.User, trash, checkActionBlocker: false, dropNear: true, handsComp: hands);
        }
        else
        {
            _hands.PickupOrDrop(args.User, injector, checkActionBlocker: false, dropNear: true);
            _hands.PickupOrDrop(args.User, trash, checkActionBlocker: false, dropNear: true);
        }

        if (ent.Comp.UnwrapSound != null)
            _audio.PlayPredicted(ent.Comp.UnwrapSound, ent, args.User);

        QueueDel(ent.Owner);
        args.Handled = true;
    }
}
