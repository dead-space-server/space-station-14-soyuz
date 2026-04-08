// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.EUI;
using Content.Shared.DeadSpace.Skills;
using Content.Shared.DeadSpace.Skills.Events;
using Content.Shared.Eui;

namespace Content.Server.DeadSpace.Skill;

public sealed class SkillsListEui : BaseEui
{
    private readonly EntityUid _viewer;
    private readonly EntityUid _target;
    private readonly string _targetName;
    private readonly List<SkillInfo> _skills;
    private readonly SkillShareSystem _system;
    private readonly bool _allowLearningRequests;

    public SkillsListEui(
        EntityUid viewer,
        EntityUid target,
        string targetName,
        List<SkillInfo> skills,
        SkillShareSystem system,
        bool allowLearningRequests)
    {
        _viewer = viewer;
        _target = target;
        _targetName = targetName;
        _skills = skills;
        _system = system;
        _allowLearningRequests = allowLearningRequests;
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new SkillsListEuiState(_targetName, _skills);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not SkillTeachRequestMessage request)
            return;

        _system.HandleSkillLearnRequest(_viewer, _target, Player, request.PrototypeId, _allowLearningRequests);
    }
}
