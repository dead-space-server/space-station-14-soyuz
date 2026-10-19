// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Sticky;
using Content.Shared.Sticky.Components;
using Content.Shared.Sticky.Systems;
using Content.Shared.Wall;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using static Content.Shared.Interaction.SharedInteractionSystem;

namespace Content.Shared.DeadSpace._Soyuz.Paper;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class HandmadePosterComponent : Component
{
    [DataField, AutoNetworkedField]
    public byte[] Pixels = Array.Empty<byte>();

    [DataField, AutoNetworkedField]
    public int DrawingRevision;

    public TimeSpan NextDrawingSave;
}

/// <summary>
/// Keeps a wall-mounted poster accessible without bypassing range or obstruction checks.
/// Attachment and removal are handled by the existing sticky item system.
/// </summary>
public sealed class HandmadePosterSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly StickySystem _sticky = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier DrawingSound = new SoundCollectionSpecifier("PaperScribbles");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HandmadePosterComponent, AccessibleOverrideEvent>(OnAccessible);
        SubscribeLocalEvent<HandmadePosterComponent, AttemptEntityStickEvent>(OnStickAttempt);
        SubscribeLocalEvent<HandmadePosterComponent, AttemptEntityUnstickEvent>(OnUnstickAttempt);
        SubscribeLocalEvent<HandmadePosterComponent, GettingPickedUpAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<HandmadePosterComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<HandmadePosterComponent, GetVerbsEvent<Verb>>(OnDrawingVerb);
        SubscribeLocalEvent<HandmadePosterComponent, BoundUIOpenedEvent>(OnDrawingOpened);
        SubscribeLocalEvent<HandmadePosterComponent, HandmadePosterDrawingSaveMessage>(OnDrawingSave);
    }

    private void OnDrawingVerb(Entity<HandmadePosterComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("handmade-poster-drawing-verb"),
            Act = () =>
            {
                if (_net.IsServer && !IsUnavailable(ent.Owner) && !IsUnavailable(user) &&
                    _blocker.CanInteract(user, ent.Owner) &&
                    _interaction.InRangeAndAccessible(user, ent.Owner))
                    _ui.OpenUi(ent.Owner, HandmadePosterUiKey.Drawing, user);
            },
        });
    }

    private void OnDrawingOpened(Entity<HandmadePosterComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_net.IsServer && Equals(args.UiKey, HandmadePosterUiKey.Drawing))
            UpdateDrawingUi(ent);
    }

    /// <summary>
    /// Artwork can only be edited on an attached poster while holding a writing tool.
    /// The server repeats these checks when saving, independent of the editor controls.
    /// </summary>
    public bool CanDraw(EntityUid poster, EntityUid user)
    {
        return !IsUnavailable(poster) && !IsUnavailable(user) && !MetaData(poster).EntityPaused &&
            IsAttached(poster, out _) &&
            _blocker.CanInteract(user, poster) && _blocker.CanComplexInteract(user) &&
            _interaction.InRangeAndAccessible(user, poster) &&
            _hands.GetActiveItem(user) is { } pen && _tags.HasTag(pen, "Write");
    }

    private void OnDrawingSave(Entity<HandmadePosterComponent> ent, ref HandmadePosterDrawingSaveMessage args)
    {
        if (!_net.IsServer || !Equals(args.UiKey, HandmadePosterUiKey.Drawing) ||
            !_ui.IsUiOpen(ent.Owner, HandmadePosterUiKey.Drawing, args.Actor))
            return;

        string? error = null;
        if (!CanDraw(ent.Owner, args.Actor))
            error = "handmade-poster-drawing-readonly";
        else if (args.Revision != ent.Comp.DrawingRevision)
            error = "handmade-poster-drawing-conflict";
        else if (_timing.CurTime < ent.Comp.NextDrawingSave)
            error = "handmade-poster-drawing-wait";
        else if (!HandmadePosterDrawing.IsValid(args.Pixels))
            error = "handmade-poster-drawing-invalid";

        if (error != null)
        {
            SendDrawingResult(ent, args.Actor, args.RequestId, error);
            return;
        }

        // Store a separate, immutable snapshot only after every check has succeeded.
        var hasInk = false;
        foreach (var pixel in args.Pixels)
            hasInk |= pixel != 0;

        ent.Comp.Pixels = hasInk ? (byte[]) args.Pixels.Clone() : Array.Empty<byte>();
        ent.Comp.DrawingRevision++;
        ent.Comp.NextDrawingSave = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        Dirty(ent.Owner, ent.Comp);
        UpdateDrawingUi(ent);
        SendDrawingResult(ent, args.Actor, args.RequestId, null);
        _audio.PlayPvs(DrawingSound, ent.Owner);
    }

    private void UpdateDrawingUi(Entity<HandmadePosterComponent> ent)
    {
        _ui.SetUiState(ent.Owner, HandmadePosterUiKey.Drawing,
            new HandmadePosterDrawingState(ent.Comp.Pixels, ent.Comp.DrawingRevision));
    }

    private void SendDrawingResult(Entity<HandmadePosterComponent> ent, EntityUid user, Guid requestId, string? error)
    {
        _ui.ServerSendUiMessage(ent.Owner, HandmadePosterUiKey.Drawing,
            new HandmadePosterDrawingResult(ent.Comp.Pixels, ent.Comp.DrawingRevision, requestId, error), user);
    }

    private bool IsAttached(EntityUid uid, out EntityUid wall)
    {
        wall = default;
        if (!TryComp<StickyComponent>(uid, out var sticky) || sticky.StuckTo is not { } target ||
            IsUnavailable(target) || !HasComp<WallComponent>(target) ||
            !_containers.TryGetContainingContainer((uid, null, null), out var container) ||
            container.Owner != target || !container.ShowContents)
            return false;

        wall = target;
        return true;
    }

    private void OnAccessible(Entity<HandmadePosterComponent> ent, ref AccessibleOverrideEvent args)
    {
        if (args.Target != ent.Owner || args.Handled || !IsAttached(ent.Owner, out var wall) ||
            !_interaction.IsAccessible(args.User, wall))
            return;

        args.Handled = true;
        args.Accessible = true;
    }

    private void OnStickAttempt(Entity<HandmadePosterComponent> ent, ref AttemptEntityStickEvent args)
    {
        // Also runs immediately before insertion, so a stale or repeated attachment cannot move a poster.
        if (IsUnavailable(ent.Owner) || IsUnavailable(args.Target) || IsUnavailable(args.User) ||
            !_blocker.CanInteract(args.User, ent.Owner) || !_blocker.CanComplexInteract(args.User) ||
            !HasComp<WallComponent>(args.Target) ||
            !TryComp<StickyComponent>(ent.Owner, out var sticky) || sticky.StuckTo != null ||
            _hands.GetActiveItem(args.User) != ent.Owner ||
            !_interaction.InRangeAndAccessible(args.User, args.Target))
            args.Cancelled = true;
    }

    private void OnUnstickAttempt(Entity<HandmadePosterComponent> ent, ref AttemptEntityUnstickEvent args)
    {
        if (IsUnavailable(ent.Owner) || IsUnavailable(args.User) ||
            !_blocker.CanInteract(args.User, ent.Owner) || !_blocker.CanComplexInteract(args.User) ||
            !IsAttached(ent.Owner, out var wall) || wall != args.Target ||
            !_interaction.InRangeAndAccessible(args.User, ent.Owner))
            args.Cancelled = true;
    }

    private void OnPickupAttempt(EntityUid uid, HandmadePosterComponent component, GettingPickedUpAttemptEvent args)
    {
        // An empty-hand click views the drawing; the existing unstick verb removes the poster.
        if (IsAttached(uid, out _))
            args.Cancel();
    }

    private void OnRemoved(EntityUid uid, HandmadePosterComponent component, EntGotRemovedFromContainerMessage args)
    {
        if (_timing.ApplyingState || TerminatingOrDeleted(uid))
            return;

        if (TryComp<StickyComponent>(uid, out var sticky) && sticky.StuckTo == args.Container.Owner)
            _sticky.RefreshStuckState((uid, sticky));
    }

    private bool IsUnavailable(EntityUid uid) => TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid);
}

