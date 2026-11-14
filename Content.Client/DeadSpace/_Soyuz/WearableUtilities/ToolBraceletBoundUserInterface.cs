// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.WearableUtilities;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace._Soyuz.WearableUtilities;

[UsedImplicitly]
public sealed class ToolBraceletBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private const int PageSize = 8;
    private static readonly SpriteSpecifier BraceletIcon = new SpriteSpecifier.Rsi(
        new ResPath("/Textures/_DeadSpace/_Soyuz/Objects/Devices/wearable_utilities.rsi"), "bracelet");
    private static readonly SpriteSpecifier ConfigureIcon = new SpriteSpecifier.Texture(
        new ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png"));
    private static readonly SpriteSpecifier ClearIcon = new SpriteSpecifier.Texture(
        new ResPath("/Textures/Interface/VerbIcons/delete.svg.192dpi.png"));
    private SimpleRadialMenu? _menu;
    private bool _populated;

    protected override void Open()
    {
        base.Open();
        _populated = false;
        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.Track(Owner);
        if (State is ToolBraceletUiState tools)
            Populate(tools);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ToolBraceletUiState tools)
            Populate(tools);
    }

    private void Populate(ToolBraceletUiState state)
    {
        if (_menu == null || _populated)
            return;
        var options = new List<RadialMenuOptionBase>();
        var configure = new List<RadialMenuOptionBase>();
        for (var i = 0; i < WearableUtilityComponent.BindingCount; i++)
        {
            var binding = state.Bindings[i];
            var label = Loc.GetString("soyuz-utility-bracelet-slot", ("slot", i + 1),
                ("name", binding?.Name ?? Loc.GetString("soyuz-utility-bracelet-empty")));
            var assignment = new RadialMenuNestedLayerOption(BindingOptions(state, i))
            {
                ToolTip = label,
                Order = i,
                IconSpecifier = Icon(binding),
                BackgroundColor = Color.FromHex("#34434b"),
            };
            configure.Add(assignment);
            if (binding == null)
            {
                options.Add(new UnavailableOption
                {
                    ToolTip = label,
                    Order = i,
                    IconSpecifier = Icon(null),
                    BackgroundColor = Color.FromHex("#3b3d42"),
                    HoverBackgroundColor = Color.FromHex("#3b3d42"),
                });
                continue;
            }
            if (binding.Available)
            {
                options.Add(new RadialMenuActionOption<ToolBraceletMessage>(SendRequest,
                    new ToolBraceletMessage(i, select: true, expectedBinding: binding.Item))
                {
                    ToolTip = label,
                    Order = i,
                    IconSpecifier = Icon(binding),
                    BackgroundColor = Color.FromHex("#355851"),
                });
                continue;
            }
            options.Add(new UnavailableOption
            {
                ToolTip = Loc.GetString("soyuz-utility-bracelet-unavailable", ("name", label)),
                Order = i,
                IconSpecifier = Icon(binding),
                BackgroundColor = Color.FromHex("#3b3d42"),
                HoverBackgroundColor = Color.FromHex("#3b3d42"),
            });
        }
        if (configure.Count > 0)
        {
            options.Add(new RadialMenuNestedLayerOption(configure)
            {
                ToolTip = Loc.GetString("soyuz-utility-bracelet-configure"),
                Order = WearableUtilityComponent.BindingCount,
                IconSpecifier = RadialMenuIconSpecifier.With(ConfigureIcon),
                BackgroundColor = Color.FromHex("#6c5436"),
            });
        }
        _populated = true;
        _menu.SetButtons(options);
        _menu.OpenOverMouseScreenPosition();
    }

    private List<RadialMenuOptionBase> BindingOptions(ToolBraceletUiState state, int slot)
    {
        var expected = state.Bindings[slot]?.Item;
        var options = new List<RadialMenuOptionBase>();
        for (var start = 0; start < state.Tools.Length; start += PageSize)
        {
            var page = new List<RadialMenuOptionBase>();
            var end = Math.Min(start + PageSize, state.Tools.Length);
            for (var i = start; i < end; i++)
            {
                var item = state.Tools[i];
                page.Add(new RadialMenuActionOption<ToolBraceletMessage>(SendRequest,
                    new ToolBraceletMessage(slot, item.Item, expectedBinding: expected))
                {
                    ToolTip = item.Name,
                    Order = i,
                    IconSpecifier = Icon(item),
                });
            }
            if (state.Tools.Length <= PageSize)
                options.AddRange(page);
            else
            {
                options.Add(new RadialMenuNestedLayerOption(page)
                {
                    ToolTip = Loc.GetString("soyuz-utility-bracelet-page", ("first", start + 1), ("last", end)),
                    Order = start,
                    IconSpecifier = Icon(state.Tools[start]),
                });
            }
        }
        if (expected != null)
        {
            options.Add(new RadialMenuActionOption<ToolBraceletMessage>(SendRequest,
                new ToolBraceletMessage(slot, expectedBinding: expected))
            {
                ToolTip = Loc.GetString("soyuz-utility-bracelet-clear"),
                Order = int.MaxValue,
                IconSpecifier = RadialMenuIconSpecifier.With(ClearIcon),
            });
        }
        if (options.Count == 0)
        {
            options.Add(new UnavailableOption
            {
                ToolTip = Loc.GetString("soyuz-utility-bracelet-no-items"),
                IconSpecifier = RadialMenuIconSpecifier.With(BraceletIcon),
            });
        }
        return options;
    }

    private RadialMenuIconSpecifier? Icon(ToolBraceletEntry? entry) =>
        entry != null && EntMan.TryGetEntity(entry.Item, out var uid)
            ? RadialMenuIconSpecifier.With(uid)
            : RadialMenuIconSpecifier.With(BraceletIcon);

    private void SendRequest(ToolBraceletMessage message) => SendMessage(message);

    private sealed class UnavailableOption : RadialMenuOptionBase;
}
