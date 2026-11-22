// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.Localization;

/// <summary>
/// Позволяет писать в прототипах и ключ локализации, и просто готовый текст.
/// </summary>
public static class PrototypeLocHelper
{
    public static string Resolve(string? textOrKey)
    {
        if (string.IsNullOrEmpty(textOrKey))
            return string.Empty;

        return Loc.TryGetString(textOrKey, out var value) ? value : textOrKey;
    }
}
