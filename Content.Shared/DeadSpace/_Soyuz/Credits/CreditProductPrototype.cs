using System;
using System.Collections.Generic;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.DeadSpace._Soyuz.Credits;

public enum CreditProductType : byte
{
    OocColor,
    Badge,
    Hat,
    DiscordTitle,
}

[Prototype]
public sealed partial class CreditProductPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string? Description;

    [DataField(required: true)]
    public CreditProductType ProductType;

    [DataField(required: true)]
    public int Price;

    /// <summary>Категория магазина (creditCategory). Не задано = «без категории».</summary>
    [DataField]
    public ProtoId<CreditCategoryPrototype>? Category;

    /// <summary>Срок действия в днях. Не задан = навсегда. Повторная покупка действующего товара продлевает срок.</summary>
    [DataField]
    public int? DurationDays;

    /// <summary>
    /// Предмет, который выдаётся купившему в руку при появлении в раунде.
    /// </summary>
    [DataField]
    public EntProtoId? CosmeticEntity;

    /// <summary>Игровые профессии, для которых товар работает (предмет выдаётся только им). Пусто = для всех.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>
    /// Постоянный товар: после покупки всегда надет и не снимается игроком.
    /// Пока он есть, другие товары его группы (и другой цвет OOC) надеть нельзя.
    /// </summary>
    [DataField]
    public bool Permanent;

    /// <summary>Товары с одинаковой группой нельзя надеть одновременно (например, "ooc-color").</summary>
    [DataField]
    public string? ExclusiveGroup;

    /// <summary>Цвет (hex, например "#ff0000") для товаров типа OocColor.</summary>
    [DataField]
    public string? Color;

    // ---------- Скидки, доступность, тираж, требования ----------
    // Даты задаются строкой в UTC: "2026-12-01" или "2026-12-01 18:00".

    /// <summary>Скидка в процентах (1-90). Действует всегда, если не заданы SaleFrom/SaleUntil.</summary>
    [DataField]
    public int? DiscountPercent;

    /// <summary>Начало скидки (UTC). Не задано = уже действует.</summary>
    [DataField]
    public string? SaleFrom;

    /// <summary>Конец скидки (UTC). Не задано = без конца.</summary>
    [DataField]
    public string? SaleUntil;

    /// <summary>Товар доступен с этой даты (UTC). До неё в магазине его нет (сезонные товары).</summary>
    [DataField]
    public string? AvailableFrom;

    /// <summary>Товар доступен до этой даты (UTC). После неё его нельзя купить, а купленный продолжает действовать.</summary>
    [DataField]
    public string? AvailableUntil;

    /// <summary>
    /// Тираж на весь сервер: сколько раз товар можно купить всего (возвращённые покупки не считаются).
    /// Продление тоже считается покупкой. Не задано = без ограничения.
    /// </summary>
    [DataField]
    public int? Stock;

    /// <summary>Сколько часов нужно наиграть на этом сервере, чтобы купить товар.</summary>
    [DataField]
    public int? MinPlaytimeHours;

    /// <summary>Покупка доступна только игрокам с привязанным Discord (команда credits_discord).</summary>
    [DataField]
    public bool RequiresDiscord;
}
