using System.Numerics;
using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Draws non-physical prototype and tile ghosts from server-authored RepairAnalyzerData.
/// </summary>
public sealed class RepairStructuralAnalyzerOverlay : Overlay
{
    private static readonly Color MissingGhost = Color.FromHex("#7CF5FF").WithAlpha(0.56f);
    private static readonly Color MissingBorder = Color.FromHex("#A9FAFF").WithAlpha(0.92f);
    private static readonly Color WrongGhost = Color.FromHex("#FF7938").WithAlpha(0.62f);
    private static readonly Color WrongBorder = Color.FromHex("#FF3D2E").WithAlpha(0.96f);

    private readonly IEntityManager _entityManager;
    private readonly IPrototypeManager _prototype;
    private readonly SpriteSystem _sprite;
    private readonly ITileDefinitionManager _tileDefinitions;
    private readonly SharedTransformSystem _transform;

    private readonly Dictionary<string, PrototypeVisual> _entityVisuals = new();
    private readonly Dictionary<string, Texture?> _tileVisuals = new();

    public EntityUid? Viewer;
    public float Range;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public RepairStructuralAnalyzerOverlay(
        IEntityManager entityManager,
        SharedTransformSystem transform,
        IPrototypeManager prototype,
        SpriteSystem sprite,
        ITileDefinitionManager tileDefinitions)
    {
        _entityManager = entityManager;
        _transform = transform;
        _prototype = prototype;
        _sprite = sprite;
        _tileDefinitions = tileDefinitions;
        ZIndex = 100;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!TryGetViewer(args.MapId, out var viewerPosition, out var rangeSquared) ||
            !TryGetSelectedGrid(
                args.MapId,
                viewerPosition,
                rangeSquared,
                out _,
                out var data,
                out var grid,
                out var worldMatrix))
        {
            return;
        }

        var handle = args.WorldHandle;
        handle.SetTransform(worldMatrix);

