// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
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
    public LocId Name;

    [DataField]
    public LocId? Description;

    [DataField(required: true)]
    public CreditProductType ProductType;

    [DataField(required: true)]
    public int Price;

    /// <summary>
    /// Срок действия в днях. Не задан = навсегда. Повторная покупка действующего товара продлевает срок
    /// </summary>
    [DataField]
    public int? DurationDays;

    /// <summary>
    /// Предмет, который выдаётся купившему в руку при появлении в раунде
    /// </summary>
    [DataField]
    public EntProtoId? CosmeticEntity;

    /// <summary>
    /// Игровые профессии, для которых товар работает (предмет выдаётся только им). Пусто = для всех
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>
    /// Постоянный товар: после покупки всегда надет и не снимается игроком
    /// Пока он есть, другие товары его группы (и другой цвет OOC) надеть нельзя
    /// </summary>
    [DataField]
    public bool Permanent;

    /// <summary>
    /// Товары с одинаковой группой нельзя надеть одновременно (например, "ooc-color")
    /// </summary>
    [DataField]
    public string? ExclusiveGroup;

    /// <summary>
    /// Цвет (hex, например "#ff0000") для товаров типа OocColor
    /// </summary>
    [DataField]
    public string? Color;
}