using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Skills.Events;

[Serializable, NetSerializable]
public sealed partial class LearnDoAfterEvent : SimpleDoAfterEvent
{ }

[Serializable, NetSerializable]
public sealed partial class SkillTransferDoAfterEvent : DoAfterEvent
{
    public string SkillId;

    public SkillTransferDoAfterEvent(string skillId)
    {
        SkillId = skillId;
    }

    private SkillTransferDoAfterEvent()
    {
        SkillId = string.Empty;
    }

    public override DoAfterEvent Clone()
    {
        return new SkillTransferDoAfterEvent(SkillId);
    }

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is SkillTransferDoAfterEvent transfer && transfer.SkillId == SkillId;
    }
}
