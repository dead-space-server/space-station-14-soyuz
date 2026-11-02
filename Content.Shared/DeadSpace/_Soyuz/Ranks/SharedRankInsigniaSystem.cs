// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared.DeadSpace.UniformAccessories.Components;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Shared.DeadSpace._Soyuz.Ranks;

/// <summary>
/// Кнопка свернуть и развернуть лычки
/// </summary>
public abstract class SharedRankInsigniaSystem : EntitySystem
{
    [Dependency] protected readonly SharedContainerSystem Container = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RankInsigniaHolderComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    /// <summary>
    /// Ищет лычки среди аксессуаров комбинезона
    /// </summary>
    public bool TryGetInsignia(EntityUid holder, out EntityUid insignia)
    {
        insignia = default;

        if (!Container.TryGetContainer(holder, UniformAccessoryHolderComponent.ContainerId, out var container))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (!HasComp<RankInsigniaComponent>(contained))
                continue;

            insignia = contained;
            return true;
        }

        return false;
    }

    private void OnGetVerbs(Entity<RankInsigniaHolderComponent> holder, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryGetInsignia(holder, out var insignia))
            return;

        var hidden = TryComp<UniformAccessoryComponent>(insignia, out var accessory) && accessory.Hidden;
        var user = args.User;
        var holderUid = holder.Owner;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(hidden ? "rank-insignia-show" : "rank-insignia-hide"),
            Priority = 2,
            Act = () => RaiseLocalEvent(new ToggleRankInsigniaEvent(holderUid, insignia, user)),
        });
    }
}