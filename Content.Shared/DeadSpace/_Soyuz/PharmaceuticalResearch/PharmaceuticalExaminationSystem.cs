// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;

namespace Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed class PharmaceuticalExaminationSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SolutionContainerManagerComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(Entity<SolutionContainerManagerComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || (!HasComp<FitsInDispenserComponent>(ent.Owner) &&
            !HasComp<PillComponent>(ent.Owner) && !HasComp<InjectorComponent>(ent.Owner)))
            return;
        foreach (var (_, solution) in _solutions.EnumerateSolutions(ent.AsNullable()))
        foreach (var (reagent, amount) in solution.Comp.Solution.Contents)
        {
            if (reagent.Prototype != PharmaceuticalData.PrototypeId || reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is not { } data)
                continue;
            args.PushText(Loc.GetString("pharma-examine-product", ("name", string.IsNullOrEmpty(data.Label) ? data.Formula : data.Label),
                ("id", data.Formula), ("quality", data.Quality.ToString("0.0", CultureInfo.InvariantCulture)), ("amount", amount.Float().ToString("0.0", CultureInfo.InvariantCulture))));
        }
    }
}
