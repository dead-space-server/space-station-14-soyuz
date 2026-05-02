using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookRuneDetailsWindow : FancyWindow
{
    private readonly RuneInstructionControl _instruction;

    public RuneBookRuneDetailsWindow(int runeId)
    {
        var runeName = ResolveRuneName(runeId);
        Title = Loc.GetString("rune-book-ui-details-title", ("rune", runeId + 1), ("name", runeName));

        MinSize = new Vector2(720, 620);
        SetSize = new Vector2(860, 720);
        Resizable = true;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10)
        };

        var header = new Label
        {
            Text = Loc.GetString("rune-book-ui-details-header", ("rune", runeId + 1), ("name", runeName)),
            HorizontalAlignment = HAlignment.Center,
            StyleClasses = { "LabelHeadingBigger" }
        };
        root.AddChild(header);

        root.AddChild(new Label
        {
            Text = Loc.GetString("rune-book-ui-details-help"),
            HorizontalAlignment = HAlignment.Center
        });

        _instruction = new RuneInstructionControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinSize = new Vector2(520, 520),
            MouseFilter = MouseFilterMode.Ignore,
            RuneId = runeId,
            StepDuration = 0.55f,
            Loop = true
        };

        root.AddChild(_instruction);

        XamlChildren.Add(root);
    }

    private static string ResolveRuneName(int runeId)
    {
        if (!RuneBookRuneLibrary.TryGetRunePrototypeId(runeId, out var protoId))
            return Loc.GetString("rune-book-ui-details-unknown");

        var proto = IoCManager.Resolve<IPrototypeManager>();
        return proto.TryIndex<RuneBookRunePrototype>(protoId, out var runeProto)
            ? runeProto.Name
            : protoId;
    }
}
