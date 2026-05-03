using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.DeadSpace.Soyuz.RitualAltar;

[RegisterComponent]
public sealed partial class RitualAltarInProgressComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan EndTime;

    [DataField]
    public RitualAltarSacrifice Sacrifice = RitualAltarSacrifice.None;

    [DataField]
    public RitualAltarDialogueStage DialogueStage = RitualAltarDialogueStage.None;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan ResponseDeadline;

    [DataField]
    public EntityUid? Questioner;

    [DataField]
    public EntityUid? Ritualist;
}

public enum RitualAltarSacrifice : byte
{
    None,
    Heart,
    Brain,
}

public enum RitualAltarDialogueStage : byte
{
    None,
    AwaitingFirstAnswer,
    AwaitingSecondAnswer,
}
