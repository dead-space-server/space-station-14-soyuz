// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Поднимается в RadioSystem на говорящем перед форматированием сообщения в рацию
/// </summary>
[ByRefEvent]
public record struct RadioSpeakerNameEvent(string Name)
{
    /// <summary>Имя заменено радиопозывным, сообщение нужно оформить без должности и глагола</summary>
    public bool IsCallsign;
}
