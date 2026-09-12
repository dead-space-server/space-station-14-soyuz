using System.Numerics;
using Content.Client.ContextMenu.UI;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Receives a private server-authorized snapshot and enables its visualization when the local player
/// carries an active structural analyzer.
/// Inventory handling intentionally mirrors the T-ray scanner behavior.
/// </summary>
public sealed class RepairStructuralAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    private readonly Dictionary<EntityUid, RepairAnalyzerTaskData[]> _authorizedSnapshots = new();
    private RepairStructuralAnalyzerOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new RepairStructuralAnalyzerOverlay(
            EntityManager,
            _transform,
            _prototype,
            _sprite,
            _tileDefinitions,
            _authorizedSnapshots);
        _overlayManager.AddOverlay(_overlay);

        SubscribeNetworkEvent<RepairAnalyzerSnapshotEvent>(OnAnalyzerSnapshot);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        CommandBinds.Builder
            .BindBefore(
                EngineKeyFunctions.UseSecondary,
                new PointerInputCmdHandler(OnSecondaryUse, outsidePrediction: true),
                typeof(EntityMenuUIController))
            .Register<RepairStructuralAnalyzerSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<RepairStructuralAnalyzerSystem>();
        _overlayManager.RemoveOverlay(_overlay);
        _authorizedSnapshots.Clear();
        base.Shutdown();
    }

    private void OnAnalyzerSnapshot(RepairAnalyzerSnapshotEvent message)
    {
        _authorizedSnapshots.Clear();
        foreach (var snapshot in message.Grids)
        {
            var gridUid = GetEntity(snapshot.Grid);
            if (gridUid.IsValid())
                _authorizedSnapshots[gridUid] = snapshot.Tasks;
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent message)
    {
        _authorizedSnapshots.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var player = _player.LocalEntity;
        var range = 0f;
        if (player is { } playerUid)
        {
            if (_inventory.TryGetContainerSlotEnumerator(playerUid, out var enumerator))
            {
                while (enumerator.MoveNext(out var slot))
                {
                    foreach (var item in slot.ContainedEntities)
                        range = MathF.Max(range, GetEnabledRange(item));
                }
            }

            foreach (var hand in _hands.EnumerateHands(playerUid))
            {
                if (_hands.TryGetHeldItem(playerUid, hand, out var held))
                    range = MathF.Max(range, GetEnabledRange(held.Value));
            }
        }

        _overlay.Viewer = player;
        _overlay.Range = range;
    }

    private float GetEnabledRange(EntityUid analyzer)
    {
        if (!TryComp<RepairStructuralAnalyzerComponent>(analyzer, out var component) ||
            !TryComp<ItemToggleComponent>(analyzer, out var toggle) ||
            !toggle.Activated)
        {
            return 0f;
        }

        return MathF.Max(0f, component.Range);
    }

    private bool OnSecondaryUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State != BoundKeyState.Down || _overlay.Range <= 0f)
            return false;

        var mapCoordinates = _transform.ToMapCoordinates(args.Coordinates);
        if (!_overlay.TryGetTasksAt(mapCoordinates, out var tasks))
            return false;

        var context = _ui.GetUIController<ContextMenuUIController>();
        if (context.RootMenu.Visible)
            context.Close();

        foreach (var task in tasks)
        {
            context.AddElement(
                context.RootMenu,
                new ContextMenuElement(_overlay.GetDisplayName(task)));
        }

        var box = UIBox2.FromDimensions(_ui.MousePositionScaled.Position, new Vector2(1f));
        context.RootMenu.Open(box);
        return true;
    }
}
