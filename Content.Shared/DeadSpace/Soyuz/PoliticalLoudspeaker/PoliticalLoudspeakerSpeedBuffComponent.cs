using Robust.Shared.GameStates;
namespace Content.Shared.PoliticalLoudspeaker;

[RegisterComponent,NetworkedComponent,AutoGenerateComponentState(true)]
public sealed partial class PoliticalLoudspeakerSpeedBuffComponent : Component
{
    [DataField,AutoNetworkedField] public float SpeedMultiplier=1f;

    [DataField]  public TimeSpan EndTime;
}
