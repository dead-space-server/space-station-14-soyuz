using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Robust.Shared.Random;
using Content.Shared.Speech;

namespace Content.Server.Speech.EntitySystems;

public sealed class KvirinAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly Regex _endTRegex = new Regex(@"т(?!.*т.*)", RegexOptions.Compiled);
    private static readonly Regex _eRegex = new Regex("е", RegexOptions.Compiled);
    private static readonly Regex _eUpperRegex = new Regex("Е", RegexOptions.Compiled);
    private static readonly Regex _uRegex = new Regex("у", RegexOptions.Compiled);
    private static readonly Regex _aRegex = new Regex("а", RegexOptions.Compiled);

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
        message = _endTRegex.Replace(message, _ => _random.Pick(tReplacements));
        message = _eRegex.Replace(message, _ => _random.Pick(eReplacements));
        message = _eUpperRegex.Replace(message, _ => _random.Pick(eReplacementsB));
        message = _uRegex.Replace(message, _ => "у́");
        message = _aRegex.Replace(message, _ => _random.Pick(aReplacements));
        
        args.Message = message;
    }
}
