// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System;
using Content.Server.Atmos.Portable;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.DeadSpace._Soyuz.Atmos;
using NUnit.Framework;
using static Content.Shared.Atmos.EntitySystems.SharedGasTileOverlaySystem;

namespace Content.Tests.DeadSpace._Soyuz.Atmos;

[TestFixture]
public sealed class SoyuzGasTest
{
    [Test]
    public void ScrubbersCollectAllGasesExceptStationAir()
    {
        var portable = new PortableScrubberComponent();
        foreach (var gas in Enum.GetValues<Gas>())
        {
            var collected = gas is not (Gas.Oxygen or Gas.Nitrogen);
            Assert.That(portable.FilterGases.Contains(gas), Is.EqualTo(collected), $"Portable: {gas}");
            Assert.That(GasVentScrubberData.FilterModePreset.FilterGases.Contains(gas), Is.EqualTo(collected), $"Fixed: {gas}");
            Assert.That(GasVentScrubberData.WideFilterModePreset.FilterGases.Contains(gas), Is.EqualTo(collected), $"Wide: {gas}");
        }
    }

    [Test]
    public void ScrubberSettingsDoNotChangeOtherDevicesOrPresets()
    {
        var first = new PortableScrubberComponent();
        var second = new PortableScrubberComponent();
        first.FilterGases.Clear();
        var fixedScrubber = new GasVentScrubberData();
        fixedScrubber.FilterGases.Clear();
        Assert.That(second.FilterGases, Does.Contain(Gas.Sumrak));
        Assert.That(GasVentScrubberData.DefaultFilterGases, Does.Contain(Gas.Sumrak));
        Assert.That(GasVentScrubberData.FilterModePreset.FilterGases, Does.Contain(Gas.Sumrak));
    }

    [Test]
    public void ImpulseBonusDoesNotStackAndRestoresGeneratorOutput()
    {
        var supplier = new PowerSupplierComponent();
        supplier.MaxSupply = 1000f;
        supplier.SetSoyuzOutputMultiplier(1.25f);
        supplier.SetSoyuzOutputMultiplier(1.25f);
        Assert.That(supplier.NetworkSupply.MaxSupply, Is.EqualTo(1250f));
        supplier.MaxSupply = 2000f;
        Assert.That(supplier.NetworkSupply.MaxSupply, Is.EqualTo(2500f));
        supplier.SetSoyuzOutputMultiplier(1f);
        Assert.That(supplier.NetworkSupply.MaxSupply, Is.EqualTo(2000f));
    }

    [Test]
    public void GasVisualsDisappearAfterRemovingTheRelevantGas()
    {
        var air = new GasMixture();
        air.SetMoles(Gas.Sumrak, 50f);
        air.SetMoles(Gas.Chromatin, 1f);
        air.SetMoles(Gas.Tritium, 10f);
        Assert.That(SoyuzGasVisuals.Darkness(air), Is.EqualTo(230));
        Assert.That(SoyuzGasVisuals.GetFireColor(air), Is.EqualTo(2));
        air.SetMoles(Gas.Sumrak, 0f);
        air.SetMoles(Gas.Chromatin, 0f);
        Assert.That(SoyuzGasVisuals.Darkness(air), Is.Zero);
        Assert.That(SoyuzGasVisuals.GetFireColor(air), Is.Zero);
    }

    [TestCase(1, 0, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, 1)]
    public void GasVisualChangesInvalidateOverlayData(byte stage, byte fire, byte darkness)
    {
        var original = new GasOverlayData(0, new byte[1], default);
        var changed = new GasOverlayData(0, new byte[1], default, soyuzTlecStage: stage, soyuzFireColor: fire, soyuzDarkness: darkness);
        Assert.That(original.Equals(changed), Is.False);
    }
}
