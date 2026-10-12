using System.Globalization;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Audio;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;   // EntityUid, NetEntity

namespace Content.Server.DeadSpace.Sectants;

[AdminCommand(AdminFlags.Admin)]
public sealed class SectantAnnounceCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;


    // # Простой анонс со стандартными параметрами
    // sectant_announce

    // # Свой текст
    // sectant_announce ОНО ИДЁТ ЗА ТОБОЙ

    // # С усиленной тряской и красным цветом
    // sectant_announce "ОНО БЛИЗКО" --shake 12 --color "#ff0000"

    // # Только для одного игрока (uid через F3 → Entity → Copy NetEntity)
    // sectant_announce "ТОЛЬКО ТЫ" --target 1234

    // # Тихий анонс без шёпота
    // sectant_announce "..." --no-whisper --volume -20

    // # Длинный текст
    // sectant_announce "ТЫ УМРЁШЬ ЗДЕСЬ" --text 8 --vignette 10
    public string Command => "sectant_announce";
    public string Description => "Запускает анонс Альпа для всех игроков.";
    public string Help =>
        "sectant_announce [текст] [флаги]\n" +
        "  --flash <сек>        длительность вспышки (по умолчанию 1.2)\n" +
        "  --vignette <сек>     длительность винетки (по умолчанию 5)\n" +
        "  --text <сек>         длительность текста (по умолчанию 4)\n" +
        "  --shake <сила>       сила тряски камеры (по умолчанию 5)\n" +
        "  --color <#RRGGBB>    цвет эффектов (по умолчанию #8b0000)\n" +
        "  --sound <путь>       путь к звуку (.ogg)\n" +
        "  --volume <дБ>        громкость звука (по умолчанию -8)\n" +
        "  --no-whisper         отключить шёпот\n" +
        "  --target <uid>       показать анонс только одному игроку\n" +
        "Пример: sectant_announce \"ОНО ИДЁТ\" --shake 12 --color #ff0000";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var flash = 1.2f;
        var vignette = 5f;
        var textDur = 4f;
        var shake = 5f;
        var colorHex = "#8b0000";
        var soundPath = "/Audio/_DeadSpace/TEMP_FOR_EVENT/little_gabry/laughAl.ogg";
        var volume = -8f;
        var whisper = true;
        EntityUid? target = null;

        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--flash":
                    if (!TryNextFloat(args, ref i, out flash, shell)) return;
                    break;
                case "--vignette":
                    if (!TryNextFloat(args, ref i, out vignette, shell)) return;
                    break;
                case "--text":
                    if (!TryNextFloat(args, ref i, out textDur, shell)) return;
                    break;
                case "--shake":
                    if (!TryNextFloat(args, ref i, out shake, shell)) return;
                    break;
                case "--volume":
                    if (!TryNextFloat(args, ref i, out volume, shell)) return;
                    break;
                case "--color":
                    if (!TryNextString(args, ref i, out colorHex, shell)) return;
                    break;
                case "--sound":
                    if (!TryNextString(args, ref i, out soundPath, shell)) return;
                    break;
                case "--no-whisper":
                    whisper = false;
                    break;
                case "--target":
                    if (!TryNextString(args, ref i, out var uidStr, shell)) return;
                    if (!NetEntity.TryParse(uidStr, out var net))
                    {
                        shell.WriteError($"Неверный uid: {uidStr}");
                        return;
                    }
                    var uid = _entities.GetEntity(net);
                    if (!_entities.EntityExists(uid))
                    {
                        shell.WriteError($"Сущность не найдена: {uidStr}");
                        return;
                    }
                    target = uid;
                    break;
                default:
                    if (arg.StartsWith("--"))
                    {
                        shell.WriteError($"Неизвестный флаг: {arg}");
                        shell.WriteLine(Help);
                        return;
                    }
                    positional.Add(arg);
                    break;
            }
        }

        var text = positional.Count > 0
            ? string.Join(' ', positional)
            : Loc.GetString("sectant-announce-here");

        Color color;
        try
        {
            color = Color.FromHex(colorHex);
        }
        catch
        {
            shell.WriteError($"Неверный цвет: {colorHex}. Формат: #RRGGBB");
            return;
        }

        var announce = _sysMan.GetEntitySystem<SectantAnnouncementSystem>();
        var sound = new SoundPathSpecifier(soundPath, AudioParams.Default.WithVolume(volume));

        if (target is { } singleTarget)
        {
            announce.AnnounceTo(singleTarget, text, sound, color,
                flash, vignette, textDur, shake, whisper);
            shell.WriteLine($"Анонс отправлен игроку {singleTarget}.");
        }
        else
        {
            announce.Announce(text, sound, color,
                flash, vignette, textDur, shake, whisper);
            shell.WriteLine("Анонс отправлен всем игрокам.");
        }
    }

    private static bool TryNextFloat(string[] args, ref int i, out float result, IConsoleShell shell)
    {
        result = 0f;
        if (i + 1 >= args.Length)
        {
            shell.WriteError($"Флаг {args[i]} требует числового значения.");
            return false;
        }
        if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            shell.WriteError($"Не удалось распарсить число: {args[i + 1]}");
            return false;
        }
        i++;
        return true;
    }

    private static bool TryNextString(string[] args, ref int i, out string result, IConsoleShell shell)
    {
        result = "";
        if (i + 1 >= args.Length)
        {
            shell.WriteError($"Флаг {args[i]} требует значения.");
            return false;
        }
        result = args[i + 1];
        i++;
        return true;
    }
}
