using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.MaterialMarketplace;

[NetSerializable, Serializable]
public sealed class MaterialMarketplaceState(
    Dictionary<string, int> availableMaterials,
    Dictionary<string, double> prices)
    : BoundUserInterfaceState
{
    public readonly Dictionary<string, int> AvailableMaterials = availableMaterials;
    public readonly Dictionary<string, double> Prices = prices;
}

[NetSerializable, Serializable]
public sealed class MaterialMarketplaceBuyMessage : BoundUserInterfaceMessage
{
    public readonly string MaterialId;
    public readonly int Amount;

    public MaterialMarketplaceBuyMessage(string materialId, int amount)
    {
        MaterialId = materialId;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public enum MaterialMarketplaceUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum MaterialMarketplaceVisuals : byte
{
    FillLevel
}

[Prototype]
public sealed partial class MaterialMarketplaceCategoryPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("order")]
    public int Order { get; private set; }

    [DataField("materials")]
    public HashSet<string> Materials { get; private set; } = new();
}
