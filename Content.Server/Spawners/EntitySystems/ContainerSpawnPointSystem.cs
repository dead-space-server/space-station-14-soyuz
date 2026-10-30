using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared.Preferences;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Spawners.EntitySystems;

public sealed class ContainerSpawnPointSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawningEvent>(HandlePlayerSpawning, before: new []{ typeof(SpawnPointSystem) });
    }

    public void HandlePlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // If it's just a spawn pref check if it's for cryo (silly).
        if (args.HumanoidCharacterProfile?.SpawnPriority != SpawnPriorityPreference.Cryosleep &&
            (!_proto.Resolve(args.Job, out var jobProto) || jobProto.JobEntity == null))
        {
            return;
        }

        var query = EntityQueryEnumerator<ContainerSpawnPointComponent, ContainerManagerComponent, TransformComponent>();
        var possibleContainers = new List<Entity<ContainerSpawnPointComponent, ContainerManagerComponent, TransformComponent>>();

        while (query.MoveNext(out var uid, out var spawnPoint, out var container, out var xform))
        {
            if (args.Station != null && _station.GetOwningStation(uid, xform) != args.Station)
                continue;

            // DS14-Soyuz-start.
            if (TryComp<SpawnPointComponent>(uid, out var spawnPointComp) && !spawnPointComp.GeneralStaffRole)
                continue;
            // DS14-Soyuz-end

            // If it's unset, then we allow it to be used for both roundstart and midround joins
            if (spawnPoint.SpawnType == SpawnPointType.Unset)
            {
                // make sure we also check the job here for various reasons.
                if (spawnPoint.Job == null || spawnPoint.Job == args.Job)
                    possibleContainers.Add((uid, spawnPoint, container, xform));
                continue;
            }

            if (_gameTicker.RunLevel == GameRunLevel.InRound && spawnPoint.SpawnType == SpawnPointType.LateJoin)
            {
                possibleContainers.Add((uid, spawnPoint, container, xform));
            }

            if (_gameTicker.RunLevel != GameRunLevel.InRound &&
                spawnPoint.SpawnType == SpawnPointType.Job &&
                (args.Job == null || spawnPoint.Job == args.Job))
            {
                possibleContainers.Add((uid, spawnPoint, container, xform));
            }
        }

        if (possibleContainers.Count == 0)
        // DS14-Soyuz-start
        {
            if (args.HumanoidCharacterProfile?.SpawnPriority == SpawnPriorityPreference.Arrivals)
            {
                TryGetSpawn(args);
            }

            return;
        }
        // DS14-Soyuz-end
        // we just need some default coords so we can spawn the player entity.
        var baseCoords = possibleContainers[0].Comp3.Coordinates;

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            baseCoords,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);

        _random.Shuffle(possibleContainers);
        foreach (var (uid, spawnPoint, manager, xform) in possibleContainers)
        {
            if (!_container.TryGetContainer(uid, spawnPoint.ContainerId, out var container, manager))
                continue;

            if (!_container.Insert(args.SpawnResult.Value, container, containerXform: xform))
                continue;

            var ev = new ContainerSpawnEvent(args.SpawnResult.Value);
            RaiseLocalEvent(uid, ref ev);

            return;
        }

        Del(args.SpawnResult);
        args.SpawnResult = null;

        // DS14-Soyuz-start
        if (args.HumanoidCharacterProfile?.SpawnPriority == SpawnPriorityPreference.Arrivals)
        {
            TryGetSpawn(args);
        }
        // DS14-Soyuz-end
    }

    // DS14-Soyuz-start
    /// <summary>
    /// Find the free point, but not on General Staff.
    /// </summary>
    private void TryGetSpawn(PlayerSpawningEvent args)
    {
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possiblePoints = new List<Entity<SpawnPointComponent, TransformComponent>>();

        while (query.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (args.Station != null && _station.GetOwningStation(uid, xform) != args.Station)
                continue;

            if (!spawnPoint.GeneralStaffRole)
                continue;

            if (spawnPoint.SpawnType != SpawnPointType.LateJoin)
                continue;

            if (spawnPoint.Job != null && spawnPoint.Job != args.Job)
                continue;

            possiblePoints.Add((uid, spawnPoint, xform));
        }

        if (possiblePoints.Count == 0)
            return;

        _random.Shuffle(possiblePoints);
        var chosen = possiblePoints[0];

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            chosen.Comp2.Coordinates,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }
    // DS14-Soyuz-end
}

/// <summary>
/// Raised on a container when a player is spawned into it.
/// </summary>
[ByRefEvent]
public record struct ContainerSpawnEvent(EntityUid Player);
