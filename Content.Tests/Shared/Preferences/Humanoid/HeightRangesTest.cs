using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using NUnit.Framework;

namespace Content.Tests.Shared.Preferences.Humanoid;

[TestFixture]
public sealed class HeightRangesTest
{
    [TestCase("Arachnid", Sex.Unsexed, 170, 180, 220)]
    [TestCase("Demon", Sex.Female, 160, 178, 220)]
    [TestCase("Demon", Sex.Male, 170, 190, 210)]
    [TestCase("Vox", Sex.Unsexed, 140, 150, 160)]
    [TestCase("Vulpkanin", Sex.Female, 150, 174, 210)]
    [TestCase("Vulpkanin", Sex.Male, 160, 184, 220)]
    [TestCase("Dwarf", Sex.Male, 146, 151, 156)]
    [TestCase("Diona", Sex.Male, 170, 180, 220)]
    [TestCase("Kobolt", Sex.Male, 140, 150, 160)]
    [TestCase("IPC", Sex.Unsexed, 175, 185, 205)]
    [TestCase("Xenomorph", Sex.Unsexed, 180, 210, 220)]
    [TestCase("Human", Sex.Male, 160, 175, 195)]
    [TestCase("Moth", Sex.Male, 150, 175, 180)]
    [TestCase("Shark", Sex.Male, 175, 205, 215)]
    [TestCase("SlimePerson", Sex.Male, 140, 170, 220)]
    [TestCase("Tajaran", Sex.Male, 145, 170, 200)]
    [TestCase("Reptilian", Sex.Male, 190, 200, 210)]
    [TestCase("Felinid", Sex.Male, 140, 150, 160)]
    [TestCase("Goblin", Sex.Male, 140, 150, 155)]
    [TestCase("Harpy", Sex.Male, 140, 157, 175)]
    [TestCase("Ariral", Sex.Male, 165, 190, 220)]
    [TestCase("Oni", Sex.Male, 190, 205, 220)]
    public void TestSpeciesHeightRanges(string species, Sex sex, int min, int expectedDefault, int max)
    {
        var range = HumanoidCharacterProfile.GetHeightRange(species, sex);

        Assert.That(range.Min, Is.EqualTo(min));
        Assert.That(range.Default, Is.EqualTo(expectedDefault));
        Assert.That(range.Max, Is.EqualTo(max));
    }

    [Test]
    public void TestHeightClampsWhenSpeciesChanges()
    {
        var profile = new HumanoidCharacterProfile()
            .WithSpecies("Human")
            .WithHeight(195)
            .WithSpecies("Dwarf");

        Assert.That(profile.Height, Is.EqualTo(156));
    }

    [Test]
    public void TestHeightClampsWhenSexChanges()
    {
        var profile = new HumanoidCharacterProfile()
            .WithSpecies("Demon")
            .WithHeight(210)
            .WithSex(Sex.Female);

        Assert.That(profile.Height, Is.EqualTo(210));
    }

    [TestCase("Human", Sex.Male, 175)]
    [TestCase("Dwarf", Sex.Male, 151)]
    [TestCase("Moth", Sex.Male, 175)]
    [TestCase("Shark", Sex.Male, 205)]
    [TestCase("Vulpkanin", Sex.Female, 174)]
    [TestCase("Goblin", Sex.Male, 150)]
    [TestCase("Harpy", Sex.Male, 157)]
    [TestCase("Ariral", Sex.Male, 190)]
    [TestCase("Oni", Sex.Male, 205)]
    public void TestAverageHeightKeepsNeutralScale(string species, Sex sex, int height)
    {
        Assert.That(HumanoidCharacterProfile.HeightToScale(species, sex, height), Is.EqualTo(1f).Within(0.0001f));
    }
}
