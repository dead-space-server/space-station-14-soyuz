using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server.Charges;
using Content.Server.Decals;
using Content.Server.Destructible;
using Content.Server.Popups;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Charges.Components;
using Content.Shared.Construction; // DS14-Soyuz: reuse the grille's existing window-building marker.
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.SprayPainter;
using Content.Shared.SprayPainter.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using System.Linq;
using System.Numerics;

namespace Content.Server.SprayPainter;

/// <summary>
/// Handles spraying pipes and decals using a spray painter.
/// Other paintable objects are handled in shared.
/// </summary>
public sealed class SprayPainterSystem : SharedSprayPainterSystem
{
    [Dependency] private readonly AtmosPipeColorSystem _pipeColor = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ChargesSystem _charges = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SprayPainterComponent, SprayPainterPipeDoAfterEvent>(OnPipeDoAfter);
        SubscribeLocalEvent<SprayPainterComponent, AfterInteractEvent>(OnFloorAfterInteract);
        SubscribeLocalEvent<AtmosPipeColorComponent, InteractUsingEvent>(OnPipeInteract);
        SubscribeLocalEvent<GasCanisterComponent, EntityPaintedEvent>(OnCanisterPainted);
    }

    /// <summary>
    /// Handles drawing decals when a spray painter is used to interact with the floor.
    /// Spray painter must have decal painting enabled and enough charges of paint to paint on the floor.
    /// </summary>
    private void OnFloorAfterInteract(Entity<SprayPainterComponent> ent, ref AfterInteractEvent args)
    {
        // DS14-Soyuz-start: Only a painter configured for grilles may draw on their grid tile.
        if (args.Handled || args.Target is { } target &&
            (!ent.Comp.AllowDecalsOnGrilles || !HasComp<SharedCanBuildWindowOnTopComponent>(target)))
            return;
        // DS14-Soyuz-end

        // DS14-Soyuz: A fixed-tint blueprint tool cannot sample another decal's color.
        if (ent.Comp.DecalColorEditable && ent.Comp.ColorPickerEnabled)
        {
            PickColor(ent, ref args);
            return;
        }

        if (!args.CanReach)
            return;

        // Includes both off and all other don't cares
        if (ent.Comp.DecalMode != DecalPaintMode.Add && ent.Comp.DecalMode != DecalPaintMode.Remove)
            return;

        args.Handled = true;
        if (TryComp(ent, out LimitedChargesComponent? charges) && _charges.GetCurrentCharges((ent, charges)) < ent.Comp.DecalChargeCost)
        {
            _popup.PopupEntity(Loc.GetString("spray-painter-interact-no-charges"), args.User, args.User);
            return;
        }

        var position = args.ClickLocation;
        if (ent.Comp.SnapDecals)
            position = position.SnapToGrid(EntityManager);

        if (ent.Comp.DecalMode == DecalPaintMode.Add)
        {
            // DS14-Soyuz-start: Validate again at placement, including stale or injected SelectedDecal values.
            if (!Proto.TryIndex<DecalPrototype>(ent.Comp.SelectedDecal, out var decal) || !IsDecalAllowed(ent.Comp, decal))
                return;
            // DS14-Soyuz-end

            // Offset painting for adding decals
            position = position.Offset(new(-0.5f));

            // DS14-Soyuz: Fixed tint is enforced at placement, even if SelectedDecalColor is stale or injected.
            var decalColor = ent.Comp.DecalColorEditable ? ent.Comp.SelectedDecalColor : ent.Comp.FixedDecalColor;
            if (!_decals.TryAddDecal(ent.Comp.SelectedDecal, position, out _, decalColor, Angle.FromDegrees(ent.Comp.SelectedDecalAngle), 0, false))
                return;
        }
        else
        {
            var gridUid = _transform.GetGrid(args.ClickLocation);
            if (gridUid is not { } grid || !TryComp<DecalGridComponent>(grid, out var decalGridComp))
            {
                _popup.PopupEntity(Loc.GetString("spray-painter-interact-nothing-to-remove"), args.User, args.User);
                return;
            }

            // DS14-Soyuz: A painter may remove only decals from its own allowed tags.
            var decals = _decals.GetDecalsInRange(grid, position.Position, validDelegate: decal => IsDecalValid(ent.Comp, decal));
            if (decals.Count <= 0)
            {
                _popup.PopupEntity(Loc.GetString("spray-painter-interact-nothing-to-remove"), args.User, args.User);
                return;
            }

            foreach (var decal in decals)
            {
                _decals.RemoveDecal(grid, decal.Index, decalGridComp);
            }
        }

        _audio.PlayPvs(ent.Comp.SpraySound, ent);

        // DS14-Soyuz-start: Zero-cost blueprint decals need no charge component.
        if (ent.Comp.DecalChargeCost > 0)
            _charges.TryUseCharges((ent, charges), ent.Comp.DecalChargeCost);
        // DS14-Soyuz-end

        AdminLogger.Add(LogType.CrayonDraw, LogImpact.Low, $"{ToPrettyString(args.User):user} painted a {ent.Comp.SelectedDecal}");
    }

    /// <summary>
    /// Returns whether <paramref name="decal"/> is valid to interact with when a spray painter is used to interact with the floor.
    /// </summary>
    private bool IsDecalValid(SprayPainterComponent painter, Decal decal)
    {
        if (!Proto.TryIndex<DecalPrototype>(decal.Id, out var decalProto))
            return false;

        // DS14-Soyuz: Use the same per-tool policy for picking and removing decals.
        return IsDecalAllowed(painter, decalProto);
    }

    /// <summary>
    /// Event handler when gas canisters are painted.
    /// The canister's color should not change when it's destroyed.
    /// </summary>
    private void OnCanisterPainted(Entity<GasCanisterComponent> ent, ref EntityPaintedEvent args)
    {
        var dummy = Spawn(args.Prototype);

        var destructibleComp = EnsureComp<DestructibleComponent>(dummy);
        CopyComp(dummy, ent, destructibleComp);

        Del(dummy);
    }

    private void OnPipeDoAfter(Entity<SprayPainterComponent> ent, ref SprayPainterPipeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        // DS14-Soyuz-start
        if (ent.Comp.DecalOnly)
            return;
        // DS14-Soyuz-end

        if (args.Args.Target is not { } target)
            return;

        if (!TryComp<AtmosPipeColorComponent>(target, out var color))
            return;

        if (TryComp<LimitedChargesComponent>(ent, out var charges) &&
            !_charges.TryUseCharges((ent, charges), ent.Comp.PipeChargeCost))
            return;

        Audio.PlayPvs(ent.Comp.SpraySound, ent);
        _pipeColor.SetColor(target, color, args.Color);

        args.Handled = true;
    }

    private void OnPipeInteract(Entity<AtmosPipeColorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<SprayPainterComponent>(args.Used, out var painter) ||
            painter.DecalOnly || // DS14-Soyuz: blueprint tools cannot paint pipes.
            painter.PickedColor is not { } colorName)
            return;

        if (!painter.ColorPalette.TryGetValue(colorName, out var color))
            return;

        if (TryComp<LimitedChargesComponent>(args.Used, out var charges)
            && _charges.GetCurrentCharges((args.Used, charges)) < painter.PipeChargeCost)
        {
            var msg = Loc.GetString("spray-painter-interact-no-charges");
            _popup.PopupEntity(msg, args.User, args.User);
            return;
        }

        var doAfterEventArgs = new DoAfterArgs(EntityManager,
            args.User,
            painter.PipeSprayTime,
            new SprayPainterPipeDoAfterEvent(color),
            args.Used,
            target: ent,
            used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            // multiple pipes can be sprayed at once just not the same one
            DuplicateCondition = DuplicateConditions.SameTarget,
            NeedHand = true,
        };

        args.Handled = DoAfter.TryStartDoAfter(doAfterEventArgs);
    }

    private void PickColor(Entity<SprayPainterComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.ClickLocation.IsValid(EntityManager) || _transform.GetGrid(args.ClickLocation) is not { } grid)
            return;

        var clickPos = args.ClickLocation.Position;
        // DS14-Soyuz: Restrict the color picker to this painter's decals.
        var decals = _decals.GetDecalsInRange(grid, clickPos, validDelegate: decal => IsDecalValid(ent.Comp, decal));
        if (decals.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("spray-painter-interact-no-color-pick"), args.User, args.User);
            return;
        }

        var closestDecal = decals.MinBy(d => Vector2.Distance(d.Decal.Coordinates, clickPos)).Decal;

        _popup.PopupEntity(Loc.GetString("spray-painter-interact-color-picked", ("id", closestDecal.Id)), args.User, args.User);

        ent.Comp.SelectedDecalColor = closestDecal.Color;
        ent.Comp.ColorPickerEnabled = false;
        Dirty(ent);
    }
}
