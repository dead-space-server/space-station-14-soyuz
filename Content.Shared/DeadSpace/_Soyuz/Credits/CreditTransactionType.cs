// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.Credits;

// Значения хранятся в БД как число: порядок менять нельзя, новые типы добавляются только в конец.
public enum CreditTransactionType : byte
{
    AdminGive = 0,
    Referral = 1,
    DeveloperReward = 2,
    EventReward = 3,
    ModeratorReward = 4,
    Purchase = 5,
    PlaytimeReward = 6,
    RolePlayReward = 7,
    OtherReward = 8,
    /// <summary>Списание администратором. Всегда отрицательная запись.</summary>
    AdminTake = 9,
    /// <summary>Возврат за покупку. Обратная запись к покупке, сама покупка в журнале не меняется.</summary>
    Refund = 10,
    /// <summary>Награда или выдача товара по промокоду.</summary>
    PromoCode = 11,
}
