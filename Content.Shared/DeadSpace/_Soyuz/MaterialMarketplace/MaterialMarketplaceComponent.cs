// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.Access;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.MaterialMarketplace
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

        /// <summary>
        /// Id контейнера, в котором лежат вставленные предметы (не материалы).
        /// Достать их оттуда можно только купив.
        /// </summary>
        [DataField]
        public string ItemContainerId = "marketplace_items";

        /// <summary>
        /// Можно ли вставлять в торгомат обычные предметы для продажи.
        /// </summary>
        [DataField]
        public bool AllowItems = true;

        /// <summary>
        /// Тип стопки, которая считается наличными.
        /// </summary>
        [DataField]
        public ProtoId<StackPrototype> CashStackType = "Credit";

        /// <summary>
        /// Деньги, внесённые наличкой и ещё не потраченные.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public int DepositedCash;

        /// <summary>
        /// Счёт станции, на который поступают все деньги от продаж.
        /// </summary>
        [DataField]
        public ProtoId<CargoAccountPrototype> SalesAccount = "Cargo";

        /// <summary>
        /// Какой доступ (глава отдела) даёт право платить с какого счёта отдела.
        /// </summary>
        [DataField]
        public Dictionary<ProtoId<AccessLevelPrototype>, ProtoId<CargoAccountPrototype>> DepartmentAccess = new();
    }
}
