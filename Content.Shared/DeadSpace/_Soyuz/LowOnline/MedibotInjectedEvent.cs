// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

namespace Content.Shared.DeadSpace._Soyuz.LowOnline;

/// <summary>
/// Медибот только что ввёл пациенту стандартное лекарство. Поднимается на медиботе из MedibotSystem.TryInject
/// </summary>
[ByRefEvent]
public readonly record struct MedibotInjectedEvent(EntityUid Medibot, EntityUid Target);