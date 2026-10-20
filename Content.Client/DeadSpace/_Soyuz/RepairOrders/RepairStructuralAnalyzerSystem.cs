// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using System.Linq;
using Content.Client.ContextMenu.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.Message;
using Robust.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Verbs;
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
    private readonly Dictionary<EntityUid, RepairAnalyzerTaskData[]> _navigationSnapshots = new();
    private readonly Dictionary<EntityUid, RepairAnalyzerLayer> _layers = new();
    private RepairStructuralAnalyzerOverlay _overlay = default!;
    private FancyWindow? _waiverDialog;
    private RepairAnalyzerWindow? _settings;
    private EntityUid? _settingsDevice;
    private EntityUid? _activeAnalyzer;
    private EntityUid? _navigationGlasses;
    private string? _navigationGroup;
    private (NetEntity Grid, int Runtime, int Requirement)? _targetKey;
    private readonly List<(EntityUid Grid, RepairAnalyzerTaskData Task)> _navigationTasks = new();
    private float _navigationRefresh;
    private bool _navigationDirty = true;

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
        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, GetVerbsEvent<AlternativeVerb>>(OnSettingsVerb);

        CommandBinds.Builder
            .BindBefore(
                EngineKeyFunctions.UseSecondary,
                new PointerInputCmdHandler(OnSecondaryUse, outsidePrediction: true),
                typeof(EntityMenuUIController))
            .Register<RepairStructuralAnalyzerSystem>();
    }

    public override void Shutdown()
    {
        _waiverDialog?.Dispose();
        _settings?.Dispose();
        CommandBinds.Unregister<RepairStructuralAnalyzerSystem>();
        _overlayManager.RemoveOverlay(_overlay);
        _authorizedSnapshots.Clear();
        _navigationSnapshots.Clear();
        base.Shutdown();
    }

    private void OnAnalyzerSnapshot(RepairAnalyzerSnapshotEvent message)
    {
        _navigationDirty = true;
        _authorizedSnapshots.Clear();
        foreach (var snapshot in message.Grids)
        {
            var gridUid = GetEntity(snapshot.Grid);
            if (gridUid.IsValid())
                _authorizedSnapshots[gridUid] = snapshot.Tasks;
        }
        if (!message.NavigationChanged)
            return;
        _navigationSnapshots.Clear();
        foreach (var snapshot in message.NavigationGrids)
        {
            var gridUid = GetEntity(snapshot.Grid);
            if (gridUid.IsValid())
                _navigationSnapshots[gridUid] = snapshot.Tasks;
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent message)
    {
        _waiverDialog?.Dispose();
        _waiverDialog = null;
        _authorizedSnapshots.Clear();
        _navigationSnapshots.Clear();
        _layers.Clear();
        _navigationGroup = null;
        _targetKey = null;
        _settings?.Dispose();
        _settings = null;
        _settingsDevice = null;
        _overlay.NavigationTarget = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var player = _player.LocalEntity;
        var range = 0f;
        _activeAnalyzer = null;
        if (player is { } playerUid)
        {
            if (_inventory.TryGetContainerSlotEnumerator(playerUid, out var enumerator))
            {
                while (enumerator.MoveNext(out var slot))
                {
                    foreach (var item in slot.ContainedEntities)
                        ConsiderAnalyzer(item, ref range);
                }
            }

            foreach (var hand in _hands.EnumerateHands(playerUid))
            {
                if (_hands.TryGetHeldItem(playerUid, hand, out var held))
                    ConsiderAnalyzer(held.Value, ref range);
            }
        }

        _overlay.Viewer = player;
        _overlay.Range = range;
        _overlay.LayerFilter = GetLayer(_activeAnalyzer);
        UpdateNavigation(player, frameTime);
    }

    private void ConsiderAnalyzer(EntityUid item, ref float range)
    {
        var itemRange = GetEnabledRange(item);
        if (itemRange <= 0f || itemRange < range || (itemRange == range && item != _settingsDevice))
            return;
        range = itemRange;
        _activeAnalyzer = item;
    }

    private RepairAnalyzerLayer GetLayer(EntityUid? item)
        => item is { } uid && _layers.TryGetValue(uid, out var layer) ? layer : RepairAnalyzerLayer.All;

    private float GetEnabledRange(EntityUid analyzer)
    {
        if (!TryComp<RepairStructuralAnalyzerComponent>(analyzer, out var component) ||
            component.Navigation ||
            !TryComp<ItemToggleComponent>(analyzer, out var toggle) ||
            !toggle.Activated)
        {
            return 0f;
        }

        return MathF.Max(0f, component.Range);
    }

    private void OnSettingsVerb(EntityUid uid, RepairStructuralAnalyzerComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (args.User != _player.LocalEntity ||
            (component.Navigation ? uid != _navigationGlasses : !IsOwnedAnalyzer(args.User, uid)))
            return;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(component.Navigation ? "repair-restorer-window-title" : "repair-analyzer-layers-title"),
            Act = () => OpenSettings(uid, component.Navigation),
        });
    }

    private bool IsOwnedAnalyzer(EntityUid user, EntityUid uid)
    {
        if (GetEnabledRange(uid) <= 0f)
            return false;
        if (_inventory.TryGetContainerSlotEnumerator(user, out var slots))
        {
            while (slots.MoveNext(out var slot))
            {
                if (slot.ContainedEntities.Contains(uid))
                    return true;
            }
        }
        return _hands.EnumerateHands(user).Any(hand => _hands.TryGetHeldItem(user, hand, out var held) && held == uid);
    }

    private void OpenSettings(EntityUid uid, bool navigation)
    {
        _settings?.Dispose();
        var window = new RepairAnalyzerWindow(navigation, GetLayer(uid));
        _settings = window;
        _settingsDevice = uid;
        _navigationDirty = true;
        window.OnLayer += layer =>
        {
            _layers[uid] = layer;
            _navigationDirty = true;
            if (navigation)
            {
                _navigationGroup = null;
                _targetKey = null;
            }
        };
        window.OnGroup += group =>
        {
            _navigationGroup = group;
            _targetKey = null;
            _navigationDirty = true;
        };
        window.OnStep += StepTarget;
        window.OnClose += () =>
        {
            if (ReferenceEquals(_settings, window))
            {
                _settings = null;
                _settingsDevice = null;
            }
            window.Dispose();
        };
        window.OpenCentered();
    }

    private void UpdateNavigation(EntityUid? player, float frameTime)
    {
        EntityUid? glasses = null;
        if (player is { } wearer && _inventory.TryGetSlotEntity(wearer, "eyes", out var eyes) &&
            TryComp<RepairStructuralAnalyzerComponent>(eyes, out var analyzer) && analyzer.Navigation &&
            TryComp<ItemToggleComponent>(eyes, out var toggle) && toggle.Activated)
            glasses = eyes;
        if (_navigationGlasses != glasses)
        {
            _navigationGroup = null;
            _targetKey = null;
            _navigationDirty = true;
        }
        _navigationGlasses = glasses;
        if (_settingsDevice is { } device && player is { } user &&
            device != glasses && !IsOwnedAnalyzer(user, device))
            _settings?.Close();
        if (glasses == null || player is not { } viewer)
        {
            _navigationTasks.Clear();
            _overlay.NavigationTarget = null;
            return;
        }
        if (_overlay.NavigationTarget is { } previousTarget && previousTarget.Grid != Transform(viewer).GridUid)
            _navigationDirty = true;
        _navigationRefresh += frameTime;
        if (!_navigationDirty && _navigationRefresh < 0.25f)
            return;
        _navigationRefresh = 0f;
        _navigationDirty = false;
        _navigationTasks.Clear();
        _overlay.NavigationTarget = null;

        var coordinates = _transform.GetMapCoordinates(viewer);
        var currentGrid = Transform(viewer).GridUid;
        var layer = GetLayer(glasses);
        foreach (var (grid, tasks) in _navigationSnapshots)
        {
            if (grid != currentGrid || !Exists(grid))
                continue;
            foreach (var task in tasks)
            {
                if (!task.Waived && task.State != RepairTaskState.Correct &&
                    (layer == RepairAnalyzerLayer.All || task.Layer == layer))
                    _navigationTasks.Add((grid, task));
            }
        }
        if (_settingsDevice == glasses)
            _settings?.UpdateTasks(_navigationTasks.Select(pair => pair.Task).ToArray(), _overlay.GetDisplayName, _navigationGroup);
        _navigationTasks.RemoveAll(pair => _navigationGroup != null && RepairAnalyzerWindow.GroupKey(pair.Task) != _navigationGroup);
        _navigationTasks.Sort((left, right) =>
        {
            var leftPosition = Vector2.Transform(left.Task.LocalPosition, _transform.GetWorldMatrix(left.Grid));
            var rightPosition = Vector2.Transform(right.Task.LocalPosition, _transform.GetWorldMatrix(right.Grid));
            var comparison = Vector2.DistanceSquared(coordinates.Position, leftPosition)
                .CompareTo(Vector2.DistanceSquared(coordinates.Position, rightPosition));
            return comparison != 0 ? comparison : left.Task.RequirementId.CompareTo(right.Task.RequirementId);
        });
        if (_navigationTasks.Count == 0)
        {
            _targetKey = null;
            if (_settingsDevice == glasses)
                _settings?.SetTarget(Loc.GetString("repair-restorer-no-tasks"));
            return;
        }
        var selected = _navigationTasks.FindIndex(pair => TaskKey(pair.Task) == _targetKey);
        if (selected < 0)
            selected = 0;
        var target = _navigationTasks[selected];
        _targetKey = TaskKey(target.Task);
        _overlay.NavigationTarget = target;
        if (_settingsDevice == glasses)
        {
            var position = Vector2.Transform(target.Task.LocalPosition, _transform.GetWorldMatrix(target.Grid));
            _settings?.SetTarget(Loc.GetString("repair-restorer-target", ("name", _overlay.GetDisplayName(target.Task)),
                ("distance", MathF.Round(Vector2.Distance(coordinates.Position, position), 1)),
                ("index", selected + 1), ("count", _navigationTasks.Count)));
        }
    }

    private static (NetEntity Grid, int Runtime, int Requirement) TaskKey(RepairAnalyzerTaskData task)
        => (task.Grid, task.RuntimeId, task.RequirementId);

    private void StepTarget(int step)
    {
        if (_navigationTasks.Count == 0)
            return;
        var index = _navigationTasks.FindIndex(pair => TaskKey(pair.Task) == _targetKey);
        index = (Math.Max(0, index) + step + _navigationTasks.Count) % _navigationTasks.Count;
        _targetKey = TaskKey(_navigationTasks[index].Task);
        _navigationDirty = true;
    }

    private void ShowWaiverConfirmation(RepairAnalyzerTaskData task)
    {
        _waiverDialog?.Dispose();
        var window = new FancyWindow { Title = Loc.GetString("repair-orders-waiver-heading"), MinWidth = 430 };
        _waiverDialog = window;
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        var explanation = new RichTextLabel { MaxWidth = 480 };
        explanation.SetMessage(Loc.GetString(task.Waived ? "repair-orders-waiver-cancel-confirm" : "repair-orders-waiver-confirm",
            ("name", _overlay.GetDisplayName(task)), ("points", task.Points),
            ("used", task.Exclusions.WaivedPoints), ("max", task.Exclusions.MaxWaivedPoints),
            ("percent", RepairTechnicalExclusion.PenaltyPercent(task.Exclusions.Count + (task.Waived ? -1 : 1)))));
        content.AddChild(explanation);
        var confirm = new Button { Text = Loc.GetString("repair-orders-waiver-confirm-button") };
        confirm.OnPressed += _ =>
        {
            RaiseNetworkEvent(new RepairAnalyzerWaiverRequest(task.Grid, task.RuntimeId, task.RequirementId, task.Waived));
            window.Close();
        };
        content.AddChild(confirm);
        var cancel = new Button { Text = Loc.GetString("repair-orders-waiver-back") };
        cancel.OnPressed += _ => window.Close();
        content.AddChild(cancel);
        window.ContentsContainer.AddChild(content);
        window.OpenCentered();
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
            var label = Loc.GetString(task.Waived ? "repair-orders-waiver-cancel-action" : "repair-orders-waiver-action",
                ("name", _overlay.GetDisplayName(task)));
            var element = new ContextMenuElement(Robust.Shared.Utility.FormattedMessage.EscapeText(label));
            element.OnPressed += _ =>
            {
                context.Close();
                ShowWaiverConfirmation(task);
            };
            context.AddElement(context.RootMenu, element);
        }

        var box = UIBox2.FromDimensions(_ui.MousePositionScaled.Position, new Vector2(1f));
        context.RootMenu.Open(box);
        return true;
    }
}
