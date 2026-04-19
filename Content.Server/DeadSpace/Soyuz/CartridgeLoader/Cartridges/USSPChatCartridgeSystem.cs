using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.DeadSpace.Soyuz.CartridgeLoader.Cartridges;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Soyuz.CartridgeLoader.Cartridges;

public sealed class USSPChatCartridgeSystem : SharedUSSPChatCartridgeSystem
{
    private const string GroupIdAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int MaxContactIdLength = 5;
    private const int MaxContactNameLength = 9;
    private const int GroupIdLength = 6;
    private const int MaxGroupNameLength = 16;
    private const int MaxMessageLength = 200;
    private const int MessageRange = 2000;

    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>Maps ChatId (e.g. "#1234") to cartridge entity for delivery.</summary>
    private readonly Dictionary<string, EntityUid> _activeChats = new();
    private readonly Dictionary<string, GroupSession> _groups = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<USSPChatCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<USSPChatCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<USSPChatCartridgeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<USSPChatCartridgeComponent, CartridgeRemovedEvent>(OnCartridgeRemoved);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _activeChats.Clear();
        _groups.Clear();
    }

    private void OnMapInit(EntityUid uid, USSPChatCartridgeComponent component, MapInitEvent args)
    {
        component.ChatId = GenerateUniqueChatId();
        _activeChats[component.ChatId] = uid;
    }

    private string GenerateUniqueChatId()
    {
        string id;
        do
        {
            id = "#" + _random.Next(10000).ToString("D4");
        } while (_activeChats.ContainsKey(id));
        return id;
    }

    private void OnCartridgeRemoved(EntityUid uid, USSPChatCartridgeComponent component, CartridgeRemovedEvent args)
    {
        _activeChats.Remove(component.ChatId);
        RemoveCartridgeFromGroups(uid, component);
    }

    private void OnUiReady(EntityUid uid, USSPChatCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        EnsureOwnerName(uid, component);
        UpdateUiState(uid, args.Loader, component, discoveryList: null);
    }

    private void OnUiMessage(EntityUid uid, USSPChatCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not USSPChatUiMessageEvent message)
            return;

        var loaderUid = GetEntity(args.LoaderUid);
        EnsureOwnerName(uid, component);
        var discoveryList = HandleUiPayload(uid, component, message.Payload);
        UpdateUiState(uid, loaderUid, component, discoveryList);
    }

    private List<DiscoveryEntry>? HandleUiPayload(
        EntityUid cartridgeUid,
        USSPChatCartridgeComponent component,
        IUSSPChatUiMessagePayload payload)
    {
        if (!component.CanSendMessages &&
            payload is not USSPChatMuted &&
            payload is not USSPChatSetActiveChat)
        {
            return null;
        }

        return payload switch
        {
            USSPChatAddContact add => HandleAddContact(component, add),
            USSPChatEraseContact erase => HandleEraseContact(component, erase),
            USSPChatMuted => HandleMuteToggle(component),
            USSPChatSendMessage send => HandleSendMessage(cartridgeUid, component, send),
            USSPChatSetActiveChat set => HandleSetActiveChat(component, set),
            USSPChatCreateGroup create => HandleCreateGroup(cartridgeUid, component, create),
            USSPChatJoinGroup join => HandleJoinGroup(cartridgeUid, component, join),
            USSPChatLeaveGroup leave => HandleLeaveGroup(cartridgeUid, component, leave),
            USSPChatSetVisibleInDiscovery visibility => HandleDiscoveryVisibility(component, visibility),
            USSPChatRequestDiscoveryList => BuildDiscoveryList(component.ChatId),
            _ => null
        };
    }

    private List<DiscoveryEntry>? HandleAddContact(USSPChatCartridgeComponent component, USSPChatAddContact payload)
    {
        AddContact(component, payload.ContactId, payload.ContactName);
        return null;
    }

    private List<DiscoveryEntry>? HandleEraseContact(USSPChatCartridgeComponent component, USSPChatEraseContact payload)
    {
        RemoveContact(component, payload.ContactId);
        return null;
    }

    private static List<DiscoveryEntry>? HandleMuteToggle(USSPChatCartridgeComponent component)
    {
        component.MutedSound = !component.MutedSound;
        return null;
    }

    private List<DiscoveryEntry>? HandleSendMessage(
        EntityUid cartridgeUid,
        USSPChatCartridgeComponent component,
        USSPChatSendMessage payload)
    {
        var now = _timing.CurTime;
        if (now < component.NextMessageAllowedAfter)
            return null;

        if (!SendMessage(cartridgeUid, component, payload.RecipientId, payload.Message, now))
            return null;

        component.NextMessageAllowedAfter = now + component.MessageDelay;
        return null;
    }

    private static List<DiscoveryEntry>? HandleSetActiveChat(
        USSPChatCartridgeComponent component,
        USSPChatSetActiveChat payload)
    {
        component.ActiveChat = payload.ContactId;
        ClearUnread(component, payload.ContactId);
        return null;
    }

    private List<DiscoveryEntry>? HandleCreateGroup(
        EntityUid cartridgeUid,
        USSPChatCartridgeComponent component,
        USSPChatCreateGroup payload)
    {
        CreateGroup(cartridgeUid, component, payload.GroupName);
        return null;
    }

    private List<DiscoveryEntry>? HandleJoinGroup(
        EntityUid cartridgeUid,
        USSPChatCartridgeComponent component,
        USSPChatJoinGroup payload)
    {
        JoinGroup(cartridgeUid, component, payload.GroupId);
        return null;
    }

    private List<DiscoveryEntry>? HandleLeaveGroup(
        EntityUid cartridgeUid,
        USSPChatCartridgeComponent component,
        USSPChatLeaveGroup payload)
    {
        LeaveGroup(cartridgeUid, component, payload.GroupId);
        if (component.ActiveChat == payload.GroupId)
            component.ActiveChat = null;

        return null;
    }

    private static List<DiscoveryEntry>? HandleDiscoveryVisibility(
        USSPChatCartridgeComponent component,
        USSPChatSetVisibleInDiscovery payload)
    {
        component.VisibleInDiscovery = payload.Visible;
        return null;
    }

    private List<DiscoveryEntry> BuildDiscoveryList(string excludeChatId)
    {
        var list = new List<DiscoveryEntry>();
        foreach (var (chatId, cartUid) in _activeChats)
        {
            if (chatId == excludeChatId)
                continue;
            if (!TryComp<USSPChatCartridgeComponent>(cartUid, out var comp) || !comp.VisibleInDiscovery)
                continue;
            list.Add(new DiscoveryEntry(comp.ChatId, comp.OwnerName ?? chatId));
        }
        return list;
    }

    private void EnsureOwnerName(EntityUid uid, USSPChatCartridgeComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.OwnerName) &&
            component.OwnerName != Loc.GetString("generic-unknown-title"))
            return;

        if (TryResolvePdaOwnerName(uid, out var pdaOwnerName))
        {
            component.OwnerName = pdaOwnerName;
            return;
        }

        var ownerUid = ResolveOwnerEntity(uid);
        component.OwnerName = ownerUid.IsValid()
            ? MetaData(ownerUid).EntityName
            : string.Empty;

        if (string.IsNullOrWhiteSpace(component.OwnerName))
            component.OwnerName = Loc.GetString("generic-unknown-title");
    }

    private bool TryResolvePdaOwnerName(EntityUid uid, out string ownerName)
    {
        ownerName = string.Empty;

        if (!TryComp<CartridgeComponent>(uid, out var cartridge) ||
            !cartridge.LoaderUid.HasValue ||
            !TryComp<PdaComponent>(cartridge.LoaderUid.Value, out var pda))
        {
            return false;
        }

        if (pda.ContainedId is { } idUid &&
            TryComp<IdCardComponent>(idUid, out var idCard) &&
            !string.IsNullOrWhiteSpace(idCard.FullName))
        {
            ownerName = idCard.FullName;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(pda.OwnerName))
        {
            ownerName = pda.OwnerName;
            return true;
        }

        if (pda.PdaOwner is { } pdaOwner && pdaOwner.IsValid())
        {
            ownerName = MetaData(pdaOwner).EntityName;
            return !string.IsNullOrWhiteSpace(ownerName);
        }

        return false;
    }

    private EntityUid ResolveOwnerEntity(EntityUid uid)
    {
        var containerOwner = ResolveContainerOwner(uid);
        if (containerOwner.IsValid())
            return containerOwner;

        return ResolveParentOwner(uid);
    }

    private EntityUid ResolveContainerOwner(EntityUid uid)
    {
        var current = uid;
        var fallback = EntityUid.Invalid;

        while (_container.TryGetContainingContainer((current, null, null), out var container))
        {
            current = container.Owner;
            fallback = current;

            if (HasComp<MobStateComponent>(current))
                return current;
        }

        return fallback;
    }

    private EntityUid ResolveParentOwner(EntityUid uid)
    {
        var current = uid;
        var fallback = uid;

        while (true)
        {
            var parent = Transform(current).ParentUid;
            if (!parent.IsValid() || parent == current)
                return fallback;

            if (HasComp<MapComponent>(parent) || HasComp<MapGridComponent>(parent))
                return fallback;

            fallback = parent;

            if (HasComp<MobStateComponent>(parent))
                return parent;

            current = parent;
        }
    }

    private static void AddContact(USSPChatCartridgeComponent component, string contactId, string contactName)
    {
        var id = NormalizeContactId(Truncate(contactId, MaxContactIdLength));
        var name = Truncate(contactName, MaxContactNameLength);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            return;

        var unread = component.Contacts.TryGetValue(id, out var existing) && existing.HasUnread;
        component.Contacts[id] = new ChatContact(id, name, unread);
        RewriteContactHistory(component, id, name);
    }

    private static void RemoveContact(USSPChatCartridgeComponent component, string contactId)
    {
        component.Contacts.Remove(contactId);
        component.Messages.Remove(contactId);

        if (component.ActiveChat == contactId)
            component.ActiveChat = null;
    }

    private void CreateGroup(EntityUid cartridgeUid, USSPChatCartridgeComponent component, string groupName)
    {
        var name = Truncate(groupName, MaxGroupNameLength);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var id = GenerateUniqueGroupId();
        var session = new GroupSession(id, name);
        session.Members.Add(cartridgeUid);
        _groups[id] = session;
        SyncGroupState(id);
    }

    private string GenerateUniqueGroupId()
    {
        var buffer = new char[GroupIdLength];
        string id;
        do
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = GroupIdAlphabet[_random.Next(GroupIdAlphabet.Length)];
            }

            id = new string(buffer);
        } while (_groups.ContainsKey(id) || !IsValidGroupId(id));

        return id;
    }

    private void JoinGroup(EntityUid cartridgeUid, USSPChatCartridgeComponent component, string groupId)
    {
        var id = NormalizeGroupId(groupId);
        if (!IsValidGroupId(id))
            return;

        if (!_groups.TryGetValue(id, out var session))
            return;

        session.Members.Add(cartridgeUid);
        SyncGroupState(id);
    }

    private void LeaveGroup(EntityUid cartridgeUid, USSPChatCartridgeComponent component, string groupId)
    {
        if (!_groups.TryGetValue(groupId, out var session))
        {
            component.Groups.Remove(groupId);
            component.Messages.Remove(groupId);
            return;
        }

        session.Members.Remove(cartridgeUid);
        component.Groups.Remove(groupId);
        component.Messages.Remove(groupId);

        if (session.Members.Count == 0)
        {
            _groups.Remove(groupId);
        }
        else
        {
            SyncGroupState(groupId);
        }
    }

    private void RemoveCartridgeFromGroups(EntityUid cartridgeUid, USSPChatCartridgeComponent component)
    {
        foreach (var groupId in component.Groups.Keys.ToArray())
        {
            LeaveGroup(cartridgeUid, component, groupId);
        }
    }

    private bool SendMessage(
        EntityUid senderUid,
        USSPChatCartridgeComponent senderComp,
        string recipientId,
        string messageText,
        TimeSpan now)
    {
        var text = Truncate(messageText, MaxMessageLength);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var ownMessage = CreateOutgoingMessage(senderComp, text, now);

        if (senderComp.Groups.ContainsKey(recipientId))
        {
            SendGroupMessage(senderUid, senderComp, recipientId, text, now);
            return true;
        }

        if (!_activeChats.TryGetValue(recipientId, out var recipientUid) ||
            !TryComp<USSPChatCartridgeComponent>(recipientUid, out var recipientComp))
        {
            AppendOwnMessageOnly(senderComp, recipientId, ownMessage);
            return true;
        }

        if (!IsWithinRange(senderUid, recipientUid))
        {
            AppendOwnMessageOnly(senderComp, recipientId, ownMessage);
            return true;
        }

        DeliverDirectMessage(senderComp, recipientUid, recipientComp, recipientId, ownMessage, text, now);
        RecordMonitoredDirectMessage(senderComp, recipientUid, recipientComp, text, now);
        return true;
    }

    private void SendGroupMessage(
        EntityUid senderUid,
        USSPChatCartridgeComponent senderComp,
        string groupId,
        string text,
        TimeSpan timestamp)
    {
        if (!_groups.TryGetValue(groupId, out var session))
        {
            AppendOwnMessageOnly(senderComp, groupId, CreateOutgoingMessage(senderComp, text, timestamp));
            return;
        }

        foreach (var memberUid in session.Members.ToArray())
        {
            if (!TryComp<USSPChatCartridgeComponent>(memberUid, out var memberComp))
            {
                session.Members.Remove(memberUid);
                continue;
            }

            EnsureGroupEntry(memberComp, groupId, session.GroupName, session.Members.Count);

            if (memberUid != senderUid && !IsWithinRange(senderUid, memberUid))
            {
                UpdateUiState(memberUid, memberComp, discoveryList: null);
                continue;
            }

            var message = memberUid == senderUid
                ? CreateOutgoingMessage(senderComp, text, timestamp)
                : CreateIncomingMessage(senderComp.ChatId, BuildOwnDisplayName(senderComp), text, timestamp);

            AppendMessage(memberComp, groupId, message);

            if (memberUid != senderUid)
            {
                if (memberComp.ActiveChat == groupId)
                {
                    SetUnreadState(memberComp, groupId, false);
                }
                else
                {
                    SetUnreadState(memberComp, groupId, true);
                    TryPlayIncomingMessageSound(memberUid, memberComp);
                }
            }

            UpdateUiState(memberUid, memberComp, discoveryList: null);
        }

        RecordMonitoredGroupMessage(senderUid, senderComp, groupId, session, text, timestamp);
    }

    private static ChatMessage CreateOutgoingMessage(
        USSPChatCartridgeComponent component,
        string text,
        TimeSpan timestamp)
    {
        return new ChatMessage(
            component.ChatId,
            BuildOwnDisplayName(component),
            text,
            timestamp,
            true,
            true);
    }

    private static ChatMessage CreateIncomingMessage(
        string senderId,
        string senderName,
        string text,
        TimeSpan timestamp)
    {
        return new ChatMessage(
            senderId,
            string.IsNullOrWhiteSpace(senderName) ? senderId : senderName,
            text,
            timestamp,
            false,
            true);
    }

    private void AppendOwnMessageOnly(USSPChatCartridgeComponent component, string chatId, ChatMessage message)
    {
        AppendMessage(component, chatId, message);
        SetUnreadState(component, chatId, true);
    }

    private void DeliverDirectMessage(
        USSPChatCartridgeComponent senderComp,
        EntityUid recipientUid,
        USSPChatCartridgeComponent recipientComp,
        string recipientId,
        ChatMessage outgoingMessage,
        string text,
        TimeSpan timestamp)
    {
        var recipientViewingChat = recipientComp.ActiveChat == senderComp.ChatId;

        AppendMessage(senderComp, recipientId, outgoingMessage);
        AppendMessage(
            recipientComp,
            senderComp.ChatId,
            CreateIncomingMessage(senderComp.ChatId, BuildOwnDisplayName(senderComp), text, timestamp));
        EnsureRecipientContact(senderComp, recipientComp, !recipientViewingChat);

        if (recipientViewingChat)
        {
            SetUnreadState(recipientComp, senderComp.ChatId, false);
        }
        else
        {
            TryPlayIncomingMessageSound(recipientUid, recipientComp);
        }

        UpdateUiState(recipientUid, recipientComp, discoveryList: null);
    }

    private void RecordMonitoredDirectMessage(
        USSPChatCartridgeComponent senderComp,
        EntityUid recipientUid,
        USSPChatCartridgeComponent recipientComp,
        string text,
        TimeSpan timestamp)
    {
        var conversationId = BuildDirectConversationId(senderComp.ChatId, recipientComp.ChatId);
        var conversationName = BuildDirectConversationName(senderComp, recipientComp);
        var message = CreateIncomingMessage(senderComp.ChatId, BuildOwnDisplayName(senderComp), text, timestamp);

        var query = EntityQueryEnumerator<USSPChatCartridgeComponent>();
        while (query.MoveNext(out var uid, out var monitorComp))
        {
            if (!monitorComp.CanMonitorAllChats || uid == recipientUid)
                continue;

            EnsureMonitorContact(monitorComp, conversationId, conversationName);
            AppendMessage(monitorComp, conversationId, message);

            if (monitorComp.ActiveChat == conversationId)
                SetUnreadState(monitorComp, conversationId, false);
            else
                SetUnreadState(monitorComp, conversationId, true);

            UpdateUiState(uid, monitorComp, discoveryList: null);
        }
    }

    private void RecordMonitoredGroupMessage(
        EntityUid senderUid,
        USSPChatCartridgeComponent senderComp,
        string groupId,
        GroupSession session,
        string text,
        TimeSpan timestamp)
    {
        var message = CreateIncomingMessage(senderComp.ChatId, BuildOwnDisplayName(senderComp), text, timestamp);

        var query = EntityQueryEnumerator<USSPChatCartridgeComponent>();
        while (query.MoveNext(out var uid, out var monitorComp))
        {
            if (!monitorComp.CanMonitorAllChats || uid == senderUid || session.Members.Contains(uid))
                continue;

            EnsureGroupEntry(monitorComp, groupId, session.GroupName, session.Members.Count);
            AppendMessage(monitorComp, groupId, message);

            if (monitorComp.ActiveChat == groupId)
                SetUnreadState(monitorComp, groupId, false);
            else
                SetUnreadState(monitorComp, groupId, true);

            UpdateUiState(uid, monitorComp, discoveryList: null);
        }
    }

    private void TryPlayIncomingMessageSound(EntityUid recipientUid, USSPChatCartridgeComponent recipientComp)
    {
        if (recipientComp.MutedSound || !TryComp<CartridgeComponent>(recipientUid, out _))
            return;

        _audio.PlayPvs(recipientComp.Sound, recipientUid);
    }

    private static void EnsureRecipientContact(
        USSPChatCartridgeComponent senderComp,
        USSPChatCartridgeComponent recipientComp,
        bool hasUnread)
    {
        if (recipientComp.Contacts.TryGetValue(senderComp.ChatId, out var contact))
        {
            recipientComp.Contacts[senderComp.ChatId] =
                new ChatContact(contact.ContactId, contact.ContactName, hasUnread);
            return;
        }

        recipientComp.Contacts[senderComp.ChatId] =
            new ChatContact(senderComp.ChatId, senderComp.OwnerName, hasUnread);
    }

    private static void AppendMessage(USSPChatCartridgeComponent component, string chatId, ChatMessage message)
    {
        if (!component.Messages.TryGetValue(chatId, out var history))
        {
            history = new List<ChatMessage>();
            component.Messages[chatId] = history;
        }

        history.Add(message);
    }

    private void SyncGroupState(string groupId)
    {
        if (!_groups.TryGetValue(groupId, out var session))
            return;

        foreach (var memberUid in session.Members.ToArray())
        {
            if (!TryComp<USSPChatCartridgeComponent>(memberUid, out var memberComp))
            {
                session.Members.Remove(memberUid);
                continue;
            }

            EnsureGroupEntry(memberComp, groupId, session.GroupName, session.Members.Count);
            UpdateUiState(memberUid, memberComp, discoveryList: null);
        }
    }

    private static void EnsureGroupEntry(
        USSPChatCartridgeComponent component,
        string groupId,
        string groupName,
        int memberCount)
    {
        var unread = component.Groups.TryGetValue(groupId, out var group) && group.HasUnread;
        component.Groups[groupId] = new ChatGroup(groupId, groupName, unread, memberCount);
    }

    private bool IsWithinRange(EntityUid sender, EntityUid recipient)
    {
        var senderCoords = _transform.GetMapCoordinates(sender);
        var recipientCoords = _transform.GetMapCoordinates(recipient);
        if (senderCoords.MapId != recipientCoords.MapId)
            return false;
        return senderCoords.InRange(recipientCoords, MessageRange);
    }

    private void UpdateUiState(EntityUid cartridgeUid, USSPChatCartridgeComponent component, List<DiscoveryEntry>? discoveryList)
    {
        if (!TryComp<CartridgeComponent>(cartridgeUid, out var cart) || !cart.LoaderUid.HasValue)
            return;
        UpdateUiState(cartridgeUid, cart.LoaderUid.Value, component, discoveryList);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, USSPChatCartridgeComponent component, List<DiscoveryEntry>? discoveryList)
    {
        var state = new USSPChatUiState(
            component.ChatId,
            component.ActiveChat,
            component.MutedSound,
            component.VisibleInDiscovery,
            component.CanSendMessages,
            new Dictionary<string, ChatContact>(component.Contacts),
            new Dictionary<string, ChatGroup>(component.Groups),
            GetActiveMessages(component),
            discoveryList);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    private static List<ChatMessage>? GetActiveMessages(USSPChatCartridgeComponent component)
    {
        return !string.IsNullOrEmpty(component.ActiveChat) &&
               component.Messages.TryGetValue(component.ActiveChat, out var activeMessages)
            ? activeMessages
            : null;
    }

    private static void RewriteContactHistory(USSPChatCartridgeComponent component, string contactId, string contactName)
    {
        if (!component.Messages.TryGetValue(contactId, out var history))
            return;

        component.Messages[contactId] = history
            .Select(message => message.IsOwnMessage || message.SenderId != contactId
                ? message
                : new ChatMessage(
                    message.SenderId,
                    contactName,
                    message.Message,
                    message.Timestamp,
                    message.IsOwnMessage,
                    message.Delivered))
            .ToList();
    }

    private static void SetUnreadState(USSPChatCartridgeComponent component, string id, bool unread)
    {
        if (component.Contacts.TryGetValue(id, out var contact))
            component.Contacts[id] = new ChatContact(contact.ContactId, contact.ContactName, unread);

        if (component.Groups.TryGetValue(id, out var group))
            component.Groups[id] = new ChatGroup(group.GroupId, group.GroupName, unread, group.MemberCount);
    }

    private static void ClearUnread(USSPChatCartridgeComponent component, string id)
    {
        SetUnreadState(component, id, false);
    }

    private static void EnsureMonitorContact(USSPChatCartridgeComponent component, string contactId, string contactName)
    {
        var unread = component.Contacts.TryGetValue(contactId, out var existing) && existing.HasUnread;
        component.Contacts[contactId] = new ChatContact(contactId, contactName, unread);
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLen ? value : value[..maxLen];
    }

    private static string NormalizeContactId(string id)
    {
        var s = id.Trim().Replace(" ", "");
        if (s.Length > 0 && s[0] != '#' && s.All(char.IsDigit))
            return "#" + s;
        return s;
    }

    private static string NormalizeGroupId(string id)
    {
        return id.Trim().Replace(" ", "").ToUpperInvariant();
    }

    private static bool IsValidGroupId(string id)
    {
        return id.Length == GroupIdLength
               && id.All(static c => c is >= 'A' and <= 'Z' or >= '0' and <= '9')
               && id.Any(char.IsAsciiLetter)
               && id.Any(char.IsAsciiDigit);
    }

    private static string BuildOwnDisplayName(USSPChatCartridgeComponent component)
    {
        return $"{component.OwnerName} ({component.ChatId})";
    }

    private static string BuildDirectConversationId(string firstChatId, string secondChatId)
    {
        return string.CompareOrdinal(firstChatId, secondChatId) <= 0
            ? $"DM:{firstChatId}:{secondChatId}"
            : $"DM:{secondChatId}:{firstChatId}";
    }

    private static string BuildDirectConversationName(
        USSPChatCartridgeComponent firstComp,
        USSPChatCartridgeComponent secondComp)
    {
        return string.CompareOrdinal(firstComp.ChatId, secondComp.ChatId) <= 0
            ? $"{BuildOwnDisplayName(firstComp)} <-> {BuildOwnDisplayName(secondComp)}"
            : $"{BuildOwnDisplayName(secondComp)} <-> {BuildOwnDisplayName(firstComp)}";
    }

    private sealed class GroupSession
    {
        public string GroupId { get; }
        public string GroupName { get; }
        public HashSet<EntityUid> Members { get; } = new();

        public GroupSession(string groupId, string groupName)
        {
            GroupId = groupId;
            GroupName = groupName;
        }
    }
}
