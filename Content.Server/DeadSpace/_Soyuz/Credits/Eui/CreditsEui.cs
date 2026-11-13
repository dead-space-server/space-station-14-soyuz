// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.EUI;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Content.Shared.Eui;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits.Eui;

public sealed class CreditsEui : BaseEui
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IReferralService _referrals = default!;
    [Dependency] private readonly IShopService _shop = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILogManager _log = default!;

    private const int PageSize = 10;

    private int _page;
    private string _notice = "";
    private CreditsEuiState _state = new();

    public CreditsEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override async void Opened()
    {
        base.Opened();

        try
        {
            await RefreshAsync();
        }
        catch (Exception e)
        {
            _log.GetSawmill("credits").Error($"CreditsEui.Opened: {e}");
        }
    }

    public override EuiStateBase GetNewState() => _state;

    public override async void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        try
        {
            var user = Player.UserId.UserId;
            _notice = "";

            switch (msg)
            {
                case CreditsPageMessage p:
                    _page = Math.Max(0, p.Page);
                    break;

                case CreditsBuyMessage b:
                    var bought = await _shop.PurchaseAsync(user, b.ProductId);
                    _notice = Loc.GetString($"credits-notice-buy-{bought.ToString().ToLowerInvariant()}");
                    break;

                case CreditsEquipMessage e:
                    var equip = await _shop.SetEquippedAsync(user, e.ProductId, e.Equip);
                    _notice = Loc.GetString($"credits-notice-equip-{equip.ToString().ToLowerInvariant()}");
                    break;

                case CreditsReferralApplyMessage r:
                    var applied = await _referrals.ApplyAsync(user, r.Code);
                    _notice = Loc.GetString($"credits-notice-ref-{applied.ToString().ToLowerInvariant()}");
                    break;
            }

            await RefreshAsync();
        }
        catch (Exception e)
        {
            _log.GetSawmill("credits").Error($"CreditsEui: {e}");
        }
    }

    private async Task RefreshAsync()
    {
        var user = Player.UserId.UserId;

        var balance = await _repo.GetBalanceAsync(user);
        var seconds = await _repo.GetPlaytimeSecondsAsync(user);
        var owned = await _repo.GetOwnedAsync(user);
        var (log, hasMore) = await _repo.GetLogAsync(user, _page, PageSize);
        var code = await _referrals.GetCodeAsync(user);
        var (applied, rewarded) = await _repo.GetReferralStatusAsync(user);
        var stats = await _repo.GetReferralStatsAsync(user);

        var state = new CreditsEuiState
        {
            Balance = balance,
            PlaytimeHours = (int) (seconds / 3600),
            Page = _page,
            HasMore = hasMore,
            ReferralCode = code,
            ReferralApplied = applied,
            ReferralRewarded = rewarded,
            ReferralMinHours = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralMinHours),
            InviterReward = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralInviterReward),
            InviteeReward = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralInviteeReward),
            RefInvited = stats.Invited,
            RefRewarded = stats.Rewarded,
            RefPending = stats.Pending,
            RefEarned = stats.EarnedAsInviter,
            Notice = _notice,
        };

        foreach (var l in log)
            state.History.Add(new CreditLogEntryDto(l.Amount, l.Type, l.Reason, l.CreatedAtUtc));

        foreach (var p in _proto.EnumeratePrototypes<CreditProductPrototype>().OrderBy(p => p.Price))
        {
            var isOwned = owned.TryGetValue(p.ID, out var ownedInfo);
            var dto = new CreditProductDto
            {
                Id = p.ID,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Owned = isOwned,
                Equipped = isOwned && (ownedInfo!.Equipped || p.Permanent),
                ExpiresAtUtc = isOwned ? ownedInfo!.ExpiresAt : null,
                DurationDays = p.DurationDays,
                GivesItem = p.CosmeticEntity != null,
                Permanent = p.Permanent,
            };

            foreach (var job in p.Jobs)
                dto.Jobs.Add(job.Id);

            state.Products.Add(dto);
        }

        _state = state;
        StateDirty();
    }
}