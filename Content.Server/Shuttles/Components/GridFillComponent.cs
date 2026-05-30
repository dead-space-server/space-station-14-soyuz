using Content.Server.Shuttles.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Shuttles.Components;

/// <summary>
/// If added to an airlock will try to autofill a grid onto it on MapInit
/// </summary>
[RegisterComponent, Access(typeof(ShuttleSystem))]
public sealed partial class GridFillComponent : Component
{
    /// <summary>
    /// Single path to the shuttle map file.
    /// </summary>
    [DataField("path")]
    public ResPath Path = new("/Maps/Shuttles/escape_pod_small.yml");
// DS14-Soyuz-start
    /// <summary>
    /// List of possible paths to choose from randomly.
    /// </summary>
    [DataField("paths")]
    public List<ResPath> Paths = new();
// DS14-Soyuz-end

    /// <summary>
    /// Components to be added to any spawned grids.
    /// </summary>
    [DataField]
    public ComponentRegistry AddComponents = new();
}
