// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.MeteorDefense;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace._Soyuz.MeteorDefense;

public sealed class MeteorDefenseBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private MeteorDefenseWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MeteorDefenseWindow>();
        _window.SetEnabled += value => SendMessage(new MeteorDefenseSetEnabledMessage(value));
        _window.SetMaxCharge += value => SendMessage(new MeteorDefenseSetMaxChargeMessage(value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MeteorDefenseBoundUserInterfaceState defense)
            _window?.UpdateState(defense);
    }
}
