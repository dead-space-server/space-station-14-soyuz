using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.DeadSpace.Virus.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DeadSpace.Soyuz.Sterility;
using Content.Shared.DeadSpace.Soyuz.Sterility.Components;
using Content.Shared.DeadSpace.Virus.Components;
using Content.Shared.DeadSpace.Virus.Prototypes;
using Content.Shared.Examine;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Soyuz.Sterility.EntitySystems;

public sealed class SterilitySystem : EntitySystem
{
    private const float MaxContamination = 100f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly VirusSystem _virus = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SterilityComponent, InjectorTransferCompletedEvent>(OnInjectorTransferCompleted);
        SubscribeLocalEvent<SterilityComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SterilityComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.IsOpened ||
                component.ExposureInterval <= TimeSpan.Zero ||
                component.ExposurePerTick <= 0f ||
                component.NextExposureTick == TimeSpan.Zero ||
                _timing.CurTime < component.NextExposureTick)
            {
                continue;
            }

            while (_timing.CurTime >= component.NextExposureTick)
            {
                component.Contamination = MathF.Min(MaxContamination, component.Contamination + component.ExposurePerTick);
                component.NextExposureTick += component.ExposureInterval;
            }

            Dirty(uid, component);
        }
    }

    private void OnInjectorTransferCompleted(Entity<SterilityComponent> ent, ref InjectorTransferCompletedEvent args)
    {
        OpenSterility(ent);
        ent.Comp.Contamination = MathF.Min(MaxContamination, ent.Comp.Contamination + ent.Comp.PerUseIncrease);

        if (TryGetTrace(args, out var trace))
            ent.Comp.StoredVirusData = trace;

        Dirty(ent);

        if (args.Kind != InjectorTransferKind.Inject || ent.Comp.Contamination <= 0f)
            return;

        var data = GetTraceForInfection(ent);
        data.Infectivity = Math.Clamp(ent.Comp.Contamination / 100f, 0f, 1f);
        _virus.ProbInfect(data, args.Target, ent.Owner);
    }

    private void OnExamined(Entity<SterilityComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var level = ent.Comp.Contamination switch
        {
            <= 0f => "sterility-examine-sterile",
            < 50f => "sterility-examine-moderate",
            _ => "sterility-examine-critical"
        };

        args.PushMarkup(Loc.GetString(level));
    }

    public void OpenSterility(Entity<SterilityComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.IsOpened)
            return;

        ent.Comp.IsOpened = true;
        if (ent.Comp.ExposureInterval > TimeSpan.Zero && ent.Comp.NextExposureTick == TimeSpan.Zero)
            ent.Comp.NextExposureTick = _timing.CurTime + ent.Comp.ExposureInterval;

        Dirty(ent);
    }

    public void ResetSterility(Entity<SterilityComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.Contamination = 0f;
        ent.Comp.IsOpened = false;
        ent.Comp.NextExposureTick = TimeSpan.Zero;
        ent.Comp.StoredVirusData = null;
        Dirty(ent);
    }

    private bool TryGetTrace(InjectorTransferCompletedEvent args, out VirusData? trace)
    {
        trace = GetVirusData(args.TransferredSolution);
        if (trace != null)
            return true;

        if (!TryComp<VirusComponent>(args.Target, out var virus))
            return false;

        trace = (VirusData) virus.Data.Clone();
        return true;
    }

    private VirusData GetTraceForInfection(Entity<SterilityComponent> ent)
    {
        if (ent.Comp.StoredVirusData == null)
            ent.Comp.StoredVirusData = GenerateFallbackVirus();

        return (VirusData) ent.Comp.StoredVirusData.CloneForInfection();
    }

    private VirusData? GetVirusData(Content.Shared.Chemistry.Components.Solution solution)
    {
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Reagent.Data == null)
                continue;

            foreach (var data in reagent.Reagent.Data)
            {
                if (data is VirusData virusData)
                    return (VirusData) virusData.Clone();
            }
        }

        return null;
    }

    private VirusData GenerateFallbackVirus()
    {
        var data = _virus.GenerateVirusData(
            _virus.GenerateStrainId(),
            new Dictionary<DangerIndicatorSymptom, int>
            {
                [DangerIndicatorSymptom.Low] = 1
            },
            int.MaxValue);

        return (VirusData) data.Clone();
    }
}