        foreach (var task in data.Tasks)
        {
            if (task.State == RepairTaskState.Correct)
                continue;

            var worldPosition = Vector2.Transform(task.LocalPosition, worldMatrix);
            if (Vector2.DistanceSquared(viewerPosition, worldPosition) > rangeSquared)
                continue;

            DrawGhost(handle, task, grid.TileSize);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    /// Hit-tests current task positions in world space, retaining the selected grid's live transform.
    /// </summary>
    public bool TryGetTasksAt(MapCoordinates coordinates, out List<RepairAnalyzerTaskData> tasks)
    {
        tasks = new List<RepairAnalyzerTaskData>();
        if (!TryGetViewer(coordinates.MapId, out var viewerPosition, out var rangeSquared) ||
            !TryGetSelectedGrid(
                coordinates.MapId,
                viewerPosition,
                rangeSquared,
                out _,
                out var data,
                out var grid,
                out var worldMatrix))
        {
            return false;
        }

        RepairAnalyzerTaskData? nearest = null;
        var nearestDistanceSquared = float.MaxValue;
        foreach (var task in data.Tasks)
        {
            if (task.State == RepairTaskState.Correct)
                continue;

            var worldPosition = Vector2.Transform(task.LocalPosition, worldMatrix);
            if (Vector2.DistanceSquared(viewerPosition, worldPosition) > rangeSquared)
                continue;

            var clickDistanceSquared = Vector2.DistanceSquared(coordinates.Position, worldPosition);
            if (clickDistanceSquared >= nearestDistanceSquared)
                continue;

            nearest = task;
            nearestDistanceSquared = clickDistanceSquared;
        }

        var hitRadius = grid.TileSize * 0.55f;
        if (nearest == null || nearestDistanceSquared > hitRadius * hitRadius)
            return false;

        // Every unfinished requirement at the same precise local position is shown independently.
        foreach (var task in data.Tasks)
        {
            if (task.State != RepairTaskState.Correct &&
                Vector2.DistanceSquared(task.LocalPosition, nearest.LocalPosition) < 0.0001f)
            {
                tasks.Add(task);
            }
        }

        return tasks.Count > 0;
    }

    public string GetDisplayName(RepairAnalyzerTaskData task)
    {
        var displayName = GetPrototypeDisplayName(task);
        if (task.Type == RepairTaskType.RemoveAnchoredEntity)
            return Loc.GetString("repair-structural-analyzer-remove-entity", ("entity", displayName));

        return displayName;
    }

    private string GetPrototypeDisplayName(RepairAnalyzerTaskData task)
    {
        if ((task.Type == RepairTaskType.AnchoredEntity ||
             task.Type == RepairTaskType.RemoveAnchoredEntity) &&
            _prototype.TryIndex<EntityPrototype>(task.ExpectedPrototype, out var entity))
        {
            return entity.Name;
        }

        if (task.Type == RepairTaskType.Tile &&
            _tileDefinitions.TryGetDefinition(task.ExpectedPrototype, out var tile))
        {
            return Loc.GetString(tile.Name);
        }

        return task.ExpectedPrototype;
    }

    private bool TryGetViewer(MapId mapId, out Vector2 position, out float rangeSquared)
    {
        position = default;
        rangeSquared = 0f;
        if (Viewer is not { } viewer || Range <= 0f || !_entityManager.EntityExists(viewer))
            return false;

        var coordinates = _transform.GetMapCoordinates(viewer);
        if (coordinates.MapId != mapId)
            return false;

        position = coordinates.Position;
        rangeSquared = Range * Range;
        return true;
    }

    private bool TryGetSelectedGrid(
        MapId mapId,
        Vector2 viewerPosition,
        float rangeSquared,
        out EntityUid selectedGrid,
        out RepairAnalyzerDataComponent selectedData,
        out MapGridComponent selectedGridComponent,
        out Matrix3x2 selectedWorldMatrix)
    {
        selectedGrid = EntityUid.Invalid;
        selectedData = default!;
        selectedGridComponent = default!;
        selectedWorldMatrix = default;
        var nearestDistanceSquared = float.MaxValue;

        // If several repair grids are nearby, select the one whose unfinished task is closest to the viewer.
        var query = _entityManager.EntityQueryEnumerator<RepairAnalyzerDataComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var gridUid, out var data, out var grid, out var gridTransform))
        {
            if (gridTransform.MapID != mapId || data.Tasks.Count == 0)
                continue;

            var worldMatrix = _transform.GetWorldMatrix(gridUid);
            foreach (var task in data.Tasks)
            {
                if (task.State == RepairTaskState.Correct)
                    continue;

                var worldPosition = Vector2.Transform(task.LocalPosition, worldMatrix);
                var distanceSquared = Vector2.DistanceSquared(viewerPosition, worldPosition);
                if (distanceSquared > rangeSquared || distanceSquared >= nearestDistanceSquared)
                    continue;

                nearestDistanceSquared = distanceSquared;
                selectedGrid = gridUid;
                selectedData = data;
                selectedGridComponent = grid;
                selectedWorldMatrix = worldMatrix;
            }
        }

