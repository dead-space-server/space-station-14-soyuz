// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Actions.Components;

namespace Content.Shared.DeadSpace.Actions;

[ByRefEvent]
public record struct ActionAvailabilityEvent(Entity<ActionComponent> Action, bool Available = true);
