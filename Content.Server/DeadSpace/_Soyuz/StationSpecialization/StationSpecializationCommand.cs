// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.DeadSpace._Soyuz.StationSpecialization;
using Content.Shared.Station.Components;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Редактирование и просмотр специализации станции
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class StationSpecializationCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public string Command => "stationspec";

    public string Description => "Показывает и меняет специализацию станции.";

    public string Help =>
        "stationspec - станции и их специализации\n" +
        "stationspec list - доступные специализации\n" +
        "stationspec set <станция> <id специализации> - сменить специализацию (станция указывается как id сущности; если станция одна, id станции можно не писать)";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var sub = args.Length == 0 ? "info" : args[0].ToLowerInvariant();

        switch (sub)
        {
            case "info":
                PrintStations(shell);
                break;
            case "list":
                PrintSpecializations(shell);
                break;
            case "set":
                Set(shell, args);
                break;
            default:
                shell.WriteError($"Неизвестное действие '{args[0]}'.\n{Help}");
                break;
        }
    }

    private List<EntityUid> GetStations()
    {
        var stations = new List<EntityUid>();
        var query = _entMan.EntityQueryEnumerator<StationDataComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            stations.Add(uid);
        }

        return stations;
    }

    private void PrintStations(IConsoleShell shell)
    {
        var stations = GetStations();
        if (stations.Count == 0)
        {
            shell.WriteLine("Станций нет.");
            return;
        }

        foreach (var station in stations)
        {
            var name = _entMan.GetComponent<MetaDataComponent>(station).EntityName;
            var specialization = _entMan.TryGetComponent(station, out StationSpecializationComponent? comp)
                ? comp.Specialization.Id
                : "нет";

            shell.WriteLine($"{_entMan.GetNetEntity(station)}: {name}, специализация: {specialization}");
        }
    }

    private void PrintSpecializations(IConsoleShell shell)
    {
        foreach (var spec in _proto.EnumeratePrototypes<StationSpecializationPrototype>().OrderBy(s => s.ID))
        {
            shell.WriteLine($"{spec.ID}: {spec.Name}");
        }
    }

    private void Set(IConsoleShell shell, string[] args)
    {
        EntityUid station;
        string specializationId;

        if (args.Length == 3)
        {
            if (!NetEntity.TryParse(args[1], out var netEntity) || !_entMan.TryGetEntity(netEntity, out var uid)
                || !_entMan.HasComponent<StationDataComponent>(uid))
            {
                shell.WriteError($"'{args[1]}' не является станцией. Список: stationspec");
                return;
            }

            station = uid.Value;
            specializationId = args[2];
        }
        else if (args.Length == 2 && GetStations() is { Count: 1 } single)
        {
            station = single[0];
            specializationId = args[1];
        }
        else
        {
            shell.WriteError($"Неверное число аргументов.\n{Help}");
            return;
        }

        if (!_entMan.System<StationSpecializationSystem>().SetSpecialization(station, specializationId, out var error))
        {
            shell.WriteError(error ?? "Не удалось сменить специализацию.");
            return;
        }

        var name = _entMan.GetComponent<MetaDataComponent>(station).EntityName;
        shell.WriteLine($"Станция «{name}»: специализация {specializationId}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(new[] { "info", "list", "set" }, "действие");
            case 2 when args[0].Equals("set", StringComparison.OrdinalIgnoreCase):
                return CompletionResult.FromHintOptions(
                    GetStations().Select(s => _entMan.GetNetEntity(s).ToString()),
                    "станция (id сущности)");
            case 3 when args[0].Equals("set", StringComparison.OrdinalIgnoreCase):
                return CompletionResult.FromHintOptions(
                    _proto.EnumeratePrototypes<StationSpecializationPrototype>().Select(s => s.ID),
                    "id специализации");
        }

        return CompletionResult.Empty;
    }
}