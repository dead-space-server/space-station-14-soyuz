// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.AlertLevel;
using Content.Server.Atmos.Components;
using Content.Server.Chat.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.DeadSpace._Soyuz.Lockdown;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Implants.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.Lockdown;

public sealed class LockdownConsoleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SharedDoorSystem _doors = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    private static readonly EntProtoId HologramPrototype = "LockdownDoorHologram";
    private const string StationId = "station";

    private TimeSpan _nextUiUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<LockdownDoorComponent, BeforeDoorOpenedEvent>(OnBeforeDoorOpened);
        SubscribeLocalEvent<LockdownDoorComponent, BeforeDoorClosedEvent>(OnBeforeDoorClosed);
        SubscribeLocalEvent<LockdownFreePassImplantComponent, LockdownFreePassEvent>(OnFreePass);
        SubscribeLocalEvent<ImplantedComponent, LockdownFreePassEvent>(OnFreePassOnUser);
        SubscribeLocalEvent<LockdownBinLockComponent, ContainerIsInsertingAttemptEvent>(OnBinInsertAttempt);

        Subs.BuiEvents<LockdownConsoleComponent>(LockdownConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<LockdownStartDepartmentMessage>(OnStartDepartment);
            subs.Event<LockdownCancelDepartmentMessage>(OnCancelDepartment);
            subs.Event<LockdownConfirmStationMessage>(OnConfirmStation);
            subs.Event<LockdownCancelStationMessage>(OnCancelStation);
            subs.Event<SecurityConsoleOpenSectionMessage>(OnOpenSection);
        });
    }

    #region Updates

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<LockdownStationComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            UpdateStation(uid, state, now);
        }

        var doors = EntityQueryEnumerator<LockdownDoorComponent>();
        while (doors.MoveNext(out var uid, out var door))
        {
            UpdateFreePass(uid, door, now);
        }

        if (now < _nextUiUpdate)
            return;

        _nextUiUpdate = now + TimeSpan.FromSeconds(1);

        var consoles = EntityQueryEnumerator<LockdownConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var comp))
        {
            UpdateUi(uid, comp);
        }
    }

    private void UpdateStation(EntityUid station, LockdownStationComponent state, TimeSpan now)
    {
        foreach (var pair in state.Confirmations.Where(c => c.Value.ExpiresAt <= now).ToArray())
        {
            state.Confirmations.Remove(pair.Key);
        }

        foreach (var (id, instance) in state.Departments.ToArray())
        {
            if (instance.Phase == LockdownPhase.Pending && now >= instance.ActivateAt)
            {
                Activate(station, state, instance, now);
            }
            else if (instance.Phase == LockdownPhase.Active && now >= instance.EndAt)
            {
                var name = DepartmentName(id);
                Finish(state, instance);
                state.Departments.Remove(id);
                Announce(station, instance.Console, Loc.GetString("lockdown-lifted-department", ("department", name)));
            }
        }

        var stationInstance = state.Station;
        if (stationInstance == null)
            return;

        if (stationInstance.Phase == LockdownPhase.Pending && now >= stationInstance.ActivateAt)
        {
            Activate(station, state, stationInstance, now);
        }
        else if (stationInstance.Phase == LockdownPhase.Active && now >= stationInstance.EndAt)
        {
            Finish(state, stationInstance);
            state.Station = null;
            Announce(station, stationInstance.Console, Loc.GetString("lockdown-lifted-station"));
        }
    }

    private void Activate(EntityUid station, LockdownStationComponent state, LockdownInstance instance, TimeSpan now)
    {
        instance.Phase = LockdownPhase.Active;
        instance.EndAt = now + instance.Duration;
        state.ConsoleLockedUntil = now + instance.SuccessCooldown;

        HashSet<string>? access = null;
        if (instance.DepartmentId != null && _proto.TryIndex<LockdownDepartmentPrototype>(instance.DepartmentId, out var dept))
            access = dept.Access.Select(a => a.ToString()).ToHashSet();

        var doors = CollectDoors(station, access, instance.DepartmentId != null);

        foreach (var door in doors)
        {
            // Дверь запоминаем сразу, чтобы при отмене с неё в любом случае снялся счётчик блокировок.
            instance.Doors.Add(door);

            try
            {
                ApplyLock(door);
            }
            catch (Exception e)
            {
                Log.Error($"Не удалось заблокировать дверь {ToPrettyString(door)}: {e}");
            }
        }

        foreach (var bin in CollectBins(station, access, instance.DepartmentId != null))
        {
            instance.Bins.Add(bin);
            EnsureComp<LockdownBinLockComponent>(bin).Locks++;
        }

        Log.Info($"Локдаун ({instance.DepartmentId ?? StationId}) активирован: дверей {instance.Doors.Count}, мусорок {instance.Bins.Count}.");

        if (doors.Count == 0)
        {
            Log.Warning(
                $"Локдаун ({instance.DepartmentId ?? StationId}): не найдено ни одной двери. " +
                "Проверьте, что двери стоят на сетке станции (GetOwningStation) и, для отдела, что их AccessReader содержит доступ отдела.");
        }
    }

    /// <summary>
    /// Снимает блокировку со всех дверей экземпляра. Из словарей состояния экземпляр убирает вызывающий.
    /// </summary>
    private void Finish(LockdownStationComponent state, LockdownInstance instance)
    {
        foreach (var door in instance.Doors)
        {
            if (Exists(door))
                ReleaseLock(door);
        }

        instance.Doors.Clear();

        foreach (var bin in instance.Bins)
        {
            if (!TryComp<LockdownBinLockComponent>(bin, out var binLock))
                continue;

            binLock.Locks--;
            if (binLock.Locks <= 0)
                RemComp<LockdownBinLockComponent>(bin);
        }

        instance.Bins.Clear();
    }

    #endregion

    #region Doors

    private List<EntityUid> CollectDoors(EntityUid station, HashSet<string>? access, bool departmentOnly)
    {
        var result = new List<EntityUid>();

        // Счётчики для диагностики: показывают, на каком фильтре отсеиваются двери.
        var total = 0;
        var skipped = 0;
        var onStation = 0;
        var withReader = 0;
        var seenAccess = new HashSet<string>();

        var query = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            total++;

            // Общие шлюзы коридоров и противопожарные шлюзы не трогаем.
            if (HasComp<LockdownImmuneComponent>(uid) || HasComp<FirelockComponent>(uid))
            {
                skipped++;
                continue;
            }

            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            onStation++;

            var readers = GetAccessReaders(uid);
            if (readers.Count > 0)
            {
                withReader++;

                foreach (var reader in readers)
                {
                    foreach (var list in reader.AccessLists)
                    {
                        foreach (var level in list)
                        {
                            seenAccess.Add(level.ToString());
                        }
                    }
                }
            }

            if (departmentOnly && (access == null || !MatchesAccess(uid, access)))
                continue;

            result.Add(uid);
        }

        if (result.Count == 0)
        {
            Log.Warning(
                $"Диагностика локдауна: дверей в мире {total}, пропущено (маркер/firelock) {skipped}, " +
                $"на станции {onStation}, из них с AccessReader {withReader}. " +
                $"Ищем доступы: [{(access == null ? "-" : string.Join(", ", access))}]. " +
                $"Доступы на дверях станции: [{string.Join(", ", seenAccess)}].");
        }

        return result;
    }

    /// <summary>
    /// Все считыватели доступа двери. Требования доступа часто хранятся не на самой двери,
    /// а на электронике двери, вставленной в контейнер (ContainerAccessProvider).
    /// </summary>
    private List<AccessReaderComponent> GetAccessReaders(EntityUid door)
    {
        var result = new List<AccessReaderComponent>();

        if (!TryComp<AccessReaderComponent>(door, out var reader))
            return result;

        result.Add(reader);

        var provider = reader.ContainerAccessProvider;
        if (!string.IsNullOrEmpty(provider) && _container.TryGetContainer(door, provider, out var container))
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (TryComp<AccessReaderComponent>(contained, out var inner))
                    result.Add(inner);
            }
        }

        return result;
    }

    private bool MatchesAccess(EntityUid door, HashSet<string> access)
    {
        foreach (var reader in GetAccessReaders(door))
        {
            foreach (var list in reader.AccessLists)
            {
                foreach (var level in list)
                {
                    if (access.Contains(level.ToString()))
                        return true;
                }
            }
        }

        return false;
    }

    private void ApplyLock(EntityUid uid)
    {
        var comp = EnsureComp<LockdownDoorComponent>(uid);
        comp.Locks++;

        // Дверь уже заблокирована другим локдауном.
        if (comp.Locks > 1)
            return;

        _doors.TryClose(uid);

        if (TryComp<DoorBoltComponent>(uid, out var bolt))
        {
            comp.WasBolted = bolt.BoltsDown;
            _doors.SetBoltsDown((uid, bolt), true);
        }

        // Голограмма — только визуал: если прототип не загрузился, блокировка двери всё равно работает.
        if (_proto.HasIndex<EntityPrototype>(HologramPrototype))
            comp.Hologram = Spawn(HologramPrototype, new EntityCoordinates(uid, Vector2.Zero));
        else
            Log.Error($"Прототип голограммы {HologramPrototype} не найден. Проверьте lockdown_console.yml.");
    }

    private void ReleaseLock(EntityUid uid)
    {
        if (!TryComp<LockdownDoorComponent>(uid, out var comp))
            return;

        comp.Locks--;
        if (comp.Locks > 0)
            return;

        if (!comp.WasBolted && TryComp<DoorBoltComponent>(uid, out var bolt))
            _doors.SetBoltsDown((uid, bolt), false);

        if (comp.Hologram is { } hologram)
            QueueDel(hologram);

        RemComp<LockdownDoorComponent>(uid);
    }

    /// <summary>
    /// Двери без болтов (гермозатворы, ставни) тоже нельзя открыть, пока на них висит локдаун.
    /// Исключение — дверь, снятая с локдауна имплантом «свободный допуск».
    /// </summary>
    private void OnBeforeDoorOpened(EntityUid uid, LockdownDoorComponent comp, BeforeDoorOpenedEvent args)
    {
        if (comp.FreePassUntil == null)
            args.Cancel();
    }

    /// <summary>
    /// Пока действует «свободный допуск», дверь удерживается открытой, чтобы можно было пройти.
    /// </summary>
    private void OnBeforeDoorClosed(EntityUid uid, LockdownDoorComponent comp, BeforeDoorClosedEvent args)
    {
        if (comp.FreePassUntil != null)
            args.Cancel();
    }

    private void OnFreePass(EntityUid uid, LockdownFreePassImplantComponent comp, LockdownFreePassEvent args)
    {
        TryFreePass(comp, args);
    }

    /// <summary>
    /// Запасной путь: в некоторых версиях действие импланта приходит на владельца, а не на сам имплант.
    /// </summary>
    private void OnFreePassOnUser(EntityUid uid, ImplantedComponent implanted, LockdownFreePassEvent args)
    {
        if (args.Handled)
            return;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (!TryComp<LockdownFreePassImplantComponent>(implant, out var comp))
                continue;

            TryFreePass(comp, args);
            return;
        }
    }

    private void TryFreePass(LockdownFreePassImplantComponent comp, LockdownFreePassEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var target = args.Target;

        Log.Debug($"Свободный допуск: {ToPrettyString(user)} -> {ToPrettyString(target)}");

        if (!TryComp<LockdownDoorComponent>(target, out var door) || door.Locks <= 0)
        {
            _popup.PopupEntity(Loc.GetString("lockdown-freepass-no-lockdown"), target, user);
            return;
        }

        if (!_transform.InRange(Transform(user).Coordinates, Transform(target).Coordinates, comp.Range))
        {
            _popup.PopupEntity(Loc.GetString("lockdown-freepass-too-far"), target, user);
            return;
        }

        if (_station.GetOwningStation(target) is { } station
            && comp.DisabledAlertLevels.Contains(_alertLevel.GetLevel(station)))
        {
            _popup.PopupEntity(Loc.GetString("lockdown-freepass-alert-level"), target, user);
            return;
        }

        door.FreePassUntil = _timing.CurTime + comp.Duration;
        door.Relocking = false;

        if (TryComp<DoorBoltComponent>(target, out var bolt))
            _doors.SetBoltsDown((target, bolt), false);

        // Без указания пользователя: иначе дверь проверит доступ владельца импланта и может не открыться.
        _doors.TryOpen(target);

        args.Handled = true;
    }

    private void UpdateFreePass(EntityUid uid, LockdownDoorComponent comp, TimeSpan now)
    {
        if (comp.FreePassUntil is { } until && now >= until)
        {
            comp.FreePassUntil = null;
            comp.Relocking = true;
        }

        if (!comp.Relocking)
            return;

        if (!TryComp<DoorComponent>(uid, out var door))
        {
            comp.Relocking = false;
            return;
        }

        // Если в проёме кто-то стоит, закрытие не удастся, поэтому пробуем снова на следующем тике.
        if (door.State != DoorState.Closed)
        {
            _doors.TryClose(uid);
            return;
        }

        if (TryComp<DoorBoltComponent>(uid, out var bolt))
            _doors.SetBoltsDown((uid, bolt), true);

        comp.Relocking = false;
    }

    #endregion

    #region Trash bins

    private List<EntityUid> CollectBins(EntityUid station, HashSet<string>? access, bool departmentOnly)
    {
        var result = new List<EntityUid>();
        var query = EntityQueryEnumerator<LockdownTrashBinComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var bin, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            if (departmentOnly && (access == null || !RoomMatchesAccess(xform, access, bin.RoomSearchLimit)))
                continue;

            result.Add(uid);
        }

        return result;
    }

    /// <summary>
    /// Определяет помещение, в котором стоит бак: заливка по тайлам, пока не упрёмся в стены и двери.
    /// Помещение относится к отделу, если хотя бы одна из его дверей даёт доступ отдела.
    /// Слишком большие зоны (коридоры, холлы) ни к какому отделу не относятся.
    /// </summary>
    private bool RoomMatchesAccess(TransformComponent binXform, HashSet<string> access, int limit)
    {
        if (binXform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var start = _map.TileIndicesFor(gridUid, grid, binXform.Coordinates);
        var visited = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        var boundaryDoors = new HashSet<EntityUid>();
        queue.Enqueue(start);

        Vector2i[] offsets = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();

            foreach (var offset in offsets)
            {
                var next = tile + offset;
                if (visited.Contains(next))
                    continue;

                var blocked = false;
                foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, next))
                {
                    if (HasComp<DoorComponent>(anchored))
                    {
                        boundaryDoors.Add(anchored);
                        blocked = true;
                    }
                    else if (TryComp<AirtightComponent>(anchored, out var airtight) && airtight.AirBlocked)
                    {
                        blocked = true;
                    }
                }

                if (blocked)
                    continue;

                // Пустой тайл (космос) — граница помещения.
                if (!_map.TryGetTileRef(gridUid, grid, next, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                visited.Add(next);
                if (visited.Count > limit)
                    return false;

                queue.Enqueue(next);
            }
        }

        foreach (var door in boundaryDoors)
        {
            if (MatchesAccess(door, access))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Пока на баке висит локдаун, в него нельзя ничего положить.
    /// </summary>
    private void OnBinInsertAttempt(EntityUid uid, LockdownBinLockComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        args.Cancel();
    }

    #endregion

    #region UI messages

    private void OnUiOpened(EntityUid uid, LockdownConsoleComponent comp, BoundUIOpenedEvent args)
    {
        var ent = new Entity<LockdownConsoleComponent>(uid, comp);

        UpdateUi(ent, ent.Comp);
    }

    private void OnStartDepartment(EntityUid uid, LockdownConsoleComponent comp, LockdownStartDepartmentMessage args)
    {
        var ent = new Entity<LockdownConsoleComponent>(uid, comp);

        if (!TryGetContext(ent, args.Actor, out var station, out var state))
            return;

        var now = _timing.CurTime;

        if (state.ConsoleLockedUntil > now)
        {
            Deny(ent, args.Actor, "lockdown-deny-console-locked");
            return;
        }

        if (state.Station != null)
        {
            Deny(ent, args.Actor, "lockdown-deny-station-active");
            return;
        }

        if (!ent.Comp.Departments.Contains(args.Department)
            || !_proto.TryIndex<LockdownDepartmentPrototype>(args.Department, out var dept))
            return;

        if (state.Departments.ContainsKey(dept.ID))
        {
            Deny(ent, args.Actor, "lockdown-deny-already");
            return;
        }

        state.Departments[dept.ID] = new LockdownInstance
        {
            Phase = LockdownPhase.Pending,
            DepartmentId = dept.ID,
            Console = ent,
            ActivateAt = now + ent.Comp.WarningDelay,
            Duration = ent.Comp.LockdownDuration,
            SuccessCooldown = ent.Comp.SuccessCooldown,
        };

        Announce(station, ent, Loc.GetString("lockdown-announce-department",
            ("department", Loc.GetString(dept.Name)),
            ("seconds", (int) ent.Comp.WarningDelay.TotalSeconds)));

        UpdateUi(ent, ent.Comp);
    }

    private void OnCancelDepartment(EntityUid uid, LockdownConsoleComponent comp, LockdownCancelDepartmentMessage args)
    {
        var ent = new Entity<LockdownConsoleComponent>(uid, comp);

        if (!TryGetContext(ent, args.Actor, out var station, out var state))
            return;

        if (!state.Departments.TryGetValue(args.Department, out var instance))
            return;

        var wasPending = instance.Phase == LockdownPhase.Pending;

        Finish(state, instance);
        state.Departments.Remove(args.Department);

        // Отмена до блокировки шлюзов блокирует консоль.
        if (wasPending)
            state.ConsoleLockedUntil = _timing.CurTime + ent.Comp.CancelledCooldown;

        Announce(station, ent, Loc.GetString("lockdown-cancel-department", ("department", DepartmentName(args.Department))));
        UpdateUi(ent, ent.Comp);
    }

    private void OnConfirmStation(EntityUid uid, LockdownConsoleComponent comp, LockdownConfirmStationMessage args)
    {
        var ent = new Entity<LockdownConsoleComponent>(uid, comp);

        if (!TryGetContext(ent, args.Actor, out var station, out var state))
            return;

        var now = _timing.CurTime;

        if (state.ConsoleLockedUntil > now)
        {
            Deny(ent, args.Actor, "lockdown-deny-console-locked");
            return;
        }

        if (state.Station != null)
        {
            Deny(ent, args.Actor, "lockdown-deny-already");
            return;
        }

        if (!TryGetJob(args.Actor, out var job) || !ent.Comp.KeyJobs.Any(j => j.ToString() == job))
        {
            Deny(ent, args.Actor, "lockdown-deny-not-key");
            return;
        }

        state.Confirmations[job] = new KeyConfirmation
        {
            User = args.Actor,
            ExpiresAt = now + ent.Comp.KeyConfirmWindow,
        };

        if (AllKeysConfirmed(ent.Comp, state, now))
        {
            state.Confirmations.Clear();

            state.Station = new LockdownInstance
            {
                Phase = LockdownPhase.Pending,
                Console = ent,
                ActivateAt = now + ent.Comp.WarningDelay,
                Duration = ent.Comp.LockdownDuration,
                SuccessCooldown = ent.Comp.SuccessCooldown,
            };

            Announce(station, ent, Loc.GetString("lockdown-announce-station",
                ("seconds", (int) ent.Comp.WarningDelay.TotalSeconds)));
        }

        UpdateUi(ent, ent.Comp);
    }

    private void OnCancelStation(EntityUid uid, LockdownConsoleComponent comp, LockdownCancelStationMessage args)
    {
        var ent = new Entity<LockdownConsoleComponent>(uid, comp);

        if (!TryGetContext(ent, args.Actor, out var station, out var state))
            return;

        if (state.Station is not { } instance)
            return;

        if (!TryGetJob(args.Actor, out var job) || job != ent.Comp.StationCancelJob.ToString())
        {
            Deny(ent, args.Actor, "lockdown-deny-need-captain");
            return;
        }

        var wasPending = instance.Phase == LockdownPhase.Pending;

        Finish(state, instance);
        state.Station = null;

        if (wasPending)
            state.ConsoleLockedUntil = _timing.CurTime + ent.Comp.CancelledCooldown;

        Announce(station, ent, Loc.GetString("lockdown-cancel-station"));
        UpdateUi(ent, ent.Comp);
    }

    private bool AllKeysConfirmed(LockdownConsoleComponent comp, LockdownStationComponent state, TimeSpan now)
    {
        var users = new HashSet<EntityUid>();

        foreach (var key in comp.KeyJobs)
        {
            if (!state.Confirmations.TryGetValue(key.ToString(), out var confirmation) || confirmation.ExpiresAt <= now)
                return false;

            // Каждый ключ должен подтвердить отдельный человек.
            if (!users.Add(confirmation.User))
                return false;
        }

        return true;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Общая проверка: консоль на станции, у пользователя есть имплант допуска.
    /// </summary>
    private bool TryGetContext(
        Entity<LockdownConsoleComponent> console,
        EntityUid user,
        out EntityUid station,
        out LockdownStationComponent state)
    {
        station = default;
        state = default!;

        if (_station.GetOwningStation(console) is not { } owning)
        {
            Deny(console, user, "lockdown-deny-no-station");
            return false;
        }

        if (!HasAccessImplant(user))
        {
            Deny(console, user, "lockdown-deny-no-implant");
            return false;
        }

        station = owning;
        state = EnsureComp<LockdownStationComponent>(owning);
        return true;
    }

    private bool HasAccessImplant(EntityUid user)
    {
        if (!TryComp<ImplantedComponent>(user, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<LockdownAccessImplantComponent>(implant))
                return true;
        }

        return false;
    }

    private bool TryGetJob(EntityUid user, out string job)
    {
        job = string.Empty;

        if (!_idCard.TryFindIdCard(user, out var card))
            return false;

        job = card.Comp.JobPrototype?.ToString() ?? string.Empty;
        return job != string.Empty;
    }

    private string DepartmentName(string id)
    {
        return _proto.TryIndex<LockdownDepartmentPrototype>(id, out var dept)
            ? Loc.GetString(dept.Name)
            : id;
    }

    private void Deny(EntityUid console, EntityUid user, string locKey)
    {
        _popup.PopupEntity(Loc.GetString(locKey), console, user);
    }

    /// <summary>
    /// Объявление в общую рацию от имени консоли. Если исходная консоль удалена, берётся любая другая консоль станции.
    /// </summary>
    private void Announce(EntityUid station, EntityUid preferred, string message)
    {
        var source = preferred;

        if (!Exists(source))
        {
            source = EntityUid.Invalid;
            var query = EntityQueryEnumerator<LockdownConsoleComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (_station.GetOwningStation(uid) != station)
                    continue;

                source = uid;
                break;
            }
        }

        if (source == EntityUid.Invalid)
            return;

        // ";" — префикс общей рации. Консоль говорит через IntrinsicRadioTransmitter, TTS озвучивает сообщение.
        _chat.TrySendInGameICMessage(
            source,
            ";" + message,
            InGameICChatType.Speak,
            ChatTransmitRange.HideChat,
            hideLog: false,
            checkRadioPrefix: true);
    }

    private void UpdateUi(EntityUid console, LockdownConsoleComponent comp)
    {
        if (!_ui.IsUiOpen(console, LockdownConsoleUiKey.Key))
            return;

        var now = _timing.CurTime;
        LockdownStationComponent? state = null;

        var owningStation = _station.GetOwningStation(console);
        var hasStation = owningStation != null;
        if (owningStation != null)
            TryComp(owningStation.Value, out state);

        var departments = new List<LockdownEntry>();
        foreach (var id in comp.Departments)
        {
            if (!_proto.TryIndex(id, out var dept))
                continue;

            var phase = LockdownPhase.None;
            var end = TimeSpan.Zero;

            if (state != null && state.Departments.TryGetValue(dept.ID, out var instance))
            {
                phase = instance.Phase;
                end = phase == LockdownPhase.Pending ? instance.ActivateAt : instance.EndAt;
            }

            departments.Add(new LockdownEntry(dept.ID, Loc.GetString(dept.Name), phase, end));
        }

        var stationEntry = new LockdownEntry(StationId, string.Empty, LockdownPhase.None, TimeSpan.Zero);
        if (state?.Station is { } stationInstance)
        {
            stationEntry = new LockdownEntry(
                StationId,
                string.Empty,
                stationInstance.Phase,
                stationInstance.Phase == LockdownPhase.Pending ? stationInstance.ActivateAt : stationInstance.EndAt);
        }

        var keys = new List<LockdownKeyEntry>();
        foreach (var job in comp.KeyJobs)
        {
            var confirmed = false;
            var expires = TimeSpan.Zero;

            if (state != null
                && state.Confirmations.TryGetValue(job.ToString(), out var confirmation)
                && confirmation.ExpiresAt > now)
            {
                confirmed = true;
                expires = confirmation.ExpiresAt;
            }

            var jobName = _proto.TryIndex(job, out var jobProto) ? jobProto.LocalizedName : job.ToString();
            keys.Add(new LockdownKeyEntry(job.ToString(), jobName, confirmed, expires));
        }

        var sections = new List<SecuritySectionEntry>();
        foreach (var section in comp.Sections)
        {
            sections.Add(new SecuritySectionEntry(section.Id, Loc.GetString(section.Name), section.Icon));
        }

        // Список смотрящих с имплантом допуска: пункт LOCKDOWN у остальных будет неактивен.
        var accessActors = new List<NetEntity>();
        foreach (var actor in _ui.GetActors(console, LockdownConsoleUiKey.Key))
        {
            if (HasAccessImplant(actor))
                accessActors.Add(GetNetEntity(actor));
        }

        var uiState = new LockdownConsoleBoundUserInterfaceState(
            accessActors,
            sections,
            hasStation,
            now,
            state?.ConsoleLockedUntil ?? TimeSpan.Zero,
            departments,
            stationEntry,
            keys);

        _ui.SetUiState(console, LockdownConsoleUiKey.Key, uiState);
    }

    /// <summary>
    /// Кнопка бокового меню: открывает на этой же консоли интерфейс выбранного раздела.
    /// </summary>
    private void OnOpenSection(EntityUid uid, LockdownConsoleComponent comp, SecurityConsoleOpenSectionMessage args)
    {
        foreach (var section in comp.Sections)
        {
            if (section.Id != args.Section || section.UiKey == null)
                continue;

            _ui.OpenUi(uid, section.UiKey, args.Actor);
            return;
        }
    }

    #endregion
}