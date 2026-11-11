// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.Credits;

public enum CreditTransactionType : byte
{
    AdminGive = 0,
    Referral = 1,
    DeveloperReward = 2,
    EventReward = 3,
    ModeratorReward = 4,
    Purchase = 5,
    PlaytimeReward = 6,
}