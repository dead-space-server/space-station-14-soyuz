// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Collections.Frozen;
using Content.Server.Access.Systems;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DeadSpace._Soyuz.Telecommunications;
using Content.Shared.GameTicking;
using Content.Shared.Lock;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.StationRecords;
using System.Linq;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Telecommunications;

/// <summary>
/// Provides station-record-backed access to the station-local radio channel blacklist.
/// </summary>
public sealed class TelecommunicationBlacklistConsoleSystem : EntitySystem
{
    private static readonly FrozenSet<ProtoId<RadioChannelPrototype>> ExcludedChannels =
        new ProtoId<RadioChannelPrototype>[]
        {
            "CentCom",
            "DeathSquad",
            "Xenoborg",
            "Mothership",
            "Merc",
            "Freelance",
            "SOCChannel",
            "CarpDragonChannel",
            "Unitolog",
            "Handheld",
            "TaipanHandheld",
            "Syndicate",
            "SpiderTerrorChannel",
            "Taipan",
            "Shadowling",
            "Hivemind",
            "CriticalForce",
        }
        .ToFrozenSet();

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TelecommunicationChannelBlacklistSystem _blacklist = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<TelecommunicationBlacklistConsoleComponent>(TelecommunicationBlacklistConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<SelectTelecommunicationBlacklistTargetMessage>(OnTargetSelected);
            subs.Event<ApplyTelecommunicationBlacklistMessage>(OnApply);
            subs.Event<RefreshTelecommunicationBlacklistConsoleMessage>(OnRefresh);
        });

        SubscribeLocalEvent<TelecommunicationBlacklistConsoleComponent, LockToggledEvent>(OnLockToggled);
        SubscribeLocalEvent<TelecommunicationBlacklistConsoleComponent, AfterGeneralRecordCreatedEvent>(OnRecordChanged);
        SubscribeLocalEvent<TelecommunicationBlacklistConsoleComponent, RecordModifiedEvent>(OnRecordChanged);
        SubscribeLocalEvent<TelecommunicationBlacklistConsoleComponent, RecordRemovedEvent>(OnRecordChanged);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete, after: [typeof(StationRecordsSystem)]);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryComp<MindContainerComponent>(args.Mob, out var mindContainer) ||
            mindContainer.Mind is not { } mind ||
            !_idCard.TryFindIdCard(args.Mob, out var idCard) ||
            !TryComp<StationRecordKeyStorageComponent>(idCard.Owner, out var keyStorage) ||
            keyStorage.Key is not { } key ||
            key.OriginStation != args.Station ||
            !_records.TryGetRecord<GeneralStationRecord>(key, out _))
        {
            return;
        }

        TryAssociateMindWithStationRecord(mind, key);
    }

    /// <summary>
    /// Associates a mind with an existing station record without depending on continued possession of its ID card.
    /// </summary>
    public bool TryAssociateMindWithStationRecord(EntityUid mind, StationRecordKey key)
    {
        if (!HasComp<MindComponent>(mind) ||
            !key.IsValid() ||
            !_records.TryGetRecord<GeneralStationRecord>(key, out _))
        {
            return false;
        }

        EnsureComp<TelecommunicationBlacklistStationRecordComponent>(mind).RecordKey = key;
        return true;
    }

    private void OnUiOpened(Entity<TelecommunicationBlacklistConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnRefresh(
        Entity<TelecommunicationBlacklistConsoleComponent> ent,
        ref RefreshTelecommunicationBlacklistConsoleMessage args)
    {
        if (!CanHandleUiMessage(ent, args.Actor))
            return;

        UpdateUserInterface(ent);
    }

    private void OnTargetSelected(
        Entity<TelecommunicationBlacklistConsoleComponent> ent,
        ref SelectTelecommunicationBlacklistTargetMessage args)
    {
        if (!CanHandleUiMessage(ent, args.Actor) || IsLocked(ent))
            return;

        ent.Comp.ActiveMind = null;

        if (_station.GetOwningStation(ent) is { } station &&
            TryGetEntity(args.TargetMind, out var mind) &&
            TryGetRosterEntry(station, mind.Value, out _, out _))
        {
            ent.Comp.ActiveMind = mind.Value;
        }

        UpdateUserInterface(ent);
    }

    private void OnApply(
        Entity<TelecommunicationBlacklistConsoleComponent> ent,
        ref ApplyTelecommunicationBlacklistMessage args)
    {
        // BUI dispatch performs BoundUserInterfaceMessageAttempt validation before this handler,
        // including the normal interaction/range and StationAiWhitelist checks. Keep an explicit
        // subscription check here so a stale or forged request cannot use this mutation path.
        if (!CanHandleUiMessage(ent, args.Actor))
            return;

        if (IsLocked(ent))
        {
            UpdateUserInterface(ent);
            return;
        }

        if (!_access.IsAllowed(args.Actor, ent))
        {
            UpdateUserInterface(ent);
            _popup.PopupEntity(Loc.GetString("telecommunication-blacklist-console-access-denied"), ent, args.Actor);
            return;
        }

        if (_station.GetOwningStation(ent) is not { } station ||
            !TryGetEntity(args.TargetMind, out var mind) ||
            ent.Comp.ActiveMind != mind.Value ||
            !TryGetRosterEntry(station, mind.Value, out var rosterEntry, out var targetCharacter))
        {
            ent.Comp.ActiveMind = null;
            UpdateUserInterface(ent);
            _popup.PopupEntity(Loc.GetString("telecommunication-blacklist-console-invalid-target"), ent, args.Actor);
            return;
        }

        var replacement = new HashSet<ProtoId<RadioChannelPrototype>>();
        foreach (var channel in args.BlockedChannels)
        {
            if (!_prototype.HasIndex<RadioChannelPrototype>(channel))
            {
                UpdateUserInterface(ent);
                _popup.PopupEntity(Loc.GetString("telecommunication-blacklist-console-invalid-channel"), ent, args.Actor);
                return;
            }

            replacement.Add(channel);
        }

        if (!_blacklist.SetBlockedChannels(station, mind.Value, replacement))
        {
            UpdateUserInterface(ent);
            return;
        }

        // The authoritative state is refreshed only after the atomic replacement has committed.
        UpdateUserInterface(ent);

        _popup.PopupEntity(
            Loc.GetString("telecommunication-blacklist-console-applied", ("target", rosterEntry.Name)),
            ent,
            args.Actor);

        var finalChannels = replacement.Count == 0
            ? "<empty>"
            : string.Join(", ", replacement.OrderBy(channel => channel.Id));
        _adminLogger.Add(
            LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(args.Actor):actor} set the station telecommunications radio blacklist for " +
            $"{ToPrettyString(targetCharacter):target} to [{finalChannels}] using {ToPrettyString(ent):console}");
    }

    private void OnLockToggled(Entity<TelecommunicationBlacklistConsoleComponent> ent, ref LockToggledEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnRecordChanged(
        Entity<TelecommunicationBlacklistConsoleComponent> ent,
        ref AfterGeneralRecordCreatedEvent args)
    {
        UpdateIfOwnedByStation(ent, args.Station);
    }

    private void OnRecordChanged(
        Entity<TelecommunicationBlacklistConsoleComponent> ent,
        ref RecordModifiedEvent args)
    {
        UpdateIfOwnedByStation(ent, args.Station);
    }

    private void OnRecordChanged(
        Entity<TelecommunicationBlacklistConsoleComponent> ent,
        ref RecordRemovedEvent args)
    {
        UpdateIfOwnedByStation(ent, args.Station);
    }

    private void UpdateIfOwnedByStation(Entity<TelecommunicationBlacklistConsoleComponent> ent, EntityUid station)
    {
        if (_station.GetOwningStation(ent) == station)
            UpdateUserInterface(ent);
    }

    private bool CanHandleUiMessage(Entity<TelecommunicationBlacklistConsoleComponent> ent, EntityUid actor)
    {
        return actor.Valid &&
               Exists(actor) &&
               _ui.IsUiOpen(ent.Owner, TelecommunicationBlacklistConsoleUiKey.Key, actor);
    }

    private bool IsLocked(Entity<TelecommunicationBlacklistConsoleComponent> ent)
    {
        return !TryComp<LockComponent>(ent, out var lockComponent) || lockComponent.Locked;
    }

    private void UpdateUserInterface(Entity<TelecommunicationBlacklistConsoleComponent> ent)
    {
        var locked = IsLocked(ent);
        var channels = _prototype
            .EnumeratePrototypes<RadioChannelPrototype>()
            .Where(channel => !ExcludedChannels.Contains(channel.ID))
            .Select(channel => (ProtoId<RadioChannelPrototype>) channel.ID)
            .OrderBy(channel => channel.Id)
            .ToList();

        var roster = new List<TelecommunicationBlacklistRosterEntry>();
        NetEntity? selectedMind = null;
        var blockedChannels = new HashSet<ProtoId<RadioChannelPrototype>>();

        if (!locked && _station.GetOwningStation(ent) is { } station)
        {
            var query = EntityQueryEnumerator<MindComponent>();
            while (query.MoveNext(out var mind, out _))
            {
                if (TryGetRosterEntry(station, mind, out var entry, out _))
                    roster.Add(entry);
            }

            roster = roster
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.JobTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Mind)
                .ToList();

            if (ent.Comp.ActiveMind is { } activeMind &&
                TryGetRosterEntry(station, activeMind, out _, out _))
            {
                selectedMind = GetNetEntity(activeMind);
                blockedChannels.UnionWith(_blacklist.GetBlockedChannels(station, activeMind));
            }
            else
            {
                ent.Comp.ActiveMind = null;
            }
        }

        _ui.SetUiState(
            ent.Owner,
            TelecommunicationBlacklistConsoleUiKey.Key,
            new TelecommunicationBlacklistConsoleState(locked, roster, channels, selectedMind, blockedChannels));
    }

    /// <summary>
    /// Resolves a mind to its current character and its spawn-time station record.
    /// Station records supply display data and station membership, while the mind remains the blacklist identity.
    /// </summary>
    private bool TryGetRosterEntry(
        EntityUid station,
        EntityUid mindUid,
        out TelecommunicationBlacklistRosterEntry entry,
        out EntityUid character)
    {
        entry = default!;
        character = default;

        if (!TryComp<MindComponent>(mindUid, out var mind) ||
            mind.OwnedEntity is not { } owned ||
            TerminatingOrDeleted(owned) ||
            !TryComp<MindContainerComponent>(owned, out var mindContainer) ||
            mindContainer.Mind != mindUid ||
            !TryComp<TelecommunicationBlacklistStationRecordComponent>(mindUid, out var recordLink) ||
            !recordLink.RecordKey.IsValid() ||
            recordLink.RecordKey.OriginStation != station ||
            !_records.TryGetRecord<GeneralStationRecord>(recordLink.RecordKey, out var record))
        {
            return false;
        }

        character = owned;
        entry = new TelecommunicationBlacklistRosterEntry(GetNetEntity(mindUid), record.Name, record.JobTitle);
        return true;
    }
}