public static class HandmadePosterDrawing
{
    public const int Width = 80;
    public const int Height = 104;
    public const int PixelCount = Width * Height;

    // Index zero is transparent: the eraser reveals the paper underneath.
    public static readonly Color[] Palette =
    {
        Color.Transparent, Color.FromHex("#202020"), Color.White, Color.FromHex("#929292"),
        Color.FromHex("#D32F2F"), Color.FromHex("#EF7D32"), Color.FromHex("#F4D35E"), Color.FromHex("#55A630"),
        Color.FromHex("#26C6DA"), Color.FromHex("#3366CC"), Color.FromHex("#7B3FB2"), Color.FromHex("#EE77AA"),
        Color.FromHex("#815438"), Color.FromHex("#285C38"), Color.FromHex("#A8DADC"), Color.FromHex("#4B4B4B"),
        Color.FromHex("#E8B796"),
    };

    public static bool IsValid(byte[]? pixels)
    {
        if (pixels == null || pixels.Length != 0 && pixels.Length != PixelCount)
            return false;

        foreach (var pixel in pixels)
        {
            if (pixel >= Palette.Length)
                return false;
        }
        return true;
    }
}

[Serializable, NetSerializable]
public enum HandmadePosterUiKey : byte { Drawing }

[Serializable, NetSerializable]
public enum HandmadePosterVisualLayers : byte { Drawing }

[Serializable, NetSerializable]
public sealed class HandmadePosterDrawingState(byte[] pixels, int revision) : BoundUserInterfaceState
{
    public readonly byte[] Pixels = pixels;
    public readonly int Revision = revision;
}

[Serializable, NetSerializable]
public sealed class HandmadePosterDrawingSaveMessage(byte[] pixels, int revision, Guid requestId) : BoundUserInterfaceMessage
{
    public readonly byte[] Pixels = pixels;
    public readonly int Revision = revision;
    public readonly Guid RequestId = requestId;
}

[Serializable, NetSerializable]
public sealed class HandmadePosterDrawingResult(byte[] pixels, int revision, Guid requestId, string? error) : BoundUserInterfaceMessage
{
    public readonly byte[] Pixels = pixels;
    public readonly int Revision = revision;
    public readonly Guid RequestId = requestId;
    public readonly string? Error = error;
}
