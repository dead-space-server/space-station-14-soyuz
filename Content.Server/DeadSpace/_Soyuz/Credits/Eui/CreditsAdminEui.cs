// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Content.Shared.Eui;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server.DeadSpace._Soyuz.Credits.Eui;

public sealed class CreditsAdminEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly ILogManager _log = default!;

    private const int PageSize = 12;

    private CreditsAdminState _state = new();
    private readonly Dictionary<Guid, string> _names = new();

    public CreditsAdminEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        if (!_admin.HasAdminFlag(Player, AdminFlags.Server))
            Close();
    }

    public override EuiStateBase GetNewState() => _state;

    public override async void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, AdminFlags.Server))
        {
            Close();
            return;
        }

        try
        {
            switch (msg)
            {
                case CreditsAdminLookupMessage lookup:
                    await LookupAsync(lookup.Target, lookup.Page, lookup.TypeFilter, lookup.ActorFilter, lookup.ReasonFilter, "");
                    break;

                case CreditsAdminGiveMessage give:
                    var located = await _locator.LookupIdByNameOrIdAsync(give.Target);
                    if (located == null)
                    {
                        await LookupAsync(give.Target, 0, _state.TypeFilter, _state.ActorFilter, _state.ReasonFilter,
                            Loc.GetString("credits-admin-notice-notfound"));
                        break;
                    }

                    var result = await _credits.GiveAsync(located.UserId.UserId, give.Amount, give.Reason, Player, give.Type);
                    await LookupAsync(give.Target, 0, _state.TypeFilter, _state.ActorFilter, _state.ReasonFilter,
                        Loc.GetString($"credits-admin-notice-{result.ToString().ToLowerInvariant()}"));
                    break;
            }
        }
        catch (Exception e)
        {
            _log.GetSawmill("credits").Error($"CreditsAdminEui: {e}");
        }
    }

    private async Task LookupAsync(string target, int page, int typeFilter, string actorFilter, string reasonFilter, string notice)
    {
        page = Math.Max(0, page);
        var state = new CreditsAdminState
        {
            Target = target,
            Page = page,
            TypeFilter = typeFilter,
            ActorFilter = actorFilter,
            ReasonFilter = reasonFilter,
            Notice = notice,
        };

        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            var located = await _locator.LookupIdByNameOrIdAsync(target.Trim());
            if (located == null)
            {
                state.Notice = Loc.GetString("credits-admin-notice-notfound");
                Publish(state);
                return;
            }

            userId = located.UserId.UserId;
            state.Resolved = true;
            state.Balance = await _credits.GetBalanceAsync(userId.Value);
        }

        Guid? actor = null;
        if (!string.IsNullOrWhiteSpace(actorFilter))
        {
            var located = await _locator.LookupIdByNameOrIdAsync(actorFilter.Trim());
            if (located == null)
            {
                state.Notice = Loc.GetString("credits-admin-notice-actor-notfound");
                Publish(state);
                return;
            }

            actor = located.UserId.UserId;
        }

        CreditTransactionType? type = typeFilter >= 0 ? (CreditTransactionType) typeFilter : null;
        var (entries, hasMore) = await _credits.GetLogAsync(userId, page, PageSize,
            new CreditLogFilter(type, actor, reasonFilter));

        state.Global = userId == null;
        state.HasMore = hasMore;
        foreach (var e in entries)
        {
            state.History.Add(new CreditLogEntryDto(e.Amount, e.Type, e.Reason, e.CreatedAtUtc)
            {
                Target = await NameOfAsync(e.UserId),
                Actor = e.Actor == null ? "—" : await NameOfAsync(e.Actor.Value),
            });
        }

        Publish(state);
    }

    private void Publish(CreditsAdminState state)
    {
        _state = state;
        StateDirty();
    }

    private async Task<string> NameOfAsync(Guid id)
    {
        if (_names.TryGetValue(id, out var cached))
            return cached;

        var name = id.ToString();
        try
        {
            var data = await _locator.LookupIdAsync(new NetUserId(id));
            if (data != null)
                name = data.Username;
        }
        catch
        {

        }

        _names[id] = name;
        return name;
    }
}