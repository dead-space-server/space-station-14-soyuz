// SPDX-FileCopyrightText: 2026 Ezio50
// SPDX-License-Identifier: LicenseRef-Ezio50

using System.Linq;
using System.Numerics;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Chemistry.TileReactions;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Radiation.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    private static readonly ProtoId<ReagentPrototype> SoyuzIneyReagent = "Iney";

    [Dependency] private readonly SharedPointLightSystem _soyuzLights = default!;
    private readonly Dictionary<TileAtmosphere, SoyuzTileState> _soyuzTiles = new();
    private readonly ITileReaction _soyuzCleanDecals = new CleanDecalsReaction { OnlyCurrentTile = true };
    private readonly ITileReaction _soyuzCleanPuddles = new CleanTileReaction { OnlyCurrentTile = true };
    private float _soyuzUpdateTimer;
    private int _soyuzDetonationsRemaining;

    public bool TryGetSoyuzGasReactionCoordinates(IGasMixtureHolder? holder, out MapCoordinates coordinates)
    {
        if (holder is Component component)
        {
#pragma warning disable CS0618
            coordinates = _transformSystem.GetMapCoordinates(component.Owner);
#pragma warning restore CS0618
            return coordinates.MapId != MapId.Nullspace;
        }
        return TryGetGasReactionCoordinates(holder, out coordinates);
    }

    private sealed class SoyuzTileState
    {
        public EntityUid? Source;
        public TimeSpan? Detonation;
        public bool PendingDetonation;
    }

    private static bool HasSoyuzTileEffect(GasMixture air)
    {
        return air.GetMoles(Gas.Radion) > Atmospherics.GasMinMoles ||
               air.GetMoles(Gas.Lumin) > Atmospherics.GasMinMoles ||
               air.GetMoles(Gas.Tlec) > Atmospherics.GasMinMoles ||
               air.GetMoles(Gas.Iney) > Atmospherics.GasMinMoles ||
               air.GetMoles(Gas.QuartzGas) >= 10f;
    }

    private void ProcessSoyuzTile(TileAtmosphere tile)
    {
        if (tile.Air is not { Immutable: false } air || !HasSoyuzTileEffect(air))
            return;

        if (!_soyuzTiles.TryGetValue(tile, out var state))
            _soyuzTiles.Add(tile, state = new SoyuzTileState());
        if (air.GetMoles(Gas.Tlec) > Atmospherics.GasMinMoles)
            state.Detonation ??= _gameTiming.CurTime + TimeSpan.FromSeconds(12);

        ProcessSoyuzCondensates(tile, air);
    }

    private void ProcessSoyuzCondensates(TileAtmosphere tile, GasMixture air)
    {
        var frost = air.GetMoles(Gas.Iney);
        if (frost > 0f && TryComp<MapGridComponent>(tile.GridIndex, out var grid))
        {
            var converted = frost < 0.01f ? frost : frost * 0.5f;
            air.AdjustMoles(Gas.Iney, -converted);
            air.AdjustMoles(Gas.WaterVapor, converted);
            var tileRef = _map.GetTileRef(tile.GridIndex, grid, tile.GridIndices);
            var reagent = _protoMan.Index(SoyuzIneyReagent);
            var volume = FixedPoint2.New(Math.Clamp(converted * 50f, 5f, 100f));
            _soyuzCleanDecals.TileReact(tileRef, reagent, volume, EntityManager, null);
            _soyuzCleanPuddles.TileReact(tileRef, reagent, volume, EntityManager, null);
            InvalidateVisuals(tile.GridIndex, tile.GridIndices);
        }

        if (air.Temperature < 80f && air.GetMoles(Gas.QuartzGas) >= 10f)
        {
            var count = (int) MathF.Min(MathF.Floor(air.GetMoles(Gas.QuartzGas) / 10f), 10f);
            for (var i = 0; i < count; i++)
                Spawn("SoyuzQuartzCrystal", new EntityCoordinates(tile.GridIndex, (Vector2) tile.GridIndices + new Vector2(0.5f)));
            air.AdjustMoles(Gas.QuartzGas, -count * 10f);
            InvalidateVisuals(tile.GridIndex, tile.GridIndices);
        }
    }

    private void UpdateSoyuzTiles(float frameTime)
    {
        _soyuzUpdateTimer += frameTime;
        if (_soyuzUpdateTimer < 0.5f)
            return;
        var elapsed = _soyuzUpdateTimer;
        _soyuzUpdateTimer = 0f;
        _soyuzDetonationsRemaining = 64;

        foreach (var (tile, state) in _soyuzTiles.ToArray())
        {
            if (!TryComp<GridAtmosphereComponent>(tile.GridIndex, out var gridAtmos) ||
                !gridAtmos.Tiles.TryGetValue(tile.GridIndices, out var currentTile) || currentTile != tile ||
                tile.Air is not { Immutable: false } air || !HasSoyuzTileEffect(air))
            {
                if (state.Source is { } removed && Exists(removed))
                    QueueDel(removed);
                _soyuzTiles.Remove(tile);
                continue;
            }

            if (Paused(tile.GridIndex))
            {
                if (state.Detonation != null)
                    state.Detonation += TimeSpan.FromSeconds(elapsed);
                continue;
            }

            GetTileMixture(tile.GridIndex, null, tile.GridIndices, true);
            var radion = air.GetMoles(Gas.Radion);
            var lumin = air.GetMoles(Gas.Lumin);
            if (radion > Atmospherics.GasMinMoles || lumin > Atmospherics.GasMinMoles)
            {
                if (state.Source is not { } existing || !Exists(existing))
                    state.Source = Spawn("SoyuzAtmosSource", new EntityCoordinates(tile.GridIndex, (Vector2) tile.GridIndices + new Vector2(0.5f)));
                var source = state.Source.Value;
                var radiation = Comp<RadiationSourceComponent>(source);
                radiation.Intensity = MathF.Min(radion * 0.02f, 20f);
                radiation.Enabled = radion > Atmospherics.GasMinMoles;
                _soyuzLights.SetEnabled(source, lumin > Atmospherics.GasMinMoles);
                _soyuzLights.SetEnergy(source, Math.Clamp(lumin / 10f, 0f, 4f));
            }
            else if (state.Source is { } source)
            {
                QueueDel(source);
                state.Source = null;
            }

            if (air.GetMoles(Gas.Tlec) <= Atmospherics.GasMinMoles)
            {
                state.Detonation = null;
                state.PendingDetonation = false;
            }
            else
                state.Detonation ??= _gameTiming.CurTime + TimeSpan.FromSeconds(12);

            InvalidateVisuals(tile.GridIndex, tile.GridIndices);
            if (state.Detonation <= _gameTiming.CurTime || state.PendingDetonation)
                DetonateSoyuzTlec(tile);
        }
    }

    public byte GetSoyuzTlecStage(TileAtmosphere tile)
    {
        if (!_soyuzTiles.TryGetValue(tile, out var state) || state.Detonation is not { } end ||
            tile.Air == null || tile.Air.GetMoles(Gas.Tlec) <= Atmospherics.GasMinMoles)
            return 0;
        var elapsed = 12 - (end - _gameTiming.CurTime).TotalSeconds;
        return elapsed >= 11 ? (byte) 5 : (byte) Math.Clamp(1 + (int) (elapsed / 3), 1, 4);
    }

    private void DetonateSoyuzTlec(TileAtmosphere initial)
    {
        var queue = new Queue<TileAtmosphere>();
        var seen = new HashSet<TileAtmosphere>();
        queue.Enqueue(initial);
        while (queue.TryDequeue(out var tile))
        {
            if (!seen.Add(tile) || tile.Air is not { Immutable: false } air ||
                air.GetMoles(Gas.Tlec) <= Atmospherics.GasMinMoles ||
                !_soyuzTiles.TryGetValue(tile, out var state) || state.Detonation == null)
                continue;
            if (_soyuzDetonationsRemaining == 0)
            {
                state.PendingDetonation = true;
                continue;
            }
            _soyuzDetonationsRemaining--;

            for (var i = 0; i < Atmospherics.Directions; i++)
                if (((int) tile.AdjacentBits & (1 << i)) != 0 && tile.AdjacentTiles[i] is { } adjacent)
                    queue.Enqueue(adjacent);

            var fuel = air.GetMoles(Gas.Tlec);
            // Never exceed HydrogenFireReaction's current 0.25/mol coefficient.
            var baseIntensity = fuel * MathF.Min(0.3f, 0.25f);
            var cap = fuel switch
            {
                < 50f => 10f,
                < 150f => 20f,
                < 1000f => 20f + (fuel - 150f) / 850f * 15f,
                _ => 35f,
            };
            air.SetMoles(Gas.Tlec, 0f);
            state.Detonation = null;
            state.PendingDetonation = false;
            Explosion.QueueExplosion(_transformSystem.ToMapCoordinates(new EntityCoordinates(tile.GridIndex, (Vector2) tile.GridIndices + new Vector2(0.5f))),
                "Default", MathF.Min(baseIntensity, cap), 3f, 6f, cause: null, addLog: false);
            InvalidateVisuals(tile.GridIndex, tile.GridIndices);
        }
    }
}
