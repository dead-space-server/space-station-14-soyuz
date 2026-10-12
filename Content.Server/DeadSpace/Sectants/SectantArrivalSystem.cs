using Content.Shared.DeadSpace.Sectants;
using Robust.Shared.Audio;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantArrivalSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<SectantDisappearComponent, ComponentStartup>(OnSpawn);
    }

    private void OnSpawn(EntityUid uid, SectantDisappearComponent comp, ComponentStartup args)
    {
        if (!HasComp<SectantInvisibilityToggleComponent>(uid) &&
            !HasComp<SectantRandomTeleportOnDamageComponent>(uid))
            return;

        // _announce.Announce(
        //     "ОНО ПРИБЛИЖАЕТСЯ…",
        //     new SoundPathSpecifier(
        //         "/Audio/_DeadSpace/TEMP_FOR_EVENT/little_gabry/laughAl.ogg",
        //         AudioParams.Default.WithVolume(-8f)),
        //     Color.FromHex("#8b0000"),
        //     flashDuration: 1.2f,
        //     vignetteDuration: 5f,
        //     textDuration: 4f,
        //     shakeIntensity: 5f,
        //     whisper: true);
    }
}
