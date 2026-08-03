// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Server.DeadSpace._Soyuz.TTS;
using NUnit.Framework;

namespace Content.Tests.Server.DeadSpace.Soyuz;

[TestFixture]
public sealed class TtsIntonationFormatterTests
{
    [TestCase("The shift is over.", TtsIntonationStyle.Neutral)]
    [TestCase("Where is the captain?", TtsIntonationStyle.Question)]
    [TestCase("Stop!", TtsIntonationStyle.Exclamation)]
    [TestCase("What?!", TtsIntonationStyle.Intense)]
    [TestCase("What???", TtsIntonationStyle.Surprised)]
    [TestCase("Stop!!!", TtsIntonationStyle.Intense)]
    [TestCase("Maybe...", TtsIntonationStyle.Thoughtful)]
    [TestCase("Well,,", TtsIntonationStyle.Thoughtful)]
    public void PunctuationSelectsExpectedStyle(string text, TtsIntonationStyle expected)
    {
        Assert.That(TtsIntonationFormatter.Analyze(text).Style, Is.EqualTo(expected));
    }

    [TestCase("Hello~", TtsIntonationStyle.Playful, "Hello")]
    [TestCase("Hello^^", TtsIntonationStyle.Playful, "Hello")]
    [TestCase("Hello)))", TtsIntonationStyle.Happy, "Hello")]
    [TestCase("Hello:(", TtsIntonationStyle.Sad, "Hello")]
    [TestCase("Hello:-/", TtsIntonationStyle.Skeptical, "Hello")]
    [TestCase("Hello:-O", TtsIntonationStyle.Surprised, "Hello")]
    [TestCase("Obviously /s", TtsIntonationStyle.Sarcastic, "Obviously")]
    public void TextMarkersAreAppliedButNotSpoken(
        string text,
        TtsIntonationStyle expectedStyle,
        string expectedText)
    {
        var result = TtsIntonationFormatter.Analyze(text);

        Assert.That(result.Style, Is.EqualTo(expectedStyle));
        Assert.That(result.Text, Is.EqualTo(expectedText));
    }

    [Test]
    public void EmojiIsAppliedButNotSpoken()
    {
        var result = TtsIntonationFormatter.Analyze("Hello \U0001F604");

        Assert.That(result.Style, Is.EqualTo(TtsIntonationStyle.Happy));
        Assert.That(result.Text, Is.EqualTo("Hello"));
    }

    [Test]
    public void StrongPunctuationIsNormalizedForSpeech()
    {
        var result = TtsIntonationFormatter.Analyze("What!?!?");

        Assert.That(result.Style, Is.EqualTo(TtsIntonationStyle.Intense));
        Assert.That(result.Text, Is.EqualTo("What?"));
    }

    [Test]
    public void SsmlEscapesTextAndAppliesWhisper()
    {
        var ssml = TtsIntonationFormatter.BuildSsml("<hello & goodbye>", TtsIntonationStyle.Happy, true);

        Assert.That(ssml, Does.Contain("&lt;hello &amp; goodbye&gt;"));
        Assert.That(ssml, Does.Contain("pitch=\"x-low\""));
        Assert.That(ssml, Does.Contain("volume=\"x-soft\""));
        Assert.That(ssml, Does.StartWith("<speak>"));
        Assert.That(ssml, Does.EndWith("</speak>"));
    }
}
