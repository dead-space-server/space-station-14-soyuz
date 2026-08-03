// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.DeadSpace._Soyuz.Construction;

/// <summary>
/// Performs exact fixture checks against a grid tile's center point.
/// </summary>
public sealed class TileCenterCollisionSystem : EntitySystem
{
    [Dependency] private readonly FixtureSystem _fixtures = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
    }

    public bool IsBlocked(
        Entity<MapGridComponent> grid,
        Vector2i gridIndices,
        int collisionLayer = 0,
        int collisionMask = 0)
    {
        var tileCenter = GetWorldTileCenter(grid, gridIndices);
        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, gridIndices);

        while (enumerator.MoveNext(out var entity))
        {
            var uid = entity.Value;
            if (!_physicsQuery.TryGetComponent(uid, out var body) ||
                !body.CanCollide ||
                !body.Hard ||
                !MatchesCollision(body.CollisionLayer, body.CollisionMask, collisionLayer, collisionMask))
            {
                continue;
            }

            if (!_fixturesQuery.TryGetComponent(uid, out var fixtures))
                return true;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard ||
                    !MatchesCollision(fixture.CollisionLayer, fixture.CollisionMask, collisionLayer, collisionMask))
                {
                    continue;
                }

                if (FixtureContains(uid, fixture, tileCenter))
                    return true;
            }
        }

        return false;
    }

    public bool FixtureContainsTileCenter(
        Entity<MapGridComponent> grid,
        Vector2i gridIndices,
        EntityUid fixtureOwner,
        Fixture fixture)
    {
        return FixtureContains(fixtureOwner, fixture, GetWorldTileCenter(grid, gridIndices));
    }

    private bool FixtureContains(EntityUid fixtureOwner, Fixture fixture, Vector2 point)
    {
        if (!_transformQuery.TryGetComponent(fixtureOwner, out var xform))
            return true;

        var (position, rotation) = _transform.GetWorldPositionRotation(xform, _transformQuery);
        return _fixtures.TestPoint(fixture.Shape, new Transform(position, rotation), point);
    }

    private Vector2 GetWorldTileCenter(Entity<MapGridComponent> grid, Vector2i gridIndices)
    {
        var gridXform = _transformQuery.GetComponent(grid.Owner);
        var (_, _, matrix) = _transform.GetWorldPositionRotationMatrix(gridXform, _transformQuery);
        var tileSize = grid.Comp.TileSize;
        var localCenter = new Vector2(
            (gridIndices.X + 0.5f) * tileSize,
            (gridIndices.Y + 0.5f) * tileSize);

        return Vector2.Transform(localCenter, matrix);
    }

    private static bool MatchesCollision(
        int entityLayer,
        int entityMask,
        int collisionLayer,
        int collisionMask)
    {
        return (entityMask & collisionLayer) != 0 ||
               (entityLayer & collisionMask) != 0;
    }
}
