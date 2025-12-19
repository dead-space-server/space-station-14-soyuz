using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Robust.Shared.Random;
using Content.Shared.Speech;

namespace Content.Server.Speech.EntitySystems;

public sealed class KvirinAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KvirinAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, KvirinAccentComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        var tReplacements = new[] { "т", "тѣ" };
        var eReplacements = new[] { "е", "ѣ" };
        var eReplacementsB = new[] { "Е", "ѣ" };
        var aReplacements = new[] { "а", "á"};

        // Меняет в конце т => т/те
        message = Regex.Replace(
            message,
            @"т(?!.*т.*)" ,
            _ => _random.Pick(tReplacements)
        );
        message = Regex.Replace(
            message,
            "е",
            _ => _random.Pick(eReplacements)
        );
        message = Regex.Replace(
            message,
            "Е",
            _ => _random.Pick(eReplacementsB)
        );
        message = Regex.Replace(
            message,
            "у",
            _ => "у́"
        );
        message = Regex.Replace(
            message,
            "а",
            _ => _random.Pick(aReplacements)
        );
        args.Message = message;
    }
}
