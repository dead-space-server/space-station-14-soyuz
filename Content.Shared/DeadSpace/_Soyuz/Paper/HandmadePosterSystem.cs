// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Wall;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.DeadSpace._Soyuz.Paper;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class HandmadePosterComponent : Component
{
    [DataField, AutoNetworkedField]
    public uint[] Pixels = Array.Empty<uint>();

    [DataField, AutoNetworkedField]
    public int DrawingRevision;

    public TimeSpan NextDrawingSave;
}

/// <summary>
/// Validates editing of a constructed wall poster. Construction also handles dismantling it.
/// </summary>
public sealed class HandmadePosterSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier DrawingSound = new SoundCollectionSpecifier("PaperScribbles");
    private static readonly ProtoId<TagPrototype> WriteTag = "Write";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HandmadePosterComponent, GettingPickedUpAttemptEvent>(OnPickupAttempt);
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
            IsAttached(poster) &&
            _blocker.CanInteract(user, poster) && _blocker.CanComplexInteract(user) &&
            _interaction.InRangeAndAccessible(user, poster) &&
            _hands.GetActiveItem(user) is { } pen && _tags.HasTag(pen, WriteTag);
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

        ent.Comp.Pixels = hasInk ? (uint[]) args.Pixels.Clone() : Array.Empty<uint>();
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

    private bool IsAttached(EntityUid uid)
    {
        var xform = Transform(uid);
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        foreach (var wall in _map.GetAnchoredEntities(gridUid, grid, xform.Coordinates))
        {
            if (!IsUnavailable(wall) && HasComp<WallComponent>(wall))
                return true;
        }
        return false;
    }

    private void OnPickupAttempt(EntityUid uid, HandmadePosterComponent component, GettingPickedUpAttemptEvent args)
    {
        // Viewing is allowed, but the poster can only be removed by dismantling it.
        args.Cancel();
    }

    private bool IsUnavailable(EntityUid uid) => TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid);
}

public static class HandmadePosterDrawing
{
    public const int Width = 80;
    public const int Height = 104;
    public const int PixelCount = Width * Height;

    // Preset brush colors; saved pixels contain packed RGB with an opaque alpha byte.
    public static readonly Color[] Palette =
    {
        Color.Transparent, Color.FromHex("#202020"), Color.White, Color.FromHex("#929292"),
        Color.FromHex("#D32F2F"), Color.FromHex("#EF7D32"), Color.FromHex("#F4D35E"), Color.FromHex("#55A630"),
        Color.FromHex("#26C6DA"), Color.FromHex("#3366CC"), Color.FromHex("#7B3FB2"), Color.FromHex("#EE77AA"),
        Color.FromHex("#815438"), Color.FromHex("#285C38"), Color.FromHex("#A8DADC"), Color.FromHex("#4B4B4B"),
        Color.FromHex("#E8B796"),
    };

    public static uint Pack(Color color) => ((uint) color.RByte << 24) | ((uint) color.GByte << 16) |
        ((uint) color.BByte << 8) | 0xFFu;

    public static bool IsValid(uint[]? pixels)
    {
        if (pixels == null || pixels.Length != 0 && pixels.Length != PixelCount)
            return false;

        foreach (var pixel in pixels)
        {
            // Zero is the eraser. Partial transparency is not part of RGB drawing.
            if (pixel != 0 && (pixel & 0xFFu) != 0xFFu)
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
public sealed class HandmadePosterDrawingState(uint[] pixels, int revision) : BoundUserInterfaceState
{
    public readonly uint[] Pixels = pixels;
    public readonly int Revision = revision;
}

[Serializable, NetSerializable]
public sealed class HandmadePosterDrawingSaveMessage(uint[] pixels, int revision, Guid requestId) : BoundUserInterfaceMessage
{
    public readonly uint[] Pixels = pixels;
    public readonly int Revision = revision;
    public readonly Guid RequestId = requestId;
}

[Serializable, NetSerializable]
public sealed class HandmadePosterDrawingResult(uint[] pixels, int revision, Guid requestId, string? error) : BoundUserInterfaceMessage
{
    public readonly uint[] Pixels = pixels;
    public readonly int Revision = revision;
    public readonly Guid RequestId = requestId;
    public readonly string? Error = error;
}
