// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace.Prison;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.EUI;
using Content.Server.Preferences.Managers;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Ghost;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Content.Shared.Damage.Components;

namespace Content.Server.DeadSpace._Soyuz.GhostBar;

public sealed class GhostBarSystem : EntitySystem
{
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly PrisonSystem _prison = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    private EntityUid? _ghostBarMap;
    public bool GhostBarEnabled { get; private set; } = true;
    private const string GhostBarMapPath = "/Maps/_Soyuz/Nonstations/ghostbar.yml";

    private readonly Dictionary<ICommonSession, GhostBarCostumeEui> _activeEuis = new();
    private readonly Dictionary<NetUserId, HashSet<string>> _selectedCostumes = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<JoinGhostBarEvent>(OnJoin);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
    }

    internal bool CanJoinOnBar(ICommonSession session)
    {
        if (!GhostBarEnabled)
            return false;

        if (_prison.IsUserPrisoner(session.UserId))
            return false;

        return true;
    }

    public void ToggleEnabled()
    {
        GhostBarEnabled = !GhostBarEnabled;
    }

    private void OnJoin(JoinGhostBarEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not ICommonSession player)
            return;

        if (_prison.IsUserPrisoner(player.UserId))
        {
            _chat.DispatchServerMessage(player, Loc.GetString("prison-arena-blocked"));
            return;
        }

        if (!GhostBarEnabled)
            return;

        if (player.AttachedEntity is not { Valid: true } ghost || !HasComp<GhostComponent>(ghost))
            return;

        if (_activeEuis.ContainsKey(player))
            return;

        var eui = new GhostBarCostumeEui(this, ghost);
        _euiManager.OpenEui(eui, player);
        _activeEuis[player] = eui;
    }

    public void OnGhostBarEuiClosed(ICommonSession session, GhostBarCostumeEui eui)
    {
        if (_activeEuis.TryGetValue(session, out var current) && ReferenceEquals(current, eui))
            _activeEuis.Remove(session);
    }

    /// <summary>
    /// Загружаем карту бара
    /// </summary>
    private EntityUid? EnsureGhostBarMap()
    {
        if (_ghostBarMap is { } existing && existing.Valid && !Deleted(existing))
            return existing;

        _ghostBarMap = null;

        var opts = DeserializationOptions.Default with { InitializeMaps = true };

        if (!_mapLoader.TryLoadMap(new ResPath(GhostBarMapPath), out var map, out _, opts))
        {
            Log.Error($"Failed to load Ghost-bar map from: {GhostBarMapPath}");
            return null;
        }

        if (map is not { } mapEntity)
        {
            Log.Error($"Ghost-bar map loaded but result is null: {GhostBarMapPath}");
            return null;
        }

        var mapUid = mapEntity.Owner;
        if (!mapUid.Valid)
        {
            Log.Error($"Ghost-bar map loaded but mapUid invalid: {GhostBarMapPath}");
            return null;
        }

        _ghostBarMap = mapUid;
        Log.Info($"Loaded Ghost-bar map from: {GhostBarMapPath}");
        return _ghostBarMap;
    }

    /// <summary>
    /// Спавн персонажа в баре
    /// </summary>
    public void SpawnPlayerInBar(ICommonSession player, EntityUid ghost)
    {
        var mapUidOpt = EnsureGhostBarMap();
        if (mapUidOpt == null)
        {
            _chat.DispatchServerMessage(player, Loc.GetString("ghost-bar-join-error"));
            return;
        }

        var mapUid = mapUidOpt.Value;
        var spawnCoords = GetSpawnCoordinates(mapUid);

        if (spawnCoords == null)
        {
            _chat.DispatchServerMessage(player, Loc.GetString("ghost-bar-join-error"));
            return;
        }

        var profile = _prefs.GetPreferences(player.UserId).SelectedCharacter as HumanoidCharacterProfile;
        var speciesId = profile?.Species ?? SharedHumanoidAppearanceSystem.DefaultSpecies;

        if (!_prototype.TryIndex<SpeciesPrototype>(speciesId, out var species))
        {
            Log.Error($"Unknown species prototype '{speciesId}' for ghost-bar.");
            _chat.DispatchServerMessage(player, Loc.GetString("ghost-bar-join-error"));
            return;
        }

        var mob = Spawn(species.Prototype, spawnCoords.Value);
        EnsureComp<GhostBarPlayerComponent>(mob);
        EnsureComp<GodmodeComponent>(mob);
        EnsureComp<PacifiedComponent>(mob);

        if (profile != null)
            _humanoid.LoadProfile(mob, profile);

        _meta.SetEntityName(mob, profile?.Name ?? player.Name);

        EquipCostumes(mob, player.UserId);

        if (_mind.TryGetMind(player.UserId, out var mindId, out var mind))
        {
            _mind.TransferTo(mindId.Value, mob, mind: mind);
        }
        else
        {
            _mind.MakeSentient(mob);
            var newMind = _mind.CreateMind(player.UserId, player.Name);
            _mind.TransferTo(newMind, mob);
        }

        if (ghost.Valid)
            QueueDel(ghost);

        _chat.DispatchServerMessage(player, Loc.GetString("ghost-bar-joined"));
    }

    /// <summary>
    /// Удаляет персонажа при выходе из бара
    /// </summary>
    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        if (_activeEuis.TryGetValue(ev.Player, out var eui) && eui.SourceGhost == ev.Entity && !eui.IsShutDown)
            eui.Close();

        if (!HasComp<GhostBarPlayerComponent>(ev.Entity))
            return;

        if (_mind.TryGetMind(ev.Entity, out _, out var mind) && mind.VisitingEntity != null)
            return;

        QueueDel(ev.Entity);
    }

    /// <summary>
    /// Возвращает координаты спавн поинта. Если нет - центр карты
    /// </summary>
    private MapCoordinates? GetSpawnCoordinates(EntityUid mapUid)
    {
        var query = EntityQueryEnumerator<GhostBarSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (Deleted(uid) || TerminatingOrDeleted(uid))
                continue;

            return _transform.GetMapCoordinates(uid, xform);
        }

        if (!TryComp<MapComponent>(mapUid, out var mapComp))
            return null;

        Log.Warning($"No ghost-bar spawn points found on map {mapUid}, falling back to center.");
        return new MapCoordinates(Vector2.Zero, mapComp.MapId);
    }

    /// <summary>
    /// Возвращает список всех костюмов из прототипов.
    /// </summary>
    public List<GhostBarCostumePrototype> GetAllCostumes()
    {
        var list = new List<GhostBarCostumePrototype>();
        foreach (var costume in _prototype.EnumeratePrototypes<GhostBarCostumePrototype>())
            list.Add(costume);

        return list;
    }

    /// <summary>
    /// Возвращает выбранные костюмы игрока.
    /// </summary>
    public HashSet<string> GetSelectedCostumes(NetUserId userId)
    {
        if (!_selectedCostumes.TryGetValue(userId, out var selected))
        {
            selected = new HashSet<string>();
            _selectedCostumes[userId] = selected;
        }

        return selected;
    }

    /// <summary>
    /// Переключает выбор костюма. Если уже выбран - снимает выбор
    /// </summary>
    public void ToggleCostume(ICommonSession player, string costumeId)
    {
        if (!_prototype.TryIndex<GhostBarCostumePrototype>(costumeId, out var costume))
            return;

        var selected = GetSelectedCostumes(player.UserId);
        if (selected.Remove(costumeId))
        {
            if (_activeEuis.TryGetValue(player, out var euiOff) && !euiOff.IsShutDown)
                euiOff.StateDirty();
            return;
        }

        var sameCategory = new List<string>();
        foreach (var id in selected)
        {
            if (_prototype.TryIndex<GhostBarCostumePrototype>(id, out var other) &&
                other.Category == costume.Category)
            {
                sameCategory.Add(id);
            }
        }

        foreach (var id in sameCategory)
            selected.Remove(id);

        selected.Add(costumeId);

        if (_activeEuis.TryGetValue(player, out var eui) && !eui.IsShutDown)
            eui.StateDirty();
    }

    /// <summary>
    /// Надевает выбранные костюмы. Если ничего не выбрал - ставит дефолтный
    /// </summary>
    private void EquipCostumes(EntityUid body, NetUserId userId)
    {
        var selected = GetSelectedCostumes(userId);
        var selectedCategories = new HashSet<string>();
        foreach (var id in selected)
        {
            if (_prototype.TryIndex<GhostBarCostumePrototype>(id, out var c))
                selectedCategories.Add(c.Category);
        }

        var toEquip = new HashSet<string>(selected);
        foreach (var costume in _prototype.EnumeratePrototypes<GhostBarCostumePrototype>())
        {
            if (!costume.Default)
                continue;

            if (selectedCategories.Contains(costume.Category))
                continue;

            toEquip.Add(costume.ID);
        }

        foreach (var costumeId in toEquip)
        {
            if (!_prototype.TryIndex<GhostBarCostumePrototype>(costumeId, out var costume))
                continue;

            if (string.IsNullOrEmpty(costume.ClothingProto.Id))
            {
                continue;
            }

            if (!_prototype.HasIndex<EntityPrototype>(costume.ClothingProto.Id))
                continue;

            var item = Spawn(costume.ClothingProto.Id, Transform(body).Coordinates);

            if (!_inventory.TryEquip(body, item, costume.Slot, silent: true, force: true))
            {
                QueueDel(item);
                continue;
            }
        }
    }

    public GhostBarEuiState GetEuiState(ICommonSession player)
    {
        var costumes = GetAllCostumes()
            .Select(c => new GhostBarCostumeOption
            {
                Id = c.ID,
                Name = c.Name,
                Category = c.Category,
                ClothingProto = c.ClothingProto.Id,
                Slot = c.Slot,
                Default = c.Default,
            })
            .ToList();

        var selected = GetSelectedCostumes(player.UserId);

        return new GhostBarEuiState
        {
            Title = Loc.GetString("ghost-bar-window-title"),
            Description = Loc.GetString("ghost-bar-window-description"),
            Button = Loc.GetString("ghost-bar-window-confirm-button"),
            Costumes = costumes,
            Selected = new HashSet<string>(selected),
        };
    }
}