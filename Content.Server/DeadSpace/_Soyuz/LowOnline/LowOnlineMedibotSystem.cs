// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Server.Station.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Components;
using Content.Shared.DeadSpace._Soyuz.LowOnline;
using Content.Shared.FixedPoint;
using Content.Shared.Silicons.Bots;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.LowOnline;

/// <summary>
/// Пока на станции нет живого медперсонала, медиботы после стандартной инъекции
/// дополнительно вводят лекарства под тип повреждений пациента. Как только медики появляются,
/// медибот снова вводит только своё стандартное лекарство (трикордразин).
/// </summary>
public sealed class LowOnlineMedibotSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly LowOnlineDepartmentSystem _departments = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private const string MedicalDepartment = "Medical";

    /// <summary>
    /// Сколько каждого дополнительного вещества вводится
    /// </summary>
    private static readonly FixedPoint2 ExtraAmount = FixedPoint2.New(5);

    private static readonly ProtoId<ReagentPrototype> Bicaridine = "Bicaridine";
    private static readonly ProtoId<ReagentPrototype> Dexalin = "Dexalin";
    private static readonly ProtoId<ReagentPrototype> Saline = "Saline";
    private static readonly ProtoId<ReagentPrototype> Dermaline = "Dermaline";

    public override void Initialize()
    {
        SubscribeLocalEvent<MedibotComponent, MedibotInjectedEvent>(OnInjected);
    }

    private void OnInjected(Entity<MedibotComponent> medibot, ref MedibotInjectedEvent args)
    {
        if (_station.GetOwningStation(medibot) == null)
            return;

        if (_departments.CountAlive(MedicalDepartment) > 0)
            return;

        if (!TryComp<DamageableComponent>(args.Target, out var damageable))
            return;

        if (!_solutionContainer.TryGetInjectableSolution(args.Target, out var injectable, out _))
            return;

        var groups = damageable.Damage.GetDamagePerGroup(_proto);
        var types = damageable.Damage.DamageDict;

        if (groups.GetValueOrDefault("Brute") > FixedPoint2.Zero)
            _solutionContainer.TryAddReagent(injectable.Value, Bicaridine, ExtraAmount, out _);

        if (groups.GetValueOrDefault("Burn") > FixedPoint2.Zero)
            _solutionContainer.TryAddReagent(injectable.Value, Dermaline, ExtraAmount, out _);

        if (types.GetValueOrDefault("Asphyxiation") > FixedPoint2.Zero)
            _solutionContainer.TryAddReagent(injectable.Value, Dexalin, ExtraAmount, out _);

        if (types.GetValueOrDefault("Bloodloss") > FixedPoint2.Zero)
            _solutionContainer.TryAddReagent(injectable.Value, Saline, ExtraAmount, out _);
    }
}