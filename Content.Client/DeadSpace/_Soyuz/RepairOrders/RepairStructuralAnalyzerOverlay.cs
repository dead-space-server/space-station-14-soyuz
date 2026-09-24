// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

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
/// Draws non-physical prototype and tile ghosts from the local player's server-authorized snapshot.
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
    private readonly IReadOnlyDictionary<EntityUid, RepairAnalyzerTaskData[]> _authorizedSnapshots;

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
        ITileDefinitionManager tileDefinitions,
        IReadOnlyDictionary<EntityUid, RepairAnalyzerTaskData[]> authorizedSnapshots)
    {
        _entityManager = entityManager;
        _transform = transform;
        _prototype = prototype;
        _sprite = sprite;
        _tileDefinitions = tileDefinitions;
        _authorizedSnapshots = authorizedSnapshots;
        ZIndex = 100;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!TryGetViewer(args.MapId, out var viewerPosition, out var rangeSquared) ||
            !TryGetSelectedGrid(
                args.MapId,
                viewerPosition,
                rangeSquared,
                out var selectedGrid,
                out var tasks,
                out var grid,
                out var worldMatrix))
        {
            return;
        }

        var handle = args.WorldHandle;
        handle.SetTransform(worldMatrix);
        var gridRotation = _transform.GetWorldRotation(selectedGrid);
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;

        foreach (var task in tasks.OrderBy(TileDrawOrder))
        {
            if (task.State == RepairTaskState.Correct && !task.Waived)
                continue;

            var worldPosition = Vector2.Transform(task.LocalPosition, worldMatrix);
            if (Vector2.DistanceSquared(viewerPosition, worldPosition) > rangeSquared)
                continue;

            DrawGhost(handle, task, grid.TileSize, gridRotation, eyeRotation);
        }

        // A covering ghost can fill the entire cell. Keep the separate missing base-floor outline visible.
        foreach (var task in tasks)
        {
            if (task.Type != RepairTaskType.Tile || (task.State == RepairTaskState.Correct && !task.Waived)) continue;
            var worldPosition = Vector2.Transform(task.LocalPosition, worldMatrix);
            if (Vector2.DistanceSquared(viewerPosition, worldPosition) > rangeSquared) continue;
            var color = task.Waived ? Color.FromHex("#B477FF") : task.State == RepairTaskState.Missing ? MissingBorder : WrongBorder;
            var size = grid.TileSize * (0.55f + 0.12f * TileDrawOrder(task));
            handle.DrawRect(Box2.CenteredAround(task.LocalPosition, new Vector2(size)), color, false);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private int TileDrawOrder(RepairAnalyzerTaskData task)
        => task.Type == RepairTaskType.Tile &&
           _tileDefinitions.TryGetDefinition(task.ExpectedPrototype, out var definition) && definition is ContentTileDefinition tile
            ? (int) RepairValueCatalog.GetTileLayer(tile) : 4;

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
                out var selectedTasks,
                out var grid,
                out var worldMatrix))
        {
            return false;
        }

        RepairAnalyzerTaskData? nearest = null;
        var nearestDistanceSquared = float.MaxValue;
        foreach (var task in selectedTasks)
        {
            if (task.State == RepairTaskState.Correct && !task.Waived)
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
        foreach (var task in selectedTasks)
        {
            if ((task.State != RepairTaskState.Correct || task.Waived) &&
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
        out IReadOnlyList<RepairAnalyzerTaskData> selectedTasks,
        out MapGridComponent selectedGridComponent,
        out Matrix3x2 selectedWorldMatrix)
    {
        selectedGrid = EntityUid.Invalid;
        selectedTasks = Array.Empty<RepairAnalyzerTaskData>();
        selectedGridComponent = default!;
        selectedWorldMatrix = default;
        var nearestDistanceSquared = float.MaxValue;

        // If several repair grids are nearby, select the one whose unfinished task is closest to the viewer.
        foreach (var (gridUid, tasks) in _authorizedSnapshots)
        {
            if (!_entityManager.TryGetComponent(gridUid, out MapGridComponent? grid) ||
                !_entityManager.TryGetComponent(gridUid, out TransformComponent? gridTransform) ||
                gridTransform.MapID != mapId ||
                tasks.Length == 0)
            {
                continue;
            }

            var worldMatrix = _transform.GetWorldMatrix(gridUid);
            foreach (var task in tasks)
            {
                if (task.State == RepairTaskState.Correct && !task.Waived)
                    continue;

                var worldPosition = Vector2.Transform(task.LocalPosition, worldMatrix);
                var distanceSquared = Vector2.DistanceSquared(viewerPosition, worldPosition);
                if (distanceSquared > rangeSquared || distanceSquared >= nearestDistanceSquared)
                    continue;

                nearestDistanceSquared = distanceSquared;
                selectedGrid = gridUid;
                selectedTasks = tasks;
                selectedGridComponent = grid;
                selectedWorldMatrix = worldMatrix;
            }
        }

        return selectedGrid.IsValid();
    }

    private void DrawGhost(DrawingHandleWorld handle, RepairAnalyzerTaskData task, float tileSize, Angle gridRotation, Angle eyeRotation)
    {
        var ghostColor = task.Waived ? Color.FromHex("#B477FF").WithAlpha(0.6f) : task.State == RepairTaskState.Missing ? MissingGhost : WrongGhost;
        var borderColor = task.Waived ? Color.FromHex("#B477FF") : task.State == RepairTaskState.Missing ? MissingBorder : WrongBorder;

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

        // RSI direction is selected in screen space, just as SpriteSystem.RenderSprite does.
        // Selecting it in grid-local space mirrors offset pipe lanes on rotated shuttles.
        var screenAngle = (task.LocalRotation + gridRotation + eyeRotation).Reduced().FlipPositive();
        var spriteRotation = visual.NoRotation ? -gridRotation - eyeRotation
            : task.LocalRotation - (visual.SnapCardinals ? screenAngle.RoundToCardinalAngle() : Angle.Zero);
        foreach (var layer in visual.Textures)
        {
            var texture = layer.TextureFor(screenAngle, out var directionRotation);
            var rotation = spriteRotation - (visual.NoRotation ? Angle.Zero : directionRotation);
            var size = texture.Size / (float) EyeManager.PixelsPerMeter * visual.Scale;
            var bounds = new Box2Rotated(Box2.CenteredAround(task.LocalPosition, size), rotation, task.LocalPosition);
            handle.DrawTextureRect(texture, bounds, ghostColor);
            handle.DrawRect(bounds, borderColor, false);
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
        public Texture TextureFor(Angle angle, out Angle directionRotation)
        {
            directionRotation = Angle.Zero;
            if (TextureProvider is not RSI.State state)
                return TextureProvider.TextureFor(Direction.South);

            var direction = SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
            directionRotation = (direction switch
            {
                RsiDirection.North => Direction.North,
                RsiDirection.East => Direction.East,
                RsiDirection.West => Direction.West,
                RsiDirection.SouthEast => Direction.SouthEast,
                RsiDirection.SouthWest => Direction.SouthWest,
                RsiDirection.NorthEast => Direction.NorthEast,
                RsiDirection.NorthWest => Direction.NorthWest,
                _ => Direction.South,
            }).ToAngle();
            return state.GetFrame(direction, 0);
        }
    }
}
