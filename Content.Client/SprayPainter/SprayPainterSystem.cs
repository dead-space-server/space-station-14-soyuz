using System.Linq;
using Content.Client.Items;
using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared.Decals;
using Content.Shared.SprayPainter;
using Content.Shared.SprayPainter.Components;
using Content.Shared.SprayPainter.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.SprayPainter;

/// <summary>
/// Client-side spray painter functions. Caches information for spray painter windows and updates the UI to reflect component state.
/// </summary>
public sealed class SprayPainterSystem : SharedSprayPainterSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    // DS14-Soyuz: Decal lists are built per tool from AllowedDecalTags.
    public Dictionary<string, List<string>> PaintableGroupsByCategory = new();
    public Dictionary<string, Dictionary<string, EntProtoId>> PaintableStylesByGroup = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.ItemStatus<SprayPainterComponent>(ent => new StatusControl(ent));
        SubscribeLocalEvent<SprayPainterComponent, AfterAutoHandleStateEvent>(OnStateUpdate);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        CachePrototypes();
    }

    private void OnStateUpdate(Entity<SprayPainterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(ent);
    }

    protected override void UpdateUi(Entity<SprayPainterComponent> ent)
    {
        if (_ui.TryGetOpenUi(ent.Owner, SprayPainterUiKey.Key, out var bui))
            bui.Update();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<PaintableGroupCategoryPrototype>() || !args.WasModified<PaintableGroupPrototype>() || !args.WasModified<DecalPrototype>())
            return;

        CachePrototypes();
    }

    private void CachePrototypes()
    {
        PaintableGroupsByCategory.Clear();
        PaintableStylesByGroup.Clear();
        foreach (var category in Proto.EnumeratePrototypes<PaintableGroupCategoryPrototype>().OrderBy(x => x.ID))
        {
            var groupList = new List<string>();
            foreach (var groupId in category.Groups)
            {
                if (!Proto.Resolve(groupId, out var group))
                    continue;

                groupList.Add(groupId);
                PaintableStylesByGroup[groupId] = group.Styles;
            }

            if (groupList.Count > 0)
                PaintableGroupsByCategory[category.ID] = groupList;
        }

        // DS14-Soyuz: The UI must not cache one decal list for every painter.
    }

            // DS14-start
    // DS14-Soyuz-start
    public List<SprayPainterDecalEntry> GetFilteredDecals(SprayPainterComponent component)
    {
        return Proto.EnumeratePrototypes<DecalPrototype>()
            .Where(decal => IsDecalAllowed(component, decal))
            .OrderBy(decal => decal.ID)
            .Select(decal => new SprayPainterDecalEntry(decal.ID, decal.Sprite, decal.SprayPainterName ?? decal.ID)) // DS14-Soyuz
            .ToList();
    }
    // DS14-Soyuz-end

    public Dictionary<string, List<string>> GetFilteredPaintableGroups(SprayPainterComponent component)
    {
        var filteredGroups = new Dictionary<string, List<string>>();

        // DS14-Soyuz-start
        if (component.DecalOnly)
            return filteredGroups;
        // DS14-Soyuz-end

        foreach (var category in Proto.EnumeratePrototypes<PaintableGroupCategoryPrototype>().OrderBy(x => x.ID))
        {
            if (component.AllowedCategories.Count > 0 && !component.AllowedCategories.Contains(category.ID))
                continue;

            var groupList = new List<string>();
            foreach (var groupId in category.Groups)
            {
                if (!IsGroupAllowed(component, groupId))
                    continue;

                if (!Proto.TryIndex(groupId, out var group))
                    continue;

                groupList.Add(groupId);
            }

            if (groupList.Count > 0)
                filteredGroups[category.ID] = groupList;
        }

        return filteredGroups;
    }

    public Dictionary<string, Dictionary<string, EntProtoId>> GetFilteredPaintableStyles(
        SprayPainterComponent component)
    {
        var filteredStyles = new Dictionary<string, Dictionary<string, EntProtoId>>();

        // DS14-Soyuz-start
        if (component.DecalOnly)
            return filteredStyles;
        // DS14-Soyuz-end

        foreach (var groupId in PaintableStylesByGroup.Keys)
        {
            if (!IsGroupAllowed(component, groupId))
                continue;

            filteredStyles[groupId] = PaintableStylesByGroup[groupId];
        }

        return filteredStyles;
    }
            // DS14-end

    private sealed class StatusControl : Control
    {
        private readonly RichTextLabel _label;
        private readonly Entity<SprayPainterComponent> _entity;
        private DecalPaintMode? _lastPaintingDecals = null;

        public StatusControl(Entity<SprayPainterComponent> ent)
        {
            _entity = ent;
            _label = new RichTextLabel { StyleClasses = { StyleClass.ItemStatus } };
            AddChild(_label);
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (_entity.Comp.DecalMode == _lastPaintingDecals)
                return;

            _lastPaintingDecals = _entity.Comp.DecalMode;

            string modeLocString = _entity.Comp.DecalMode switch
            {
                DecalPaintMode.Add => "spray-painter-item-status-add",
                DecalPaintMode.Remove => "spray-painter-item-status-remove",
                _ => "spray-painter-item-status-off"
            };

            _label.SetMarkupPermissive(Robust.Shared.Localization.Loc.GetString("spray-painter-item-status-label",
                ("mode", Robust.Shared.Localization.Loc.GetString(modeLocString))));
        }
    }
}

/// <summary>
/// A spray paintable decal, mapped by ID.
/// </summary>
// DS14-Soyuz: DisplayName is optional prototype data; Name remains the decal ID sent to the server.
public sealed record SprayPainterDecalEntry(string Name, SpriteSpecifier Sprite, string DisplayName);
