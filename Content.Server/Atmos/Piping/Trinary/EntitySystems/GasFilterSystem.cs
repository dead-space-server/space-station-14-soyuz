using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components; // DS14 - current engine keeps AtmosDeviceUpdateEvent server-side.
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Atmos.Piping.Trinary.Components;
using Content.Shared.Atmos.Piping.Trinary.EntitySystems;
using Content.Shared.Audio;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Trinary.EntitySystems;

[UsedImplicitly]
// DS14: Current engine uses explicit event subscriptions without system source generation.
public sealed class GasFilterSystem : SharedGasFilterSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSoundSystem = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        // DS14-start: Current engine does not support source-generated event subscriptions.
        SubscribeLocalEvent<GasFilterComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GasFilterComponent, AtmosDeviceUpdateEvent>(OnFilterUpdated);
        SubscribeLocalEvent<GasFilterComponent, AtmosDeviceDisabledEvent>(OnFilterLeaveAtmosphere);
        SubscribeLocalEvent<GasFilterComponent, GasAnalyzerScanEvent>(OnFilterAnalyzed);
        // DS14-end
    }

    private void OnInit(Entity<GasFilterComponent> ent, ref ComponentInit args)
    {
        UpdateAppearance(ent);
    }

    private void OnFilterUpdated(Entity<GasFilterComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!ent.Comp.Enabled
            || !_nodeContainer.TryGetNodes(ent.Owner, ent.Comp.Inlet, ent.Comp.Filter, ent.Comp.Outlet, out PipeNode? inletNode, out PipeNode? filterNode, out PipeNode? outletNode)
            || outletNode.Air.Pressure >= Atmospherics.MaxOutputPressure) // No need to transfer if target is full.
        {
            _ambientSoundSystem.SetAmbience(ent.Owner, false);
            return;
        }

        // We multiply the transfer rate in L/s by the seconds passed since the last process to get the liters.
        var transferVol = ent.Comp.TransferRate * _atmosphereSystem.PumpSpeedup() * args.dt;

        if (transferVol <= 0)
        {
            _ambientSoundSystem.SetAmbience(ent.Owner, false);
            return;
        }

        var removed = inletNode.Air.RemoveVolume(transferVol);

        if (ent.Comp.FilteredGas.HasValue)
        {
            // DS14-Soyuz-start
            var filteredOut = new GasMixture() { Temperature = removed.Temperature };

            filteredOut.SetMoles(ent.Comp.FilteredGas.Value, removed.GetMoles(ent.Comp.FilteredGas.Value));
            removed.SetMoles(ent.Comp.FilteredGas.Value, 0f);

            var target = filterNode.Air.Pressure < Atmospherics.MaxOutputPressure ? filterNode : inletNode;
            _atmosphereSystem.Merge(target.Air, filteredOut);
            _ambientSoundSystem.SetAmbience(ent.Owner, filteredOut.TotalMoles > 0f);
        }

        _atmosphereSystem.Merge(outletNode.Air, removed);
        // DS14-Soyuz-end
    }

    private void OnFilterLeaveAtmosphere(Entity<GasFilterComponent> ent, ref AtmosDeviceDisabledEvent args)
    {
        ent.Comp.Enabled = false;

        DirtyField(ent.Owner, ent.Comp, nameof(GasFilterComponent.Enabled));
        UpdateAppearance(ent);
        _ambientSoundSystem.SetAmbience(ent.Owner, false);
        _ui.CloseUi(ent.Owner, GasFilterUiKey.Key);
    }

    /// <summary>
    /// Returns the gas mixture for the gas analyzer
    /// </summary>
    private void OnFilterAnalyzed(Entity<GasFilterComponent> ent, ref GasAnalyzerScanEvent args)
    {
        args.GasMixtures ??= new List<(string, GasMixture?)>();

        // multiply by volume fraction to make sure to send only the gas inside the analyzed pipe element, not the whole pipe system
        if (_nodeContainer.TryGetNode(ent.Owner, ent.Comp.Inlet, out PipeNode? inlet) && inlet.Air.Volume != 0f)
        {
            var inletAirLocal = inlet.Air.Clone();
            inletAirLocal.Multiply(inlet.Volume / inlet.Air.Volume);
            inletAirLocal.Volume = inlet.Volume;
            args.GasMixtures.Add((Loc.GetString("gas-analyzer-window-text-inlet"), inletAirLocal));
        }
        if (_nodeContainer.TryGetNode(ent.Owner, ent.Comp.Filter, out PipeNode? filterNode) && filterNode.Air.Volume != 0f)
        {
            var filterNodeAirLocal = filterNode.Air.Clone();
            filterNodeAirLocal.Multiply(filterNode.Volume / filterNode.Air.Volume);
            filterNodeAirLocal.Volume = filterNode.Volume;
            args.GasMixtures.Add((Loc.GetString("gas-analyzer-window-text-filter"), filterNodeAirLocal));
        }
        if (_nodeContainer.TryGetNode(ent.Owner, ent.Comp.Outlet, out PipeNode? outlet) && outlet.Air.Volume != 0f)
        {
            var outletAirLocal = outlet.Air.Clone();
            outletAirLocal.Multiply(outlet.Volume / outlet.Air.Volume);
            outletAirLocal.Volume = outlet.Volume;
            args.GasMixtures.Add((Loc.GetString("gas-analyzer-window-text-outlet"), outletAirLocal));
        }

        args.DeviceFlipped = inlet != null && filterNode != null && inlet.CurrentPipeDirection.ToDirection() == filterNode.CurrentPipeDirection.ToDirection().GetClockwise90Degrees();
    }
}
