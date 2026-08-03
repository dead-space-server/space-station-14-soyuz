// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using System.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace Content.Server.DeadSpace._Soyuz.TTS;

/// <summary>
/// Converts chat punctuation and common text emotions into safe SSML prosody.
/// </summary>
public static partial class TtsIntonationFormatter
{
    private const RegexOptions CommonRegexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"(?:^|\s)/s\s*$", CommonRegexOptions)]
    private static partial Regex SarcasmRegex();

    [GeneratedRegex(@"(?<marker>:'\(|:-O|:-/|:/|:-?\)|:-?\(|:D|;-?\)|\^\^+|~+|\)+|\(+)\s*$", CommonRegexOptions)]
    private static partial Regex EmotionMarkerRegex();

    [GeneratedRegex(@"(?:\?+!+|!+\?+|[!?]*[!?][!?]+)\s*$")]
    private static partial Regex StrongPunctuationRegex();

    [GeneratedRegex(@"(?:\.{3,}|\u2026|,{2,})\s*$")]
    private static partial Regex ThoughtfulPunctuationRegex();

    [GeneratedRegex(@"\?\s*$")]
    private static partial Regex QuestionRegex();

    [GeneratedRegex(@"!\s*$")]
    private static partial Regex ExclamationRegex();

    [GeneratedRegex(@"[!?]{2,}\s*$")]
    private static partial Regex NormalizeStrongPunctuationRegex();

    [GeneratedRegex(@"\.{4,}\s*$")]
    private static partial Regex NormalizeEllipsisRegex();

    [GeneratedRegex(@",{2,}\s*$")]
    private static partial Regex NormalizeCommaPauseRegex();

    private static readonly (string Symbol, TtsIntonationStyle Style)[] EmojiMap =
    {
        ("\U0001F600", TtsIntonationStyle.Happy),
        ("\U0001F603", TtsIntonationStyle.Happy),
        ("\U0001F604", TtsIntonationStyle.Happy),
        ("\U0001F601", TtsIntonationStyle.Happy),
        ("\U0001F602", TtsIntonationStyle.Happy),
        ("\U0001F923", TtsIntonationStyle.Happy),
        ("\U0001F60A", TtsIntonationStyle.Happy),
        ("\U0001F642", TtsIntonationStyle.Happy),
        ("\U0001F609", TtsIntonationStyle.Playful),
        ("\U0001F60D", TtsIntonationStyle.Happy),
        ("\U0001F970", TtsIntonationStyle.Happy),
        ("\U0001F618", TtsIntonationStyle.Playful),
        ("\U0001F973", TtsIntonationStyle.Happy),
        ("\U0001F622", TtsIntonationStyle.Sad),
        ("\U0001F62D", TtsIntonationStyle.Sad),
        ("\u2639\uFE0F", TtsIntonationStyle.Sad),
        ("\U0001F641", TtsIntonationStyle.Sad),
        ("\U0001F61E", TtsIntonationStyle.Sad),
        ("\U0001F614", TtsIntonationStyle.Sad),
        ("\U0001F625", TtsIntonationStyle.Sad),
        ("\U0001F621", TtsIntonationStyle.Angry),
        ("\U0001F620", TtsIntonationStyle.Angry),
        ("\U0001F92C", TtsIntonationStyle.Angry),
        ("\U0001F631", TtsIntonationStyle.Surprised),
        ("\U0001F632", TtsIntonationStyle.Surprised),
        ("\U0001F62E", TtsIntonationStyle.Surprised),
        ("\U0001F92F", TtsIntonationStyle.Surprised),
        ("\U0001F60F", TtsIntonationStyle.Sarcastic),
        ("\U0001F643", TtsIntonationStyle.Sarcastic),
        ("\U0001F612", TtsIntonationStyle.Skeptical),
        ("\U0001F644", TtsIntonationStyle.Skeptical),
        ("\U0001F628", TtsIntonationStyle.Afraid),
        ("\U0001F630", TtsIntonationStyle.Afraid),
        ("\U0001F62C", TtsIntonationStyle.Afraid),
    };

    /// <summary>
    /// Removes non-verbal emotion markers and determines the strongest terminal intonation.
    /// </summary>
    public static TtsIntonationAnalysis Analyze(string rawText)
    {
        var text = rawText.Trim();
        TtsIntonationStyle? explicitStyle = null;

        var sarcasm = SarcasmRegex().Match(text);
        if (sarcasm.Success)
        {
            explicitStyle = TtsIntonationStyle.Sarcastic;
            text = text[..sarcasm.Index].TrimEnd();
        }

        var emojiStyle = FindLastEmojiStyle(text);
        foreach (var (symbol, _) in EmojiMap)
        {
            text = text.Replace(symbol, string.Empty, StringComparison.Ordinal);
        }

        var marker = EmotionMarkerRegex().Match(text);

        TtsIntonationStyle? markerStyle = null;
        if (marker.Success)
        {
            var value = marker.Groups["marker"].Value;
            markerStyle = GetMarkerStyle(value);
            text = text[..marker.Index].TrimEnd();
        }

        var punctuationStyle = GetPunctuationStyle(text);
        var style = explicitStyle ?? markerStyle ?? emojiStyle ?? punctuationStyle;

        text = NormalizeTerminalPunctuation(text);
        return new TtsIntonationAnalysis(text.Trim(), style);
    }

    /// <summary>
    /// Builds an escaped SSML document using prosody supported by the TTS backend.
    /// </summary>
    public static string BuildSsml(string text, TtsIntonationStyle style, bool isWhisper = false)
    {
        var prosody = GetProsody(style);
        if (isWhisper)
            prosody = prosody with { Pitch = "x-low", Volume = "x-soft" };

        var escaped = SecurityElement.Escape(text) ?? string.Empty;
        var pause = prosody.PauseMs > 0
            ? $"<break time=\"{prosody.PauseMs}ms\"/>"
            : string.Empty;

        return $"<speak><prosody rate=\"{prosody.Rate}\" pitch=\"{prosody.Pitch}\" volume=\"{prosody.Volume}\">{escaped}</prosody>{pause}</speak>";
    }

    private static TtsIntonationStyle? FindLastEmojiStyle(string text)
    {
        var lastIndex = -1;
        TtsIntonationStyle? style = null;

        foreach (var (symbol, emojiStyle) in EmojiMap)
        {
            var index = text.LastIndexOf(symbol, StringComparison.Ordinal);
            if (index <= lastIndex)
                continue;

            lastIndex = index;
            style = emojiStyle;
        }

        return style;
    }

    private static TtsIntonationStyle GetMarkerStyle(string marker)
    {
        if (marker.StartsWith("^^", StringComparison.Ordinal) || marker.StartsWith(";", StringComparison.Ordinal))
            return TtsIntonationStyle.Playful;

        if (marker.StartsWith("~", StringComparison.Ordinal))
            return TtsIntonationStyle.Playful;

        if (marker.Equals(":D", StringComparison.OrdinalIgnoreCase) ||
            marker.StartsWith(":)", StringComparison.Ordinal) ||
            marker.StartsWith(":-)", StringComparison.Ordinal) ||
            marker.StartsWith(")", StringComparison.Ordinal))
            return TtsIntonationStyle.Happy;

        if (marker.Equals(":-O", StringComparison.OrdinalIgnoreCase))
            return TtsIntonationStyle.Surprised;

        if (marker is ":/" or ":-/")
            return TtsIntonationStyle.Skeptical;

        return TtsIntonationStyle.Sad;
    }

    private static TtsIntonationStyle GetPunctuationStyle(string text)
    {
        var strongPunctuation = StrongPunctuationRegex().Match(text);
        if (strongPunctuation.Success)
        {
            var punctuation = strongPunctuation.Value;
            if (punctuation.Contains('?') && !punctuation.Contains('!') && punctuation.Count(c => c == '?') >= 3)
                return TtsIntonationStyle.Surprised;

            return TtsIntonationStyle.Intense;
        }

        if (ThoughtfulPunctuationRegex().IsMatch(text))
            return TtsIntonationStyle.Thoughtful;

        if (QuestionRegex().IsMatch(text))
            return TtsIntonationStyle.Question;

        if (ExclamationRegex().IsMatch(text))
            return TtsIntonationStyle.Exclamation;

        return TtsIntonationStyle.Neutral;
    }

    private static string NormalizeTerminalPunctuation(string text)
    {
        text = NormalizeStrongPunctuationRegex().Replace(text, match => match.Value.Contains('?') ? "?" : "!");
        text = NormalizeEllipsisRegex().Replace(text, "...");
        text = NormalizeCommaPauseRegex().Replace(text, ",");
        return text;
    }

    private static Prosody GetProsody(TtsIntonationStyle style)
    {
        return style switch
        {
            TtsIntonationStyle.Question => new Prosody("medium", "high", "medium", 150),
            TtsIntonationStyle.Exclamation => new Prosody("fast", "high", "loud", 120),
            TtsIntonationStyle.Intense => new Prosody("fast", "x-high", "x-loud", 180),
            TtsIntonationStyle.Thoughtful => new Prosody("slow", "low", "soft", 550),
            TtsIntonationStyle.Playful => new Prosody("slow", "high", "medium", 220),
            TtsIntonationStyle.Happy => new Prosody("fast", "high", "loud", 180),
            TtsIntonationStyle.Sad => new Prosody("slow", "low", "soft", 450),
            TtsIntonationStyle.Skeptical => new Prosody("slow", "low", "medium", 250),
            TtsIntonationStyle.Sarcastic => new Prosody("slow", "low", "medium", 300),
            TtsIntonationStyle.Surprised => new Prosody("fast", "x-high", "loud", 200),
            TtsIntonationStyle.Angry => new Prosody("fast", "low", "x-loud", 120),
            TtsIntonationStyle.Afraid => new Prosody("fast", "x-high", "soft", 250),
            _ => new Prosody("medium", "medium", "medium", 0),
        };
    }

    private readonly record struct Prosody(string Rate, string Pitch, string Volume, int PauseMs);
}

public readonly record struct TtsIntonationAnalysis(string Text, TtsIntonationStyle Style);

public enum TtsIntonationStyle : byte
{
    Neutral,
    Question,
    Exclamation,
    Intense,
    Thoughtful,
    Playful,
    Happy,
    Sad,
    Skeptical,
    Sarcastic,
    Surprised,
    Angry,
    Afraid,
}
