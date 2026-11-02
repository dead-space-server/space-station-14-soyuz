// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared._RMC14.Marines.Roles.Ranks;
using Content.Shared.DeadSpace._Soyuz.Ranks;
using Content.Shared.DeadSpace.UniformAccessories;
using Content.Shared.DeadSpace.UniformAccessories.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.Ranks;

public sealed class RankInsigniaSystem : SharedRankInsigniaSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedRankSystem _rank = default!;
    [Dependency] private readonly SharedUniformAccessorySystem _accessories = default!;

    private const string JumpsuitSlot = "jumpsuit";

    /// <summary>
    /// Лычки по умолчанию, если для звания нет собственного прототипа RankInsignia + ID звания
    /// </summary>
    private static readonly EntProtoId DefaultInsignia = "RankInsigniaDefault";

    private readonly List<EntityUid> _pending = new();
    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleRankInsigniaEvent>(OnToggle);
        SubscribeLocalEvent<UniformAccessoryHolderComponent, EntInsertedIntoContainerMessage>(OnAccessoryInserted);
        SubscribeLocalEvent<UniformAccessoryHolderComponent, EntRemovedFromContainerMessage>(OnAccessoryRemoved);
        SubscribeLocalEvent<RankInsigniaHolderComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<RankInsigniaHolderComponent, GotUnequippedEvent>(OnUnequipped);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + TimeSpan.FromSeconds(1);

        _pending.Clear();
        var query = EntityQueryEnumerator<RankComponent>();
        while (query.MoveNext(out var uid, out var rankComp))
        {
            if (rankComp.Rank != null && !HasComp<RankInsigniaWearerComponent>(uid))
                _pending.Add(uid);
        }

        foreach (var uid in _pending)
        {
            if (!TryComp<RankComponent>(uid, out var comp) || comp.Rank is not { } rank)
                continue;

            if (!_inventory.TryGetSlotEntity(uid, JumpsuitSlot, out var uniform)
                || !HasComp<UniformAccessoryHolderComponent>(uniform))
                continue;

            if (TryGetInsignia(uniform.Value, out var existing))
            {
                if (TryComp<RankInsigniaComponent>(existing, out var insignia) && insignia.Rank == null)
                {
                    insignia.Rank = rank;
                    Dirty(existing, insignia);
                    RefreshRank(uid);
                }

                continue;
            }

            TryAttachInsignia(uid, rank);
        }
    }

    /// <summary>
    /// Прикрепляет к комбинезону моба лычки заданного звания. Вызывайте при появлении роли со званием вместо прямой выдачи звания. Возвращает false, если на мобе нет подходящего комбинезона или на нём уже есть лычки
    /// </summary>
    public bool TryAttachInsignia(EntityUid mob, ProtoId<RankPrototype> rank)
    {
        if (!_inventory.TryGetSlotEntity(mob, JumpsuitSlot, out var uniform))
            return false;

        if (!TryComp<UniformAccessoryHolderComponent>(uniform, out var holder))
            return false;

        if (TryGetInsignia(uniform.Value, out _))
            return false;

        if (!Container.TryGetContainer(uniform.Value, UniformAccessoryHolderComponent.ContainerId, out var container))
            return false;

        var specific = $"RankInsignia{rank}";
        var spawnProto = _proto.HasIndex<EntityPrototype>(specific) ? specific : DefaultInsignia.Id;

        var insignia = Spawn(spawnProto, Transform(mob).Coordinates);
        var comp = EnsureComp<RankInsigniaComponent>(insignia);
        comp.Rank = rank;
        Dirty(insignia, comp);

        if (!Container.Insert(insignia, container))
        {
            Del(insignia);
            return false;
        }

        _accessories.UpdateExamineData((uniform.Value, holder));
        _item.VisualsChanged(uniform.Value);
        RefreshRank(mob);
        return true;
    }

    private void OnToggle(ToggleRankInsigniaEvent ev)
    {
        if (!TryComp<UniformAccessoryComponent>(ev.Insignia, out var accessory))
            return;

        accessory.Hidden = !accessory.Hidden;
        Dirty(ev.Insignia, accessory);

        if (TryComp<UniformAccessoryHolderComponent>(ev.Holder, out var holder))
            Dirty(ev.Holder, holder);

        _item.VisualsChanged(ev.Holder);

        if (GetWearer(ev.Holder) is { } wearer)
            RefreshRank(wearer);
    }

    private void OnAccessoryInserted(Entity<UniformAccessoryHolderComponent> holder, ref EntInsertedIntoContainerMessage args)
    {
        if (!IsInsignia(args.Container.ID, args.Entity))
            return;

        EnsureComp<RankInsigniaHolderComponent>(holder);

        if (GetWearer(holder) is { } wearer)
            RefreshRank(wearer);
    }

    private void OnAccessoryRemoved(Entity<UniformAccessoryHolderComponent> holder, ref EntRemovedFromContainerMessage args)
    {
        if (!IsInsignia(args.Container.ID, args.Entity))
            return;

        if (GetWearer(holder) is { } wearer)
            RefreshRank(wearer);

        if (!TryGetInsignia(holder, out _))
            RemComp<RankInsigniaHolderComponent>(holder);
    }

    private bool IsInsignia(string containerId, EntityUid accessory)
    {
        return containerId == UniformAccessoryHolderComponent.ContainerId && HasComp<RankInsigniaComponent>(accessory);
    }

    private void OnEquipped(Entity<RankInsigniaHolderComponent> holder, ref GotEquippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.INNERCLOTHING) != 0)
            RefreshRank(args.Equipee);
    }

    private void OnUnequipped(Entity<RankInsigniaHolderComponent> holder, ref GotUnequippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.INNERCLOTHING) != 0)
            RefreshRank(args.Equipee);
    }

    /// <summary>
    /// Кто носит этот комбинезон (если он надет в слот комбинезона)
    /// </summary>
    private EntityUid? GetWearer(EntityUid holder)
    {
        if (!Container.TryGetContainingContainer(holder, out var container))
            return null;

        return container.ID == JumpsuitSlot && HasComp<InventoryComponent>(container.Owner)
            ? container.Owner
            : null;
    }

    /// <summary>
    /// Выставляет мобу звание видимых лычек на его комбинезоне или убирает его
    /// </summary>
    private void RefreshRank(EntityUid mob)
    {
        ProtoId<RankPrototype>? rank = null;

        if (_inventory.TryGetSlotEntity(mob, JumpsuitSlot, out var uniform)
            && TryGetInsignia(uniform.Value, out var insignia)
            && TryComp<UniformAccessoryComponent>(insignia, out var accessory)
            && !accessory.Hidden
            && TryComp<RankInsigniaComponent>(insignia, out var insigniaComp))
        {
            rank = insigniaComp.Rank;
        }

        if (rank is { } value)
        {
            EnsureComp<RankInsigniaWearerComponent>(mob);
            _rank.SetRank(mob, value);
        }
        else if (HasComp<RankInsigniaWearerComponent>(mob))
        {
            _rank.ClearRank(mob);
        }
    }
}