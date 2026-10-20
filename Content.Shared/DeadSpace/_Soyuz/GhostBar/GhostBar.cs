// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.GhostBar;

[NetSerializable, Serializable]
public sealed class JoinGhostBarEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class GhostBarEuiState : EuiStateBase
{
    [DataField] public string Title { get; set; } = string.Empty;
    [DataField] public string Description { get; set; } = string.Empty;
    [DataField] public string Button { get; set; } = string.Empty;
    [DataField] public List<GhostBarCostumeOption> Costumes { get; set; } = new();
    [DataField] public HashSet<string> Selected { get; set; } = new();

    public GhostBarEuiState() { }
}

[Serializable, NetSerializable]
public sealed class GhostBarCostumeOption
{
    [DataField] public string Id { get; set; } = string.Empty;
    [DataField] public string Name { get; set; } = string.Empty;
    [DataField] public string Category { get; set; } = string.Empty;
    [DataField] public string ClothingProto { get; set; } = string.Empty;
    [DataField] public string Slot { get; set; } = string.Empty;
    [DataField] public bool Default { get; set; }
}

[Serializable, NetSerializable]
public sealed class GhostBarCostumeToggleMessage : EuiMessageBase
{
    [DataField] public string CostumeId { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public sealed class GhostBarButtonPressedMessage : EuiMessageBase;