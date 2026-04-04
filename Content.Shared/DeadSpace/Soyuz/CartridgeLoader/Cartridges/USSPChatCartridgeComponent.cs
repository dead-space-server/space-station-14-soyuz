using Robust.Shared.Audio;

namespace Content.Shared.DeadSpace.Soyuz.CartridgeLoader.Cartridges;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class USSPChatCartridgeComponent : Component
{
    private static readonly SoundSpecifier DefaultMessageSound = new SoundPathSpecifier("/Audio/Effects/pop_high.ogg");

    [DataField]
    public string ChatId = string.Empty;

    [DataField]
    public string? ActiveChat;

    [DataField]
    public string OwnerName = string.Empty;

    [DataField]
    public bool MutedSound;

    [DataField]
    public SoundSpecifier Sound = DefaultMessageSound;

    [DataField]
    public Dictionary<string, ChatContact> Contacts = new();

    [DataField]
    public Dictionary<string, List<ChatMessage>> Messages = new();

    [DataField]
    public Dictionary<string, ChatGroup> Groups = new();

    [DataField, AutoPausedField]
    public TimeSpan NextMessageAllowedAfter = TimeSpan.Zero;

    [DataField]
    public TimeSpan MessageDelay = TimeSpan.FromSeconds(2.5);

    [DataField]
    public bool VisibleInDiscovery;

    [DataField]
    public bool CanSendMessages = true;

    [DataField]
    public bool CanMonitorAllChats;
}
