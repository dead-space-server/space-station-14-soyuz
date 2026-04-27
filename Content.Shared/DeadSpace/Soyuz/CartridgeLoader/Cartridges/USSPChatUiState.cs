using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class USSPChatUiState : BoundUserInterfaceState
{
    public string ChatId { get; }
    public string? ActiveChat { get; }
    public bool Muted { get; }
    public bool VisibleInDiscovery { get; }
    public bool CanSendMessages { get; }
    public Dictionary<string, ChatContact> Contacts { get; }
    public Dictionary<string, ChatGroup> Groups { get; }
    public List<ChatMessage>? ActiveChatMessages { get; }
    public List<DiscoveryEntry>? DiscoveryList { get; }

    public USSPChatUiState(
        string chatId, string? activeChat, bool muted, bool visibleInDiscovery, bool canSendMessages,
        Dictionary<string, ChatContact> contacts,
        Dictionary<string, ChatGroup> groups,
        List<ChatMessage>? activeChatMessages,
        List<DiscoveryEntry>? discoveryList = null
    )
    {
        ChatId = chatId;
        ActiveChat = activeChat;
        Muted = muted;
        VisibleInDiscovery = visibleInDiscovery;
        CanSendMessages = canSendMessages;
        Contacts = contacts;
        Groups = groups;
        ActiveChatMessages = activeChatMessages;
        DiscoveryList = discoveryList;
    }
}

[Serializable, NetSerializable]
public sealed class DiscoveryEntry
{
    public string ChatId { get; }
    public string DisplayName { get; }
    public DiscoveryEntry(string chatId, string displayName)
    {
        ChatId = chatId;
        DisplayName = displayName;
    }
}

[Serializable, NetSerializable]
public sealed class ChatContact
{
    public string ContactId { get; }
    public string ContactName { get; }
    public bool HasUnread { get; }

    public ChatContact(string contactId, string contactName, bool hasUnread)
    {
        ContactId = contactId;
        ContactName = contactName;
        HasUnread = hasUnread;
    }
}

[Serializable, NetSerializable]
public sealed class ChatGroup
{
    public string GroupId { get; }
    public string GroupName { get; }
    public bool HasUnread { get; }
    public int MemberCount { get; }

    public ChatGroup(string groupId, string groupName, bool hasUnread, int memberCount)
    {
        GroupId = groupId;
        GroupName = groupName;
        HasUnread = hasUnread;
        MemberCount = memberCount;
    }
}

[Serializable, NetSerializable]
public sealed class ChatMessage
{
    public string SenderId { get; }
    public string SenderName { get; }
    public string Message { get; }
    public TimeSpan Timestamp { get; }
    public bool IsOwnMessage { get; }
    public bool Delivered { get; }

    public ChatMessage(string senderId, string senderName, string message, TimeSpan timestamp, bool isOwnMessage, bool delivered)
    {
        SenderId = senderId;
        SenderName = senderName;
        Message = message;
        Timestamp = timestamp;
        IsOwnMessage = isOwnMessage;
        Delivered = delivered;
    }
}
