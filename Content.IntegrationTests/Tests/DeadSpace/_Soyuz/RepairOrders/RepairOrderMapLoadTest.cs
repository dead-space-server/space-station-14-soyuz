using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairOrderMapLoadTest
{
    private readonly record struct RepairOrderGridUsage(
        string PrototypeId,
        string Kind,
        ResPath Path);

    [Test]
    public async Task RepairOrderGridsLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitPost(() =>
        {
            var repairOrders = prototypeManager.EnumeratePrototypes<RepairOrderPrototype>()
                .OrderBy(order => order.ID)
                .ToArray();

            Assert.That(repairOrders, Is.Not.Empty, "No RepairOrderPrototype instances were loaded.");

            var paths = repairOrders
                .SelectMany(order => new[]
                {
                    new RepairOrderGridUsage(order.ID, "Target", order.TargetGridPath),
                    new RepairOrderGridUsage(order.ID, "Damaged", order.DamagedGridPath),
                })
                .GroupBy(usage => usage.Path)
                .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal);

            foreach (var pathGroup in paths)
            {
                var path = pathGroup.Key;
                var usages = string.Join(", ", pathGroup.Select(usage => $"{usage.PrototypeId} ({usage.Kind})"));
                var diagnostic = $"Repair Order grid '{path}' used by: {usages}.";
                var failingLogCount = pair.ServerLogHandler.FailingLogs.Count;

                mapSystem.CreateMap(out var mapId);
                try
                {
                    Entity<MapGridComponent>? grid;
                    bool loaded;

                    try
                    {
                        loaded = mapLoader.TryLoadGrid(mapId, path, out grid);
                    }
                    catch (Exception exception)
                    {
                        throw new Exception($"Failed to load {diagnostic}", exception);
                    }

                    Assert.That(loaded, Is.True, $"Failed to load {diagnostic}");
                    Assert.That(grid!.Value.Comp.LocalAABB.Size.X, Is.GreaterThan(0f),
                        $"Loaded grid has no width. {diagnostic}");
                    Assert.That(grid.Value.Comp.LocalAABB.Size.Y, Is.GreaterThan(0f),
                        $"Loaded grid has no height. {diagnostic}");
                }
                finally
                {
                    mapSystem.DeleteMap(mapId);
                }

                var mapErrorLogs = pair.ServerLogHandler.FailingLogs
                    .Skip(failingLogCount)
                    .ToArray();
                Assert.That(mapErrorLogs, Is.Empty,
                    $"Error logs were emitted while loading {diagnostic}\n{string.Join('\n', mapErrorLogs)}");
            }
        });

        await pair.CleanReturnAsync();
    }
}
