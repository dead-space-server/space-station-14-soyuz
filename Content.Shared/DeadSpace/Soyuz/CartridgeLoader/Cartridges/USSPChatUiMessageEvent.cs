using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.CartridgeLoader.Cartridges;

public interface IUSSPChatUiMessagePayload { }

[Serializable, NetSerializable]
public sealed class USSPChatAddContact : IUSSPChatUiMessagePayload
{
    public string ContactId { get; }
    public string ContactName { get; }
    public USSPChatAddContact(string contactId, string contactName)
    {
        ContactId = contactId;
        ContactName = contactName;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatEraseContact : IUSSPChatUiMessagePayload
{
    public string ContactId { get; }
    public USSPChatEraseContact(string contactId)
    {
        ContactId = contactId;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatMuted : IUSSPChatUiMessagePayload
{
}

[Serializable, NetSerializable]
public sealed class USSPChatSendMessage : IUSSPChatUiMessagePayload
{
    public string RecipientId { get; }
    public string Message { get; }
    public USSPChatSendMessage(string recipientId, string message)
    {
        RecipientId = recipientId;
        Message = message;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatSetActiveChat : IUSSPChatUiMessagePayload
{
    public string ContactId { get; }
    public USSPChatSetActiveChat(string contactId)
    {
        ContactId = contactId;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatCreateGroup : IUSSPChatUiMessagePayload
{
    public string GroupName { get; }
    public USSPChatCreateGroup(string groupName)
    {
        GroupName = groupName;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatJoinGroup : IUSSPChatUiMessagePayload
{
    public string GroupId { get; }
    public USSPChatJoinGroup(string groupId)
    {
        GroupId = groupId;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatLeaveGroup : IUSSPChatUiMessagePayload
{
    public string GroupId { get; }
    public USSPChatLeaveGroup(string groupId)
    {
        GroupId = groupId;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatSetVisibleInDiscovery : IUSSPChatUiMessagePayload
{
    public bool Visible { get; }
    public USSPChatSetVisibleInDiscovery(bool visible)
    {
        Visible = visible;
    }
}

[Serializable, NetSerializable]
public sealed class USSPChatRequestDiscoveryList : IUSSPChatUiMessagePayload
{
}

[Serializable, NetSerializable]
public sealed class USSPChatUiMessageEvent : CartridgeMessageEvent
{
    public IUSSPChatUiMessagePayload Payload { get; }

    public USSPChatUiMessageEvent(IUSSPChatUiMessagePayload payload)
    {
        Payload = payload;
    }
}
