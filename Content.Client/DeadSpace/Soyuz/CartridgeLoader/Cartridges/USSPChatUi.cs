using Content.Client.UserInterface.Fragments;
using Content.Shared.DeadSpace.Soyuz.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace.Soyuz.CartridgeLoader.Cartridges;

public sealed partial class USSPChatUi : UIFragment
{
    private USSPChatUiFragment? _fragment;
    private USSPChatAddContactPopup? _addContactPopup;
    private USSPChatDiscoveryPopup? _discoveryPopup;
    private USSPChatJoinGroupPopup? _joinGroupPopup;
    private USSPChatCreateGroupPopup? _createGroupPopup;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        InitializeControls();
        WireInterface(userInterface);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is USSPChatUiState chatState)
            _fragment?.UpdateState(chatState);
    }

    private void InitializeControls()
    {
        _fragment = new USSPChatUiFragment();
        _addContactPopup = new USSPChatAddContactPopup();
        _discoveryPopup = new USSPChatDiscoveryPopup();
        _joinGroupPopup = new USSPChatJoinGroupPopup();
        _createGroupPopup = new USSPChatCreateGroupPopup();
        _fragment.InitializeEmojiPicker();
    }

    private void WireInterface(BoundUserInterface userInterface)
    {
        var fragment = _fragment!;
        var addContactPopup = _addContactPopup!;
        var discoveryPopup = _discoveryPopup!;
        var joinGroupPopup = _joinGroupPopup!;
        var createGroupPopup = _createGroupPopup!;

        fragment.OpenAddContact += addContactPopup.OpenCentered;
        fragment.OpenDiscovery += () => SendPayload(userInterface, new USSPChatRequestDiscoveryList());
        fragment.SetVisibleInDiscovery += visible =>
            SendPayload(userInterface, new USSPChatSetVisibleInDiscovery(visible));
        fragment.DiscoveryListReceived += list => ShowDiscoveryPopup(discoveryPopup, list);
        fragment.JoinGroup += joinGroupPopup.OpenCentered;
        fragment.CreateGroup += createGroupPopup.OpenCentered;
        fragment.EraseChat += chatId => SendPayload(userInterface, new USSPChatEraseContact(chatId));
        fragment.LeaveChat += groupId => SendPayload(userInterface, new USSPChatLeaveGroup(groupId));
        fragment.OpenEmojiPicker += fragment.OpenEmojiPickerInternal;
        fragment.OnMutePressed += () => SendPayload(userInterface, new USSPChatMuted());
        fragment.SetActiveChat += chatId => SendPayload(userInterface, new USSPChatSetActiveChat(chatId));
        fragment.SendMessage += message => SendMessage(userInterface, fragment, message);

        addContactPopup.OnContactAdded +=
            (contactId, contactName) => SendPayload(userInterface, new USSPChatAddContact(contactId, contactName));
        discoveryPopup.OnAddContact +=
            (contactId, contactName) => SendPayload(userInterface, new USSPChatAddContact(contactId, contactName));
        joinGroupPopup.OnGroupJoined += groupId =>
        {
            SendPayload(userInterface, new USSPChatJoinGroup(groupId));
            return true;
        };
        createGroupPopup.OnGroupCreated += groupName => SendPayload(userInterface, new USSPChatCreateGroup(groupName));
    }

    private static void SendPayload(BoundUserInterface userInterface, IUSSPChatUiMessagePayload payload)
    {
        userInterface.SendMessage(new CartridgeUiMessage(new USSPChatUiMessageEvent(payload)));
    }

    private static void ShowDiscoveryPopup(USSPChatDiscoveryPopup popup, List<DiscoveryEntry> entries)
    {
        popup.SetEntries(entries);
        popup.OpenCentered();
    }

    private static void SendMessage(BoundUserInterface userInterface, USSPChatUiFragment fragment, string message)
    {
        if (fragment.ActiveChatId == null)
            return;

        SendPayload(userInterface, new USSPChatSendMessage(fragment.ActiveChatId, message));
    }
}
