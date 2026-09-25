// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.EUI;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Content.Shared.Eui;

namespace Content.Server.DeadSpace._Soyuz.GhostBar;

public sealed class GhostBarCostumeEui : BaseEui
{
    private readonly GhostBarSystem _bar;
    public EntityUid SourceGhost { get; }

    public GhostBarCostumeEui(GhostBarSystem bar, EntityUid sourceGhost)
    {
        _bar = bar;
        SourceGhost = sourceGhost;
    }

    public override EuiStateBase GetNewState()
    {
        return _bar.GetEuiState(Player);
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (IsShutDown)
            return;

        switch (msg)
        {
            case GhostBarCostumeToggleMessage toggle:
                _bar.ToggleCostume(Player, toggle.CostumeId);
                StateDirty();
                break;

            case GhostBarButtonPressedMessage:
                _bar.SpawnPlayerInBar(Player, SourceGhost);
                if (!IsShutDown)
                    Close();
                break;
        }
    }

    public override void Closed()
    {
        base.Closed();
        _bar.OnGhostBarEuiClosed(Player, this);
    }
}