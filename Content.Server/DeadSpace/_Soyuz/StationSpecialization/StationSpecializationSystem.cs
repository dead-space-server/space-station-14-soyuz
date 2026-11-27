// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.Localization;
using Content.Shared.DeadSpace._Soyuz.StationSpecialization;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Research.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.StationSpecialization;

public sealed partial class StationSpecializationSystem : SharedStationSpecializationSystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;

    /// <summary>Счёт карго, на который идут пассивные деньги почты </summary>
    private const string CargoAccount = "Cargo";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<CallsignComponent, RadioSpeakerNameEvent>(OnRadioName);
        SubscribeLocalEvent<HumanoidAppearanceComponent, RadioSpeakerNameEvent>(OnHumanoidRadioName);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!IsEnabled)
            return;

        ApplyNamePrefixes();

        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        var interval = TimeSpan.FromSeconds(Math.Max(0.1f, Cfg.GetCVar(StationSpecializationCVars.PassiveInterval)));

        var query = EntityQueryEnumerator<StationSpecializationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!Proto.TryIndex(comp.Specialization, out var spec))
                continue;

            if (!comp.OneTimeApplied)
            {
                comp.OneTimeApplied = true;
                ApplyChemVendBonus(uid, spec);
            }

            if (comp.NextPassive == TimeSpan.Zero)
            {
                comp.NextPassive = now + interval;
                continue;
            }

            if (now < comp.NextPassive)
                continue;

            comp.NextPassive = now + interval;

            if (spec.Bonuses.Contains(StationSpecializationBonus.PassiveResearch))
                AddResearchPoints(uid, Cfg.GetCVar(StationSpecializationCVars.ResearchPoints));

            if (spec.Bonuses.Contains(StationSpecializationBonus.PassiveCargoMoney))
            {
                var money = Cfg.GetCVar(StationSpecializationCVars.CargoMoney);
                if (money != 0)
                    _cargo.UpdateBankAccount(uid, money, CargoAccount);
            }
        }
    }

    private void ApplyNamePrefixes()
    {
        if (!Cfg.GetCVar(StationSpecializationCVars.NamePrefixEnabled))
            return;

        var query = EntityQueryEnumerator<StationSpecializationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.NamePrefixApplied)
                continue;

            comp.NamePrefixApplied = true;

            if (!Proto.TryIndex(comp.Specialization, out var spec) || string.IsNullOrWhiteSpace(spec.NamePrefix))
                continue;

            var prefix = PrototypeLocHelper.Resolve(spec.NamePrefix);
            var name = MetaData(uid).EntityName;
            if (prefix.Length == 0 || name.Length == 0 || name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            _stationSystem.RenameStation(uid, $"{prefix} {name}", false);
        }
    }

    private void AddResearchPoints(EntityUid station, int points)
    {
        if (points == 0)
            return;

        var servers = EntityQueryEnumerator<ResearchServerComponent>();
        while (servers.MoveNext(out var server, out var serverComp))
        {
            if (serverComp.isTaipan || Station.GetOwningStation(server) != station)
                continue;

            _research.ModifyServerPoints(server, points, serverComp);
        }
    }

    partial void ApplyChemVendBonus(EntityUid station, StationSpecializationPrototype spec);

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        if (!TryGetStationSpecialization(ev.Mob, out var station, out var spec))
            return;

        if (ev.JobId != null && spec.SkillMessage != null && IsJobInDepartments(ev.JobId, spec.SkillDepartments))
            _chat.DispatchServerMessage(ev.Player, PrototypeLocHelper.Resolve(spec.SkillMessage));

        if (spec.Bonuses.Contains(StationSpecializationBonus.Callsigns) && TryComp<CallsignComponent>(ev.Mob, out var callsign))
            AssignCallsign(station.Value, (ev.Mob, callsign));
    }

    private bool IsJobInDepartments(string jobId, HashSet<ProtoId<DepartmentPrototype>> departments)
    {
        foreach (var departmentId in departments)
        {
            if (Proto.TryIndex(departmentId, out var department) && department.Roles.Contains(jobId))
                return true;
        }

        return false;
    }

    private void AssignCallsign(Entity<StationSpecializationComponent> station, Entity<CallsignComponent> ent)
    {
        if (ent.Comp.FixedValue is { } fixedValue)
        {
            ent.Comp.Assigned = fixedValue;
            return;
        }

        if (ent.Comp.Base is not { } callsignBase)
            return;

        if (ent.Comp.IsHead)
        {
            ent.Comp.Assigned = Loc.GetString("station-specialization-callsign-head", ("base", callsignBase));
            return;
        }

        var number = station.Comp.CallsignCounters.GetValueOrDefault(callsignBase) + 1;
        station.Comp.CallsignCounters[callsignBase] = number;
        ent.Comp.Assigned = Loc.GetString("station-specialization-callsign-numbered",
            ("base", callsignBase),
            ("number", number.ToString()));
    }

    private void OnRadioName(Entity<CallsignComponent> ent, ref RadioSpeakerNameEvent args)
    {
        if (IsEnabled && ent.Comp.Assigned is { } assigned)
        {
            args.Name = assigned;
            args.IsCallsign = true;
        }
    }

    /// <summary>
    /// Станция с позывными человек без позывного пишется в рации как НЕИЗВЕСТНЫЙ
    /// Если у него есть позывной, его поставит <see cref="OnRadioName"/>
    /// </summary>
    private void OnHumanoidRadioName(Entity<HumanoidAppearanceComponent> ent, ref RadioSpeakerNameEvent args)
    {
        if (args.IsCallsign || !IsEnabled)
            return;

        if (!TryGetStationSpecialization(ent.Owner, out _, out var spec)
            || !spec.Bonuses.Contains(StationSpecializationBonus.Callsigns))
        {
            return;
        }

        if (!Cfg.GetCVar(StationSpecializationCVars.CallsignUnknownForAll) && !HasComp<CallsignComponent>(ent))
            return;

        args.Name = Loc.GetString("station-specialization-callsign-unknown");
        args.IsCallsign = true;
    }

    private static string? ResolvePrefix(StationSpecializationPrototype spec)
    {
        return string.IsNullOrWhiteSpace(spec.NamePrefix) ? null : PrototypeLocHelper.Resolve(spec.NamePrefix);
    }

    public bool SetSpecialization(EntityUid station, string specializationId, out string? error)
    {
        error = null;

        if (!Proto.TryIndex(new ProtoId<StationSpecializationPrototype>(specializationId), out var newSpec))
        {
            error = $"Нет специализации с id '{specializationId}'.";
            return false;
        }

        var comp = EnsureComp<StationSpecializationComponent>(station);
        var name = MetaData(station).EntityName;

        if (comp.NamePrefixApplied
            && Proto.TryIndex(comp.Specialization, out var oldSpec)
            && ResolvePrefix(oldSpec) is { } oldPrefix
            && name.StartsWith(oldPrefix + " ", StringComparison.Ordinal))
        {
            name = name[(oldPrefix.Length + 1)..];
        }

        comp.Specialization = specializationId;
        comp.NextPassive = TimeSpan.Zero;
        comp.NamePrefixApplied = true;

        if (Cfg.GetCVar(StationSpecializationCVars.NamePrefixEnabled) && ResolvePrefix(newSpec) is { } newPrefix)
            name = $"{newPrefix} {name}";

        if (name != MetaData(station).EntityName)
            _stationSystem.RenameStation(station, name, false);

        return true;
    }

    /// <summary>Имя компонента специализации в реестре компонентов.</summary>
    public string SpecializationComponentName => _compFactory.GetComponentName<StationSpecializationComponent>();

    /// <summary>Строка для лобби по компонентам станций выбранной карты, null если специализации нет</summary>
    public string? GetLobbyText(IEnumerable<IComponent?>? stationComponents)
    {
        if (!IsEnabled || stationComponents == null)
            return null;

        foreach (var component in stationComponents)
        {
            if (component is StationSpecializationComponent comp && Proto.TryIndex(comp.Specialization, out var spec))
                return Loc.GetString("station-specialization-lobby", ("name", PrototypeLocHelper.Resolve(spec.Name)));
        }

        return null;
    }
}