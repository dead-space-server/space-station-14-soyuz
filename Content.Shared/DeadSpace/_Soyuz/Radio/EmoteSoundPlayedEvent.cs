// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Audio;

namespace Content.Shared.DeadSpace._Soyuz.Radio;

[ByRefEvent]
public readonly record struct EmoteSoundPlayedEvent(string EmoteId, ResolvedSoundSpecifier Sound, AudioParams SoundParams);
