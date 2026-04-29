using Content.Server._NF.Speech.Components;
using Content.Shared.Speech;
using Content.Server.Speech.EntitySystems;
using System.Text.RegularExpressions;

namespace Content.Server._NF.Speech.EntitySystems;

// The whole code is a copy of SouthernAccentSystem by UBlueberry (https://github.com/UBlueberry)
public sealed class GoblinSpeechAccentSystem : EntitySystem
{
    private static readonly Regex RegexIng = new(@"(in)g\b", RegexOptions.IgnoreCase);
    private static readonly Regex RegexAnd = new(@"\b(an)d\b", RegexOptions.IgnoreCase);
    private static readonly Regex RegexEr = new(@"([^\WpPfF])er\b"); // Keep "er", "per", "Per", "fer" and "Fer"
    private static readonly Regex RegexErUpper = new(@"([^\WpPfF])ER\b"); // Keep "ER", "PER" and "FER"
    private static readonly Regex RegexTwoLetterEr = new(@"(\w\w)er\b"); // Replace "..XXer", e.g. "super"->"supah"
    private static readonly Regex RegexTwoLetterErUpper = new(@"(\w\w)ER\b"); // Replace "..XXER", e.g. "SUPER"->"SUPAH"
    private static readonly Regex RegexErs = new(@"(\w)ers\b"); // Replace "..XXers", e.g. "fixers"->"fixas"
    private static readonly Regex RegexErsUpper = new(@"(\w)ERS\b"); // Replace "..XXers", e.g. "fixers"->"fixas"
    private static readonly Regex RegexTt = new(@"([aeiouy])tt", RegexOptions.IgnoreCase);
    private static readonly Regex RegexOf = new(@"\b(o)f\b", RegexOptions.IgnoreCase);
    private static readonly Regex RegexThe = new(@"\bthe\b");
    private static readonly Regex RegexTheUpper = new(@"\bTHE\b");
    private static readonly Regex RegexH = new(@"\bh", RegexOptions.IgnoreCase);
    private static readonly Regex RegexSelf = new(@"self\b");
    private static readonly Regex RegexSelfUpper = new(@"SELF\b");

    [Dependency] private readonly ReplacementAccentSystem _replacement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GoblinSpeechAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, GoblinSpeechAccentComponent component, AccentGetEvent args)
    {
        var text = _replacement.ApplyReplacements(args.Message, "goblin_accent");
        args.Message = ApplyGoblinPatternReplacements(text);
    }

    private static string ApplyGoblinPatternReplacements(string text)
    {
        text = RegexIng.Replace(text, "$1'"); //ing->in', ING->IN'
        text = RegexAnd.Replace(text, "$1'"); //and->an', AND->AN'
        text = RegexEr.Replace(text, "$1ah");
        text = RegexErUpper.Replace(text, "$1AH");
        text = RegexTwoLetterEr.Replace(text, "$1ah");
        text = RegexTwoLetterErUpper.Replace(text, "$1AH");
        text = RegexErs.Replace(text, "$1as");
        text = RegexErsUpper.Replace(text, "$1AS");
        text = RegexTt.Replace(text, "$1'");
        text = RegexH.Replace(text, "'");
        text = RegexSelf.Replace(text, "sewf");
        text = RegexSelfUpper.Replace(text, "SEWF");
        text = RegexOf.Replace(text, "$1'"); //of->o', OF->O'
        text = RegexThe.Replace(text, "da");
        return RegexTheUpper.Replace(text, "DA");
    }
};
