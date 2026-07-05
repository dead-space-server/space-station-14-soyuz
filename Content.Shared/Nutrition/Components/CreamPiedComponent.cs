using Content.Shared.DisplacementMap;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Nutrition.Components
{
    [Access(typeof(SharedCreamPieSystem))]
    [RegisterComponent]
    public sealed partial class CreamPiedComponent : Component
    {
        [ViewVariables]
        public bool CreamPied { get; set; } = false;
    }

    /// <summary>
    /// The sprite to draw on someone's face if they were hit by a pie.
    /// The layer will be dynamically added with the component.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Sprite;

    /// <summary>
    /// If set, applies a displacement map to the pie sprite.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<DisplacementDataPrototype>? Displacement;
}

/// <summary>
/// Key to be used with appearance data, indicating if the entity has a banana cream pie in their face.
/// </summary>
[Serializable, NetSerializable]
public enum CreamPiedVisuals
{
    Creamed,
}

/// <summary>
/// The visual layer for the creampied face.
/// Will be dynamically added and removed with the component.
/// </summary>
[Serializable, NetSerializable]
public enum CreamPiedVisualLayer
{
    Key,
}
