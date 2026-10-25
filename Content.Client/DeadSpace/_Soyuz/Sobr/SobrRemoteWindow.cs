// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.Sobr;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace._Soyuz.Sobr;

/// <summary>
/// Окно пультика. Я рот ебал xaml, так что будет хардкод ☺.
/// </summary>
public sealed class SobrRemoteWindow : FancyWindow
{
    public event Action<ProtoId<SobrSquadPrototype>>? OnWake;

    private readonly BoxContainer _list;

    public SobrRemoteWindow()
    {
        Title = Loc.GetString("sobr-remote-title");
        MinSize = new Vector2(320, 120);

        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        XamlChildren.Add(_list);
    }

    public void Update(SobrRemoteBuiState state)
    {
        _list.RemoveAllChildren();

        foreach (var squad in state.Squads)
        {
            var id = squad.Id;
            var button = new Button
            {
                Text = Loc.GetString("sobr-remote-squad-button",
                    ("name", squad.Name), ("ready", squad.Ready), ("total", squad.Total)),
                HorizontalExpand = true,
                MinHeight = 36,
                Disabled = squad.Ready == 0,
                ModulateSelfOverride = squad.Color,
            };
            button.OnPressed += _ => OnWake?.Invoke(id);
            _list.AddChild(button);
        }
    }
}