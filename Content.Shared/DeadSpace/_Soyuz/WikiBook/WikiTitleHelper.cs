// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

public static class WikiTitleHelper
{
    public static string Short(string title)
    {
        title = title.Replace('_', ' ').Trim();

        var index = title.IndexOf(':');
        if (index <= 0 || index + 1 >= title.Length)
            return title;

        if (title.AsSpan(0, index).Contains(' '))
            return title;

        return title[(index + 1)..].TrimStart();
    }
}
