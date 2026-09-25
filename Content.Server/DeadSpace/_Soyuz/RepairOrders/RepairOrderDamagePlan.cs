// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Immutable;
using System.Numerics;
using System.Linq;
using Robust.Shared.Prototypes;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>Index is stable within a snapshot; UID is used only by Apply, never by the random stream.</summary>
public sealed record RepairDamageEntity(int Index, EntityUid Uid, string Prototype, Vector2 Position,
    Angle Rotation, Vector2i Cell, string Category, int Value, bool Protected = false);

public sealed record RepairDamageSnapshot(EntityUid Grid, ImmutableArray<Vector2i> Floors,
    ImmutableArray<RepairDamageEntity> Entities, ImmutableArray<Vector2i> ProtectedFloors, int FloorValue)
{
    public ImmutableDictionary<Vector2i, int> FloorLayerCounts { get; init; } = ImmutableDictionary<Vector2i, int>.Empty;
    public int CountTileRequirements(IEnumerable<Vector2i> cells) => cells.Sum(cell => FloorLayerCounts.GetValueOrDefault(cell, 1));
    public int TotalValue => CountTileRequirements(Floors) * FloorValue + Entities.Sum(entity => entity.Value);
}

public sealed record RepairDamageEventResult(ProtoId<RepairDamageEventPrototype> Event,
    ImmutableArray<Vector2i> Centers, ImmutableArray<Vector2i> AffectedCells, int ChangedRequirements);

/// <summary>Staged immutable plan. No world mutation occurs until validation accepts the entire plan.</summary>
public sealed record RepairOrderDamagePlan(int Seed, int Attempt, ImmutableArray<Vector2i> RemovedTiles,
    ImmutableArray<int> RemovedEntities, ImmutableArray<RepairDamageEventResult> Events, int DamageValue, int TotalValue)
{
    public float DamageFraction => TotalValue > 0 ? (float) DamageValue / TotalValue : 0;
    public RepairDamageGenerationInfo ToInfo() => new(Seed, Attempt, Events.Select(e => e.Event.Id).ToImmutableArray(),
        RemovedTiles.Length, RemovedEntities.Length, DamageValue, DamageFraction);
}

/// <summary>Safe after cleanup: contains no entity UIDs or mutable world references.</summary>
public sealed record RepairDamageGenerationInfo(int Seed, int Attempt, ImmutableArray<string> SelectedEvents,
    int ChangedTiles, int RemovedEntities, int DamageValue, float DamageFraction);
