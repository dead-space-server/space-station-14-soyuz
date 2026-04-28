using Content.Shared.Database;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tools.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Shared.Tools.Systems;

public abstract partial class SharedToolSystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public void InitializeTile()
    {
        SubscribeLocalEvent<ToolTileCompatibleComponent, AfterInteractEvent>(OnToolTileAfterInteract);
        SubscribeLocalEvent<ToolTileCompatibleComponent, TileToolDoAfterEvent>(OnToolTileComplete);
    }

    private void OnToolTileAfterInteract(Entity<ToolTileCompatibleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target != null && !HasComp<PuddleComponent>(args.Target))
            return;

        args.Handled = UseToolOnTile((ent, ent, null), args.User, args.ClickLocation);
    }

    private void OnToolTileComplete(Entity<ToolTileCompatibleComponent> ent, ref TileToolDoAfterEvent args)
    {
        var comp = ent.Comp;
        if (args.Handled || args.Cancelled)
            return;

        if (!TryComp<ToolComponent>(ent, out var tool))
            return;

        var gridUid = GetEntity(args.Grid);
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
        {
            Log.Error("Attempted use tool on a non-existent grid?");
            return;
        }

        var tileRef = _maps.GetTileRef(gridUid, grid, args.GridTile);
        var coords = _maps.ToCoordinates(tileRef, grid);
        if (comp.RequiresUnobstructed && IsTileCenterBlockedByImpassable(gridUid, tileRef))
            return;

        if (!TryDeconstructWithToolQualities(tileRef, tool.Qualities))
            return;

        AdminLogger.Add(
            LogType.LatticeCut,
            LogImpact.Medium,
            $"{ToPrettyString(args.User):player} used {ToPrettyString(ent)} to edit the tile at {coords}");
        args.Handled = true;
    }

    private bool UseToolOnTile(Entity<ToolTileCompatibleComponent?, ToolComponent?> ent, EntityUid user, EntityCoordinates clickLocation)
    {
        if (!Resolve(ent, ref ent.Comp1, ref ent.Comp2, false))
            return false;

        var comp = ent.Comp1!;
        var tool = ent.Comp2!;

        if (!_mapManager.TryFindGridAt(_transformSystem.ToMapCoordinates(clickLocation), out var gridUid, out var mapGrid))
            return false;

        var tileRef = _maps.GetTileRef(gridUid, mapGrid, clickLocation);
        var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];

        if (!tool.Qualities.ContainsAny(tileDef.DeconstructTools))
            return false;

        if (string.IsNullOrWhiteSpace(tileDef.BaseTurf))
            return false;

        if (comp.RequiresUnobstructed && IsTileCenterBlockedByImpassable(gridUid, tileRef))
            return false;

        var coordinates = _maps.GridTileToLocal(gridUid, mapGrid, tileRef.GridIndices);
        if (!InteractionSystem.InRangeUnobstructed(user, coordinates, popup: false))
            return false;

        var args = new TileToolDoAfterEvent(GetNetEntity(gridUid), tileRef.GridIndices);
        UseTool(ent, user, ent, comp.Delay, tool.Qualities, args, out _, toolComponent: tool);
        return true;
    }

    public bool TryDeconstructWithToolQualities(TileRef tileRef, PrototypeFlags<ToolQualityPrototype> withToolQualities)
    {
        var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];
        if (withToolQualities.ContainsAny(tileDef.DeconstructTools))
        {
            // don't do this on the client or else the tile entity spawn mispredicts and looks horrible
            return _net.IsClient || _tiles.DeconstructTile(tileRef);
        }
        return false;
    }

    private bool IsTileCenterBlockedByImpassable(EntityUid gridUid, TileRef tileRef)
    {
        var tileCenter = tileRef.GridIndices + new Vector2(0.5f, 0.5f);
        var entities = _lookup.GetEntitiesInTile(tileRef, LookupFlags.Uncontained);
        var physicsQuery = GetEntityQuery<PhysicsComponent>();
        var fixturesQuery = GetEntityQuery<FixturesComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var ent in entities)
        {
            if (!physicsQuery.TryGetComponent(ent, out var physics) ||
                physics.BodyType != BodyType.Static ||
                !physics.CanCollide ||
                !physics.Hard ||
                (physics.CollisionLayer & (int) CollisionGroup.Impassable) == 0)
            {
                continue;
            }

            if (!fixturesQuery.TryGetComponent(ent, out var fixtures) ||
                !xformQuery.TryGetComponent(ent, out var xform))
            {
                return true;
            }

            var fixtureXform = new Transform(xform.LocalPosition, xform.LocalRotation);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionLayer & (int) CollisionGroup.Impassable) == 0)
                    continue;

                if (fixture.Shape.ComputeAABB(fixtureXform, 0).Contains(tileCenter))
                    return true;
            }
        }

        return false;
    }
}
