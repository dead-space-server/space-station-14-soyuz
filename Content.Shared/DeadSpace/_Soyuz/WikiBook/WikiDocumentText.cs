// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Text;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

/// <summary>Простой текст документа и его блоков (для поиска и оглавления)</summary>
public static class WikiDocumentText
{
    public static string Plain(WikiDocument document)
    {
        var sb = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            Append(block, sb);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    public static string Plain(WikiBlock block)
    {
        var sb = new StringBuilder();
        Append(block, sb);
        return sb.ToString().Trim();
    }

    private static void Append(WikiBlock block, StringBuilder sb)
    {
        foreach (var run in block.Inlines)
        {
            sb.Append(run.Text);
        }

        if (block.Title.Length > 0)
            sb.Append(' ').Append(block.Title);

        foreach (var child in block.Children)
        {
            sb.Append(' ');
            Append(child, sb);
        }

        foreach (var row in block.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var child in cell.Blocks)
                {
                    sb.Append(' ');
                    Append(child, sb);
                }
            }
        }
    }
}
