// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace._Soyuz.GhostBar;

public sealed class GhostBarCostumeCard : PanelContainer
{
    public event Action<string>? OnToggled;
    public string CostumeId { get; }

    private readonly Button _button;

    public GhostBarCostumeCard(GhostBarCostumeOption option, bool selected)
    {
        CostumeId = option.Id;

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        MinHeight = 56;

        var hbox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        if (!string.IsNullOrEmpty(option.ClothingProto))
        {
            var sprite = new EntityPrototypeView
            {
                MinSize = new Vector2(48, 48),
                SetSize = new Vector2(48, 48),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                OverrideDirection = Direction.South,
            };
            sprite.SetPrototype(option.ClothingProto);
            hbox.AddChild(sprite);
        }

        var label = new Label
        {
            Text = Loc.GetString(option.Name),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        hbox.AddChild(label);

        _button = new Button
        {
            Text = selected
                ? Loc.GetString("ghost-bar-costume-remove")
                : Loc.GetString("ghost-bar-costume-select"),
            VerticalAlignment = VAlignment.Center,
            MinSize = new Vector2(90, 32),
        };
        _button.OnPressed += _ => OnToggled?.Invoke(CostumeId);
        hbox.AddChild(_button);

        AddChild(hbox);
    }
}