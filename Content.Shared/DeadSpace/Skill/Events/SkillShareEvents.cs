// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Skills.Events;

/// <summary>
/// EUI state for showing the skills list of another player.
/// </summary>
[Serializable, NetSerializable]
public sealed class SkillsListEuiState : EuiStateBase
{
    public string TargetName;
    public List<SkillInfo> Skills;

    public SkillsListEuiState(string targetName, List<SkillInfo> skills)
    {
        TargetName = targetName;
        Skills = skills;
    }
}

/// <summary>
/// EUI state for asking player if they want to share their skills.
/// </summary>
[Serializable, NetSerializable]
public sealed class SkillShareRequestEuiState : EuiStateBase
{
    public string RequesterName;

    public SkillShareRequestEuiState(string requesterName)
    {
        RequesterName = requesterName;
    }
}

/// <summary>
/// Message sent when player responds to skill share request.
/// </summary>
[Serializable, NetSerializable]
public sealed class SkillShareResponseMessage : EuiMessageBase
{
    public bool Accepted;

    public SkillShareResponseMessage(bool accepted)
    {
        Accepted = accepted;
    }
}

/// <summary>
/// Message sent when the player clicks a skill in another character's list.
/// </summary>
[Serializable, NetSerializable]
public sealed class SkillTeachRequestMessage : EuiMessageBase
{
    public string PrototypeId;

    public SkillTeachRequestMessage(string prototypeId)
    {
        PrototypeId = prototypeId;
    }
}

/// <summary>
/// Generic yes/no confirm state used for skill teaching flow.
/// </summary>
[Serializable, NetSerializable]
public sealed class SkillTransferConfirmEuiState : EuiStateBase
{
    public string Title;
    public string Message;

    public SkillTransferConfirmEuiState(string title, string message)
    {
        Title = title;
        Message = message;
    }
}

/// <summary>
/// Response message for skill teaching confirmations.
/// </summary>
[Serializable, NetSerializable]
public sealed class SkillTransferConfirmResponseMessage : EuiMessageBase
{
    public bool Accepted;

    public SkillTransferConfirmResponseMessage(bool accepted)
    {
        Accepted = accepted;
    }
}
