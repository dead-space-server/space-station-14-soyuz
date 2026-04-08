using Content.Server.EUI;
using Content.Shared.DeadSpace.Skills.Events;
using Content.Shared.Eui;

namespace Content.Server.DeadSpace.Skill;

public sealed class SkillTransferConfirmEui : BaseEui
{
    private readonly string _title;
    private readonly string _message;
    private readonly Action<bool> _onResponse;

    public SkillTransferConfirmEui(string title, string message, Action<bool> onResponse)
    {
        _title = title;
        _message = message;
        _onResponse = onResponse;
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new SkillTransferConfirmEuiState(_title, _message);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        var accepted = msg is SkillTransferConfirmResponseMessage response && response.Accepted;
        _onResponse(accepted);
        Close();
    }
}
