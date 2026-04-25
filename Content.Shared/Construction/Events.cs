using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Construction;

/// <summary>
///     Sent client -> server to to tell the server that we started building
///     a structure-construction.
/// </summary>
[Serializable, NetSerializable]
public sealed class TryStartStructureConstructionMessage : EntityEventArgs
{
    /// <summary>
    ///     Position to start building.
    /// </summary>
    public readonly NetCoordinates Location;

    /// <summary>
    ///     The construction prototype to start building.
    /// </summary>
    public readonly string PrototypeName;

    public readonly Angle Angle;

    /// <summary>
    ///     Identifier to be sent back in the acknowledgement so that the client can clean up its ghost.
    /// </summary>
    /// <remarks>
    /// So essentially the client is sending its own entity to the server so it knows to delete it when it gets server
    /// response back.
    /// </remarks>
    public readonly int Ack;

    public TryStartStructureConstructionMessage(NetCoordinates loc, string prototypeName, Angle angle, int ack)
    {
        Location = loc;
        PrototypeName = prototypeName;
        Angle = angle;
        Ack = ack;
    }
}

/// <summary>
///     Sent client -> server to to tell the server that we started building
///     an item-construction.
/// </summary>
[Serializable, NetSerializable]
public sealed class TryStartItemConstructionMessage : EntityEventArgs
{
    /// <summary>
    ///     The construction prototype to start building.
    /// </summary>
    public readonly string PrototypeName;

    public TryStartItemConstructionMessage(string prototypeName)
    {
        PrototypeName = prototypeName;
    }
}

/// <summary>
/// Sent server -> client to tell the client that a ghost has started to be constructed.
/// </summary>
[Serializable, NetSerializable]
public sealed class AckStructureConstructionMessage : EntityEventArgs
{
    public readonly int GhostId;

    /// <summary>
    ///     The entity that is now being constructed, if any.
    /// </summary>
    public readonly NetEntity? Uid;

    public AckStructureConstructionMessage(int ghostId, NetEntity? uid = null)
    {
        GhostId = ghostId;
        Uid = uid;
    }
}

/// <summary>
/// Sent client -> server to request a specific construction guide.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestConstructionGuide : EntityEventArgs
{
    public readonly string ConstructionId;

    public RequestConstructionGuide(string constructionId)
    {
        ConstructionId = constructionId;
    }
}

/// <summary>
/// Sent server -> client as a response to a <see cref="RequestConstructionGuide"/> net message.
/// </summary>
[Serializable, NetSerializable]
public sealed class ResponseConstructionGuide : EntityEventArgs
{
    public readonly string ConstructionId;
    public readonly ConstructionGuide Guide;

    public ResponseConstructionGuide(string constructionId, ConstructionGuide guide)
    {
        ConstructionId = constructionId;
        Guide = guide;
    }
}

// DS14-start: server-authored construction ghost preview for stacked ghosts and real atmos connections.
[Serializable, NetSerializable]
public sealed class ConstructionGhostPlan
{
    public readonly int GhostId;
    public readonly NetCoordinates Location;
    public readonly string PrototypeName;
    public readonly Angle Angle;

    public ConstructionGhostPlan(int ghostId, NetCoordinates location, string prototypeName, Angle angle)
    {
        GhostId = ghostId;
        Location = location;
        PrototypeName = prototypeName;
        Angle = angle;
    }
}

[Serializable, NetSerializable]
public sealed class ConstructionGhostPreviewData
{
    public readonly int GhostId;
    public readonly bool HasPipeVisualState;
    public readonly int PipeVisualState;
    public readonly byte PipeLayers;

    public ConstructionGhostPreviewData(int ghostId, bool hasPipeVisualState = false, int pipeVisualState = 0, byte pipeLayers = 0)
    {
        GhostId = ghostId;
        HasPipeVisualState = hasPipeVisualState;
        PipeVisualState = pipeVisualState;
        PipeLayers = pipeLayers;
    }
}

[Serializable, NetSerializable]
public sealed class RequestConstructionGhostsPreviewMessage : EntityEventArgs
{
    public readonly int Revision;
    public readonly bool HasCandidateGhostId;
    public readonly int CandidateGhostId;
    public readonly List<ConstructionGhostPlan> Ghosts;

    public RequestConstructionGhostsPreviewMessage(int revision, bool hasCandidateGhostId, int candidateGhostId, List<ConstructionGhostPlan> ghosts)
    {
        Revision = revision;
        HasCandidateGhostId = hasCandidateGhostId;
        CandidateGhostId = candidateGhostId;
        Ghosts = ghosts;
    }
}

[Serializable, NetSerializable]
public sealed class ResponseConstructionGhostsPreviewMessage : EntityEventArgs
{
    public readonly int Revision;
    public readonly bool HasCandidateGhostId;
    public readonly int CandidateGhostId;
    public readonly bool CandidateAccepted;
    public readonly List<ConstructionGhostPreviewData> Ghosts;

    public ResponseConstructionGhostsPreviewMessage(
        int revision,
        bool hasCandidateGhostId,
        int candidateGhostId,
        bool candidateAccepted,
        List<ConstructionGhostPreviewData> ghosts)
    {
        Revision = revision;
        HasCandidateGhostId = hasCandidateGhostId;
        CandidateGhostId = candidateGhostId;
        CandidateAccepted = candidateAccepted;
        Ghosts = ghosts;
    }
}
// DS14-end

[Serializable, NetSerializable]
public sealed partial class ConstructionInteractDoAfterEvent : DoAfterEvent
{
    [DataField("clickLocation")]
    public NetCoordinates ClickLocation;

    private ConstructionInteractDoAfterEvent()
    {
    }

    public ConstructionInteractDoAfterEvent(IEntityManager entManager, InteractUsingEvent ev)
    {
        ClickLocation = entManager.GetNetCoordinates(ev.ClickLocation);
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class WelderRefineDoAfterEvent : SimpleDoAfterEvent
{
}
