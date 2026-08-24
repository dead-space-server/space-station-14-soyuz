using Content.Server.DeadSpace._Soyuz.TTS;

namespace Content.Server.Corvax.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem
{
    // Kofeecheks expanded TTS intonation integration: LicenseRef-Kofeecheks
    private static string ToSsmlText(string text, TtsIntonationStyle style, bool isWhisper)
        => TtsIntonationFormatter.BuildSsml(text, style, isWhisper);
}
