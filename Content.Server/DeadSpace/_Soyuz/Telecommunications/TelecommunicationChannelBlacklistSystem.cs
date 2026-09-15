// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Collections.Frozen;
using Content.Server.Radio;
using Content.Server.Station.Systems;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Radio;
using Content.Shared.Station.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Telecommunications;

/// <summary>
/// Owns station-local radio channel blacklists and applies them to outgoing radio transmissions.
/// </summary>
public sealed class TelecommunicationChannelBlacklistSystem : EntitySystem
{
    private static readonly FrozenSet<ProtoId<RadioChannelPrototype>> EmptyChannels =
        Array.Empty<ProtoId<RadioChannelPrototype>>().ToFrozenSet();

    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioSendAttemptEvent>(OnRadioSendAttempt);
    }

    private void OnRadioSendAttempt(ref RadioSendAttemptEvent args)
    {
        if (!TryComp(args.RadioSource, out TransformComponent? radioTransform))
            return;

        var station = _station.GetOwningStation(args.RadioSource, radioTransform);
        if (station == null ||
            !_mind.TryGetMind(args.MessageSource, out var mindId, out _) ||
            !IsChannelBlocked(station.Value, mindId, args.Channel.ID))
        {
            return;
        }

        args.Cancelled = true;
    }

    /// <summary>
    /// Gets the immutable set of channels blocked for a mind on a station.
    /// </summary>
    public IReadOnlySet<ProtoId<RadioChannelPrototype>> GetBlockedChannels(EntityUid station, EntityUid mind)
    {
        if (TryComp<TelecommunicationChannelBlacklistComponent>(station, out var blacklist) &&
            blacklist.BlockedChannels.TryGetValue(mind, out var channels))
        {
            return channels;
        }

        return EmptyChannels;
    }

    /// <summary>
    /// Returns whether a radio channel is blocked for a mind on a station.
    /// </summary>
    public bool IsChannelBlocked(
        EntityUid station,
        EntityUid mind,
        ProtoId<RadioChannelPrototype> channel)
    {
        return TryComp<TelecommunicationChannelBlacklistComponent>(station, out var blacklist) &&
               blacklist.BlockedChannels.TryGetValue(mind, out var channels) &&
               channels.Contains(channel);
    }

    /// <summary>
    /// Atomically replaces all blocked channels for a mind on a station.
    /// Returns false without changing state if any input is invalid.
    /// </summary>
    public bool SetBlockedChannels(
        EntityUid station,
        EntityUid mind,
        IEnumerable<ProtoId<RadioChannelPrototype>>? channels)
    {
        if (channels == null ||
            !HasComp<StationDataComponent>(station) ||
            !HasComp<MindComponent>(mind))
        {
            return false;
        }

        var replacement = new HashSet<ProtoId<RadioChannelPrototype>>();
        foreach (var channel in channels)
        {
            if (!_prototype.HasIndex<RadioChannelPrototype>(channel))
                return false;

            replacement.Add(channel);
        }

        if (replacement.Count == 0)
        {
            if (TryComp<TelecommunicationChannelBlacklistComponent>(station, out var existing))
                existing.BlockedChannels.Remove(mind);

            return true;
        }

        var frozenReplacement = replacement.ToFrozenSet();
        var blacklist = EnsureComp<TelecommunicationChannelBlacklistComponent>(station);

        if (blacklist.BlockedChannels.TryGetValue(mind, out var previous) &&
            previous.SetEquals(frozenReplacement))
        {
            return true;
        }

        blacklist.BlockedChannels[mind] = frozenReplacement;
        return true;
    }

    /// <summary>
    /// Removes all blocked channels for a mind on a station.
    /// </summary>
    public bool ClearBlockedChannels(EntityUid station, EntityUid mind)
    {
        return SetBlockedChannels(station, mind, EmptyChannels);
    }
}
