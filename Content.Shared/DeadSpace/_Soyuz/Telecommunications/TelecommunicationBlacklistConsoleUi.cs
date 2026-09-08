// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Telecommunications;

[Serializable, NetSerializable]
public enum TelecommunicationBlacklistConsoleUiKey : byte
{
    Key,
}

/// <summary>
/// A station roster entry shown by the telecommunications blacklist console.
/// The mind entity is the authoritative identity; the name and job are display-only station-record data.
/// </summary>
[Serializable, NetSerializable]
public sealed record TelecommunicationBlacklistRosterEntry(NetEntity Mind, string Name, string JobTitle);

[Serializable, NetSerializable]
public sealed class TelecommunicationBlacklistConsoleState(
    bool locked,
    List<TelecommunicationBlacklistRosterEntry> roster,
    List<ProtoId<RadioChannelPrototype>> channels,
    NetEntity? selectedMind,
    HashSet<ProtoId<RadioChannelPrototype>> blockedChannels) : BoundUserInterfaceState
{
    public readonly bool Locked = locked;
    public readonly List<TelecommunicationBlacklistRosterEntry> Roster = roster;
    public readonly List<ProtoId<RadioChannelPrototype>> Channels = channels;
    public readonly NetEntity? SelectedMind = selectedMind;
    public readonly HashSet<ProtoId<RadioChannelPrototype>> BlockedChannels = blockedChannels;
}

[Serializable, NetSerializable]
public sealed class SelectTelecommunicationBlacklistTargetMessage(NetEntity targetMind) : BoundUserInterfaceMessage
{
    public readonly NetEntity TargetMind = targetMind;
}

[Serializable, NetSerializable]
public sealed class ApplyTelecommunicationBlacklistMessage(
    NetEntity targetMind,
    List<ProtoId<RadioChannelPrototype>> blockedChannels) : BoundUserInterfaceMessage
{
    public readonly NetEntity TargetMind = targetMind;
    public readonly List<ProtoId<RadioChannelPrototype>> BlockedChannels = blockedChannels;
}

[Serializable, NetSerializable]
public sealed class RefreshTelecommunicationBlacklistConsoleMessage : BoundUserInterfaceMessage;
