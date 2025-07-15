using Content.Shared.Interaction;
using Content.Shared.DeadSpace.Soyuz.GardenShovel;
using Robust.Shared.Map;
using Content.Shared.Maps;
using Content.Server.DoAfter;
using Content.Shared.DoAfter;
using Robust.Shared.Physics;
using Content.Shared.Physics;
using Content.Shared.RCD.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Burial.Components;

namespace Content.Server.DeadSpace.Soyuz.GardenShovel;

public sealed class GardenSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly RCDSystem _mapget = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    private EntityCoordinates _cords;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GardenShovelComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<GardenShovelComponent, AfterInteractEvent>(GardenAfterInteract);
        SubscribeLocalEvent<GardenShovelComponent, GardenShovelDoAfterEvent>(GardenOnDoAfter);
    }
    private void OnUseInHand(Entity<GardenShovelComponent> ent, ref UseInHandEvent args)
    {
        switch (ent.Comp.Mode)
        {
            case Modes.Dig:
                ent.Comp.Mode = Modes.Bury;
                _popup.PopupEntity(Loc.GetString("gardenshovel-mode-bury"), args.User, args.User);
                break;
            case Modes.Bury:
                ent.Comp.Mode = Modes.Dig;
                _popup.PopupEntity(Loc.GetString("gardenshovel-mode-dig"), args.User, args.User);
                break;
        }
    }

    private void GardenAfterInteract(Entity<GardenShovelComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.CanReach)
            return;

        bool haveAvailableTile = false;

        _cords = args.ClickLocation;

        if (!_mapget.TryGetMapGridData(args.ClickLocation, out var mapGridData))
        {
            return;
        }
        switch (ent.Comp.Mode)
        {
            case Modes.Dig:
                var tileRef = mapGridData.Value.Tile;

                if (tileRef.IsSpace())
                {
                    _popup.PopupEntity(Loc.GetString("gardenshovel-not-available-tile-space"), args.User, args.User);
                    return;
                }

                foreach (string tileID in ent.Comp.AvailableTiles)
                {
                    haveAvailableTile = false;
                    if (tileRef.GetContentTileDefinition().ID == tileID)
                    {
                        haveAvailableTile = true;
                        break;
                    }
                }
                if (!haveAvailableTile)
                {
                    if (HasComp<GraveComponent>(args.Target))
                    {
                        return;
                    }

                    _popup.PopupEntity(Loc.GetString("gardenshovel-not-available-tile"), args.User, args.User);
                    return;
                }
                if (!IsCultivationValid(ent, mapGridData.Value))
                {
                    return;
                }
                break;
            case Modes.Bury:
                if (!TryComp<GardenShovelBuryComponent>(args.Target, out var _))
                    return;
                break;
        }

        var doAfterCancelled = !_doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(5.0), new GardenShovelDoAfterEvent(), ent, target: args.Target, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
        });

        if (doAfterCancelled)
        {
            return;
        }
    }
    private void GardenOnDoAfter(Entity<GardenShovelComponent> ent, ref GardenShovelDoAfterEvent args)
    {
        if (args.Cancelled)
            return;
        switch (ent.Comp.Mode)
        {
            case Modes.Dig:
                Spawn("hydroponicsSoil", _cords);
                break;
            case Modes.Bury:
                QueueDel(args.Target);
                break;
        }
    }
    private bool IsCultivationValid(Entity<GardenShovelComponent> ent, MapGridData mapGridData)
    {
        HashSet<EntityUid> intersectingEntities = new();

        intersectingEntities.Clear();
        _lookup.GetLocalEntitiesIntersecting(mapGridData.GridUid, mapGridData.Position, intersectingEntities, -0.05f, LookupFlags.Uncontained);

        foreach (var entity in intersectingEntities)
        {
            if (ent.Comp.CollisionMask != CollisionGroup.None && TryComp<FixturesComponent>(entity, out var fixtures))
            {
                foreach (var fixture in fixtures.Fixtures.Values)
                {
                    if (fixture.CollisionLayer <= 0 || (fixture.CollisionLayer & (int)ent.Comp.CollisionMask) == 0)
                        continue;
                    return false;
                }
            }
        }
        return true;
    }
}