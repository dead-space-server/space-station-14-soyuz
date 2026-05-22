using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace.MaterialMarketplace
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class MaterialMarketplaceComponent : Component
    {
        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public int FillLevels = 6;

        [ViewVariables]
        public Dictionary<string, int> MaterialStock = new();

        [DataField("whitelistTags")]
        public HashSet<string> WhitelistTags { get; private set; } = new();

        [DataField("blacklistTags")]
        public HashSet<string> BlacklistTags { get; private set; } = new();

        [DataField("whitelistMaterials")]
        public HashSet<ProtoId<MaterialPrototype>> WhitelistMaterials { get; private set; } = new();

        [DataField("blacklistMaterials")]
        public HashSet<ProtoId<MaterialPrototype>> BlacklistMaterials { get; private set; } = new();
    }
}
