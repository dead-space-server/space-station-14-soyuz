using System.Linq;
using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Server.Materials;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace.MaterialMarketplace;
using Content.Shared.Materials;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.MaterialMarketplace
{
    /// <summary>
    /// Серверная система маркетплейса материалов.
    /// Отвечает за подсчёт материалов, их спавн и обработку покупки.
    /// </summary>
    public sealed class MaterialMarketplaceSystem : EntitySystem
    {
        [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
        [Dependency] private readonly CargoSystem _cargo = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;
        [Dependency] private readonly IPrototypeManager _prototype = default!;
        [Dependency] private readonly UserInterfaceSystem _ui = default!;
        private List<MaterialPrototype> _cachedMaterials = new();

        public override void Initialize()
        {
            base.Initialize();
            _cachedMaterials = _prototype.EnumeratePrototypes<MaterialPrototype>().ToList();
            SubscribeLocalEvent<MaterialMarketplaceComponent, BoundUIOpenedEvent>(OnUIOpened);
            SubscribeLocalEvent<MaterialMarketplaceComponent, MaterialMarketplaceBuyMessage>(OnBuyRequest);
            SubscribeLocalEvent<MaterialStorageComponent, MaterialAmountChangedEvent>(OnMaterialChanged);
        }

        private void OnUIOpened(EntityUid uid, MaterialMarketplaceComponent comp, BoundUIOpenedEvent args)
        {
            UpdateUI(uid, comp);
        }

        private void OnMaterialChanged(EntityUid uid, MaterialStorageComponent comp, MaterialAmountChangedEvent args)
        {
            if (TryComp<MaterialMarketplaceComponent>(uid, out var marketplace))
                UpdateUI(uid, marketplace);
        }

        private IEnumerable<MaterialPrototype> GetFilteredMaterials(MaterialMarketplaceComponent comp)
        {
            return _cachedMaterials.Where(mat =>
                (comp.WhitelistMaterials.Count == 0 || comp.WhitelistMaterials.Contains(mat.ID)) &&
                !comp.BlacklistMaterials.Contains(mat.ID) &&
                IsAllowedByTags(mat, comp.WhitelistTags, comp.BlacklistTags));
        }

        private void UpdateUI(EntityUid uid, MaterialMarketplaceComponent comp)
        {
            if (!TryComp<MaterialStorageComponent>(uid, out _))
                return;

            var available = new Dictionary<string, int>();
            var prices = new Dictionary<string, double>();

            foreach (var mat in GetFilteredMaterials(comp))
            {
                var units = _materialStorage.GetMaterialAmount(uid, mat.ID);
                var perEntity = GetUnitsPerEntity(mat);
                if (perEntity <= 0)
                    perEntity = 1;
                var entitiesCount = units / perEntity;

                available[mat.ID] = entitiesCount;
                prices[mat.ID] = mat.Price * perEntity;
            }

            _ui.SetUiState(uid, MaterialMarketplaceUiKey.Key, new MaterialMarketplaceState(available, prices));
        }

        private bool IsAllowedByTags(MaterialPrototype mat, HashSet<string> whitelistTags, HashSet<string> blacklistTags)
        {
            if (mat.StackEntity == null)
                return true;

            if (!_prototype.TryIndex<EntityPrototype>(mat.StackEntity, out var stackProto))
                return true;

            if (whitelistTags.Count > 0)
            {
                if (!whitelistTags.Any(tag =>
                        stackProto.ID.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
                        stackProto.Name.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            if (blacklistTags.Count > 0)
            {
                if (blacklistTags.Any(tag =>
                        stackProto.ID.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
                        stackProto.Name.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        }

        private int GetUnitsPerEntity(MaterialPrototype material)
        {
            if (!string.IsNullOrEmpty(material.StackEntity) &&
                _prototype.TryIndex<EntityPrototype>(material.StackEntity, out var stackProto) &&
                stackProto.TryGetComponent<PhysicalCompositionComponent>(out var compo) &&
                compo.MaterialComposition.TryGetValue(material.ID, out var units))
            {
                if (units <= 0)
                    return 1;
                return units;
            }

            return 1;
        }

        private void OnBuyRequest(EntityUid uid, MaterialMarketplaceComponent comp, MaterialMarketplaceBuyMessage args)
        {
            if (args.Amount <= 0)
                return;

            if (!_prototype.TryIndex<MaterialPrototype>(args.MaterialId, out var material))
                return;

            if (comp.WhitelistMaterials.Count > 0 && !comp.WhitelistMaterials.Contains(args.MaterialId))
                return;

            if (comp.BlacklistMaterials.Contains(args.MaterialId))
                return;

            var unitsAvailable = _materialStorage.GetMaterialAmount(uid, args.MaterialId);
            var perEntity = GetUnitsPerEntity(material);
            if (perEntity <= 0)
                return;

            var maxEntities = unitsAvailable / perEntity;
            if (args.Amount > maxEntities)
                return;

            var pricePerEntity = material.Price * perEntity;
            var totalPrice = (int)Math.Round(pricePerEntity * args.Amount);

            var station = _stationSystem.GetOwningStation(uid);
            if (station != null)
                _cargo.UpdateBankAccount(station.Value, totalPrice, "Cargo");

            var totalUnits = args.Amount * perEntity;
            if (_materialStorage.TryChangeMaterialAmount(uid, args.MaterialId, -totalUnits))
            {
                var spawnCoords = Transform(uid).Coordinates;
                _materialStorage.SpawnMultipleFromMaterial(totalUnits, args.MaterialId, spawnCoords);
            }

            UpdateUI(uid, comp);
        }
    }
}