        return selectedGrid.IsValid();
    }

    private void DrawGhost(DrawingHandleWorld handle, RepairAnalyzerTaskData task, float tileSize)
    {
        var ghostColor = task.State == RepairTaskState.Missing ? MissingGhost : WrongGhost;
        var borderColor = task.State == RepairTaskState.Missing ? MissingBorder : WrongBorder;

        if (task.Type == RepairTaskType.Tile)
        {
            if (!TryGetTileTexture(task.ExpectedPrototype, out var tileTexture))
                return;

            var bounds = Box2.CenteredAround(task.LocalPosition, new Vector2(tileSize));
            handle.DrawTextureRect(tileTexture, bounds, ghostColor);
            handle.DrawRect(bounds, borderColor, false);
            return;
        }

        if (!TryGetEntityVisual(task.ExpectedPrototype, out var visual))
            return;

        var direction = task.LocalRotation.GetCardinalDir();
        Box2? combinedBounds = null;
        var hasRotatedLayer = false;
        var hasUnrotatedLayer = false;
        foreach (var layer in visual.Textures)
        {
            var rotateLayer = !visual.NoRotation &&
                              !visual.SnapCardinals &&
                              layer.RotatesWithEntity;
            var texture = layer.TextureFor(rotateLayer ? Direction.South : direction);
            var size = texture.Size / (float) EyeManager.PixelsPerMeter * visual.Scale;
            var bounds = Box2.CenteredAround(task.LocalPosition, size);
            if (rotateLayer)
            {
                handle.DrawTextureRect(
                    texture,
                    new Box2Rotated(bounds, task.LocalRotation, task.LocalPosition),
                    ghostColor);
                hasRotatedLayer = true;
            }
            else
            {
                handle.DrawTextureRect(texture, bounds, ghostColor);
                hasUnrotatedLayer = true;
            }

            combinedBounds = combinedBounds?.Union(bounds) ?? bounds;
        }

        if (combinedBounds is { } outline)
        {
            if (hasRotatedLayer && !hasUnrotatedLayer)
                handle.DrawRect(new Box2Rotated(outline, task.LocalRotation, task.LocalPosition), borderColor, false);
            else
                handle.DrawRect(outline, borderColor, false);
        }
    }

    private bool TryGetEntityVisual(string prototypeId, out PrototypeVisual visual)
    {
        if (_entityVisuals.TryGetValue(prototypeId, out visual))
            return visual.Textures.Count > 0;

        if (!_prototype.TryIndex<EntityPrototype>(prototypeId, out var prototype))
        {
            visual = new PrototypeVisual(new List<PrototypeVisualLayer>(), Vector2.One, false, false);
            _entityVisuals[prototypeId] = visual;
            return false;
        }

        var scale = Vector2.One;
        var snapCardinals = false;
        var noRotation = false;
        if (prototype.TryGetComponent<SpriteComponent>("Sprite", out var spriteComponent))
        {
            scale = spriteComponent.Scale;
            snapCardinals = spriteComponent.SnapCardinals;
            noRotation = spriteComponent.NoRotation;
        }

        var layers = _sprite
            .GetPrototypeTextures(prototype)
            .Select(texture => new PrototypeVisualLayer(texture))
            .ToList();

        visual = new PrototypeVisual(layers, scale, snapCardinals, noRotation);
        _entityVisuals[prototypeId] = visual;
        return visual.Textures.Count > 0;
    }

    private bool TryGetTileTexture(string prototypeId, out Texture texture)
    {
        if (_tileVisuals.TryGetValue(prototypeId, out var cached))
        {
            texture = cached!;
            return cached != null;
        }

        if (!_tileDefinitions.TryGetDefinition(prototypeId, out var definition) ||
            definition is not ContentTileDefinition { Sprite: { } spritePath })
        {
            _tileVisuals[prototypeId] = null;
            texture = default!;
            return false;
        }

        texture = _sprite.Frame0(new SpriteSpecifier.Texture(spritePath));
        _tileVisuals[prototypeId] = texture;
        return true;
    }

    private readonly record struct PrototypeVisual(
        List<PrototypeVisualLayer> Textures,
        Vector2 Scale,
        bool SnapCardinals,
        bool NoRotation);

    private readonly record struct PrototypeVisualLayer(IDirectionalTextureProvider TextureProvider)
    {
        public bool RotatesWithEntity =>
            TextureProvider is Texture ||
            TextureProvider is RSI.State { RsiDirections: RsiDirectionType.Dir1 };

        public Texture TextureFor(Direction direction)
        {
            return TextureProvider.TextureFor(direction);
        }
    }
}
