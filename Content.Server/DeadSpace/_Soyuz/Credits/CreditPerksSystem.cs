// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>
/// Выдаёт купленные предметы в руку при появлении игрока в раунде и следит за кэшем покупок онлайн-игроков
/// </summary>
public sealed class CreditPerksSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly ICreditPerkCache _cache = default!;
    [Dependency] private readonly ICreditRateLimiter _limiter = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnStatusChanged;
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);

        foreach (var product in _proto.EnumeratePrototypes<CreditProductPrototype>())
        {
            if (product.CosmeticEntity is { } entId && !_proto.HasIndex<EntityPrototype>(entId))
                Log.Error($"Товар Советских кредитов '{product.ID}': прототип предмета '{entId}' не найден.");
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnStatusChanged;
    }

    private async void OnStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        var user = e.Session.UserId.UserId;

        try
        {
            switch (e.NewStatus)
            {
                case SessionStatus.Connected:
                    await _cache.RefreshAsync(user);
                    break;
                case SessionStatus.Disconnected:
                    _cache.Remove(user);
                    _limiter.Forget(user);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось обновить покупки Советских кредитов: {ex}");
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!HasComp<HandsComponent>(ev.Mob))
            return;

        foreach (var productId in _cache.GetActiveEquippedProducts(ev.Player.UserId.UserId))
        {
            if (!_proto.TryIndex<CreditProductPrototype>(productId, out var product))
                continue;

            if (product.CosmeticEntity is not { } itemId)
                continue;

            if (product.Jobs.Count > 0 && (ev.JobId == null || !product.Jobs.Contains(ev.JobId)))
                continue;

            if (!_proto.HasIndex<EntityPrototype>(itemId))
            {
                Log.Error($"Товар Советских кредитов '{productId}': прототип предмета '{itemId}' не найден.");
                continue;
            }

            try
            {
                var item = Spawn(itemId, Transform(ev.Mob).Coordinates);
                _hands.TryPickup(ev.Mob, item, checkActionBlocker: false);
            }
            catch (Exception e)
            {
                Log.Error($"Не удалось выдать предмет товара '{productId}': {e}");
            }
        }
    }
}