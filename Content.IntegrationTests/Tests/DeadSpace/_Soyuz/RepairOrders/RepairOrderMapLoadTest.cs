using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairOrderMapLoadTest
{
    private static readonly ResPath[] RepairOrderGridPaths =
    [
        new("/Maps/_Soyuz/RepairOrders/mini_wreck_damaged.yml"),
        new("/Maps/_Soyuz/RepairOrders/mini_wreck_target.yml"),
        new("/Maps/_Soyuz/RepairOrders/floor_training_damaged.yml"),
        new("/Maps/_Soyuz/RepairOrders/floor_training_target.yml"),
    ];

    [Test]
    public async Task RepairOrderGridsLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            foreach (var path in RepairOrderGridPaths)
            {
                mapSystem.CreateMap(out var mapId);
                try
                {
                    Assert.That(mapLoader.TryLoadGrid(mapId, path, out var grid), Is.True, $"Failed to load {path}.");
                    Assert.That(grid!.Value.Comp.LocalAABB.Size.X, Is.GreaterThan(0f));
                    Assert.That(grid.Value.Comp.LocalAABB.Size.Y, Is.GreaterThan(0f));
                }
                finally
                {
                    mapSystem.DeleteMap(mapId);
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
