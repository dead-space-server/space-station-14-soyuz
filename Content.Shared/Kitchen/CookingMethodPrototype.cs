using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen;

/// <summary>
/// Assigns existing meal recipes to the vessel or preparation surface that makes them.
/// The microwave recipes remain available during migration.
/// </summary>
[Prototype]
public sealed partial class CookingMethodPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Appliance { get; private set; } = default!;

    [DataField]
    public List<string> Recipes { get; private set; } = new();
}
