// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Linq;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Content.Shared.DeadSpace._Soyuz.WikiBook;

namespace Content.Server.DeadSpace._Soyuz.WikiBook;

internal sealed class HtmlNode
{
    public string Tag = string.Empty;
    public string? Text;
    public Dictionary<string, string> Attrs = new();
    public List<HtmlNode> Children = new();
    public HtmlNode? Parent;

    public bool IsText => Text != null;

    public string? Attr(string name) => Attrs.TryGetValue(name, out var value) ? value : null;

    public bool HasClass(string name)
    {
        if (!Attrs.TryGetValue("class", out var classes))
            return false;

        foreach (var part in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == name)
                return true;
        }

        return false;
    }
}

/// <summary>Небольшой терпимый HTML-разборщик под вывод MediaWiki</summary>
internal static class HtmlParser
{
    private static readonly HashSet<string> VoidTags = new() { "br", "hr", "img", "meta", "link", "input", "wbr", "col" };
    private static readonly HashSet<string> RawTextTags = new() { "script", "style" };
    private const int MaxDepth = 100;

    public static HtmlNode Parse(string html)
    {
        var root = new HtmlNode { Tag = "#root" };
        var current = root;
        var i = 0;

        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    var commentEnd = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = commentEnd < 0 ? html.Length : commentEnd + 3;
                    continue;
                }

                if (i + 1 < html.Length && html[i + 1] == '/')
                {
                    var end = html.IndexOf('>', i);
                    if (end < 0)
                        break;

                    var name = html.Substring(i + 2, end - i - 2).Trim().ToLowerInvariant();
                    var node = current;
                    while (node != null && node.Tag != name)
                    {
                        node = node.Parent;
                    }

                    if (node?.Parent != null)
                        current = node.Parent;

                    i = end + 1;
                    continue;
                }

                if (i + 1 < html.Length && char.IsLetter(html[i + 1]))
                {
                    i = ParseStartTag(html, i, ref current);
                    continue;
                }
            }

            var next = html.IndexOf('<', html[i] == '<' ? i + 1 : i);
            if (next < 0)
                next = html.Length;

            current.Children.Add(new HtmlNode
            {
                Text = WebUtility.HtmlDecode(html.Substring(i, next - i)),
                Parent = current,
            });
            i = next;
        }

        return root;
    }

    private static int Depth(HtmlNode node)
    {
        var depth = 0;
        for (var n = node.Parent; n != null; n = n.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static int ParseStartTag(string html, int start, ref HtmlNode current)
    {
        var j = start + 1;
        var nameStart = j;
        while (j < html.Length && (char.IsLetterOrDigit(html[j]) || html[j] == '-' || html[j] == ':'))
        {
            j++;
        }

        var tag = html.Substring(nameStart, j - nameStart).ToLowerInvariant();
        var element = new HtmlNode { Tag = tag, Parent = current };
        var selfClose = false;

        while (j < html.Length)
        {
            while (j < html.Length && char.IsWhiteSpace(html[j]))
            {
                j++;
            }

            if (j >= html.Length)
                break;

            if (html[j] == '>')
            {
                j++;
                break;
            }

            if (html[j] == '/')
            {
                selfClose = true;
                j++;
                continue;
            }

            var attrStart = j;
            while (j < html.Length && !char.IsWhiteSpace(html[j]) && html[j] != '=' && html[j] != '>' && html[j] != '/')
            {
                j++;
            }

            var attrName = html.Substring(attrStart, j - attrStart).ToLowerInvariant();
            var value = string.Empty;

            while (j < html.Length && char.IsWhiteSpace(html[j]))
            {
                j++;
            }

            if (j < html.Length && html[j] == '=')
            {
                j++;
                while (j < html.Length && char.IsWhiteSpace(html[j]))
                {
                    j++;
                }

                if (j < html.Length && (html[j] == '"' || html[j] == '\''))
                {
                    var quote = html[j];
                    j++;
                    var valueStart = j;
                    while (j < html.Length && html[j] != quote)
                    {
                        j++;
                    }

                    value = html.Substring(valueStart, j - valueStart);
                    if (j < html.Length)
                        j++;
                }
                else
                {
                    var valueStart = j;
                    while (j < html.Length && !char.IsWhiteSpace(html[j]) && html[j] != '>')
                    {
                        j++;
                    }

                    value = html.Substring(valueStart, j - valueStart);
                }
            }

            if (attrName.Length > 0)
                element.Attrs[attrName] = WebUtility.HtmlDecode(value);
            else if (j == attrStart)
                j++;
        }

        current.Children.Add(element);

        if (RawTextTags.Contains(tag))
        {
            var close = html.IndexOf("</" + tag, j, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                return html.Length;

            var gt = html.IndexOf('>', close);
            return gt < 0 ? html.Length : gt + 1;
        }

        if (!selfClose && !VoidTags.Contains(tag) && Depth(current) < MaxDepth)
            current = element;

        return j;
    }
}

internal static class WikiTitle
{
    public static bool IsValid(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return false;

        foreach (var c in title)
        {
            if (char.IsControl(c) || c is '<' or '>' or '[' or ']' or '{' or '}' or '|' or '#')
                return false;
        }

        return true;
    }
}

/// <summary>Превращает HTML страницы MediaWiki в <see cref="WikiDocument"/></summary>
internal sealed class WikiHtmlConverter
{
    private const int MaxBlocks = 3000;
    private const int MaxDepth = 12;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> InlineTags = new()
    {
        "a", "b", "strong", "i", "em", "u", "span", "small", "big", "sup", "sub", "code", "tt", "s", "del", "ins",
        "mark", "abbr", "font", "cite", "q", "bdi", "kbd", "samp", "var", "time",
    };

    private static readonly HashSet<string> SkipTags = new()
    {
        "script", "style", "noscript", "figure", "img", "svg", "button", "form", "input", "nav", "video", "audio", "iframe",
    };

    private static readonly string[] SkipClasses =
    {
        "mw-editsection", "toc", "noprint", "mw-empty-elt", "mw-collapsible-toggle", "printfooter", "catlinks",
        "navbar", "reference", "mw-cite-backlink",
    };

    private int _blocks;

    public static WikiDocument Convert(string html, string title)
    {
        var root = HtmlParser.Parse(html);
        var converter = new WikiHtmlConverter();
        var document = new WikiDocument { Title = title };
        converter.Walk(root, document.Blocks, 0);
        return document;
    }

    private static bool ShouldSkip(HtmlNode node)
    {
        if (node.IsText)
            return false;

        if (SkipTags.Contains(node.Tag) || node.Attrs.ContainsKey("data-skip"))
            return true;

        foreach (var cls in SkipClasses)
        {
            if (node.HasClass(cls))
                return true;
        }

        var style = node.Attr("style");
        return style != null && style.Replace(" ", string.Empty).Contains("display:none", StringComparison.OrdinalIgnoreCase);
    }

    private void Add(List<WikiBlock> output, WikiBlock block)
    {
        _blocks++;
        output.Add(block);
    }

    private void Walk(HtmlNode container, List<WikiBlock> output, int depth)
    {
        if (depth > MaxDepth)
            return;

        var pending = new List<HtmlNode>();
        foreach (var child in container.Children)
        {
            if (_blocks >= MaxBlocks)
                break;

            if (ShouldSkip(child))
                continue;

            if (child.IsText || child.Tag == "br" || InlineTags.Contains(child.Tag))
            {
                pending.Add(child);
                continue;
            }

            FlushPending(pending, output);
            HandleBlock(child, output, depth);
        }

        FlushPending(pending, output);
    }

    private void FlushPending(List<HtmlNode> pending, List<WikiBlock> output)
    {
        if (pending.Count == 0)
            return;

        var runs = CollectRuns(pending);
        pending.Clear();

        if (runs.Count == 0)
            return;

        Add(output, new WikiBlock { Kind = WikiBlockKind.Paragraph, Inlines = runs });
    }

    private void HandleBlock(HtmlNode node, List<WikiBlock> output, int depth)
    {
        switch (node.Tag)
        {
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                {
                    var runs = CollectRuns(node.Children);
                    if (runs.Count > 0)
                        Add(output, new WikiBlock { Kind = WikiBlockKind.Heading, Level = node.Tag[1] - '0', Inlines = runs });
                    break;
                }
            case "p":
                {
                    var runs = CollectRuns(node.Children);
                    if (runs.Count == 0)
                        break;

                    var styledLevel = StyledHeadingLevel(node);
                    Add(output, new WikiBlock
                    {
                        Kind = styledLevel > 0 ? WikiBlockKind.Heading : WikiBlockKind.Paragraph,
                        Level = styledLevel,
                        Inlines = runs,
                    });
                    break;
                }
            case "dl":
                HandleDefinitions(node, 0, output);
                break;
            case "li":
            case "dd":
                {
                    var runs = CollectRuns(node.Children);
                    if (runs.Count > 0)
                        Add(output, new WikiBlock { Kind = WikiBlockKind.Paragraph, Inlines = runs });
                    break;
                }
            case "dt":
                {
                    var runs = CollectRuns(node.Children);
                    foreach (var run in runs)
                    {
                        run.Bold = true;
                    }

                    if (runs.Count > 0)
                        Add(output, new WikiBlock { Kind = WikiBlockKind.Paragraph, Inlines = runs });
                    break;
                }
            case "ul":
            case "ol":
                HandleList(node, node.Tag == "ol", 0, output, depth);
                break;
            case "table":
                HandleTable(node, output, depth);
                break;
            case "hr":
                Add(output, new WikiBlock { Kind = WikiBlockKind.Rule });
                break;
            case "pre":
                {
                    var text = RawText(node).TrimEnd();
                    if (text.Length > 0)
                    {
                        Add(output, new WikiBlock
                        {
                            Kind = WikiBlockKind.Paragraph,
                            Inlines = new List<WikiInline> { new() { Text = text } },
                        });
                    }

                    break;
                }
            case "details":
                HandleDetails(node, output, depth);
                break;
            default:
                if (node.HasClass("mw-collapsible"))
                    HandleCollapsibleDiv(node, output, depth);
                else
                    Walk(node, output, depth + 1);
                break;
        }
    }

    private void HandleList(HtmlNode list, bool ordered, int level, List<WikiBlock> output, int depth)
    {
        if (depth > MaxDepth)
            return;

        var number = 1;
        if (ordered && int.TryParse(list.Attr("start"), out var start))
            number = start;

        foreach (var item in list.Children)
        {
            if (item.IsText || ShouldSkip(item))
                continue;

            if (item.Tag is "ul" or "ol")
            {
                HandleList(item, item.Tag == "ol", level + 1, output, depth + 1);
                continue;
            }

            if (item.Tag != "li")
                continue;

            var pending = new List<HtmlNode>();
            var emitted = false;

            foreach (var child in item.Children)
            {
                if (ShouldSkip(child))
                    continue;

                if (child.Tag is "ul" or "ol")
                {
                    emitted = EmitListItem(pending, ordered, level, number, emitted, output);
                    HandleList(child, child.Tag == "ol", level + 1, output, depth + 1);
                }
                else if (child.Tag == "dl")
                {
                    emitted = EmitListItem(pending, ordered, level, number, emitted, output);
                    HandleDefinitions(child, level + 1, output);
                }
                else
                {
                    pending.Add(child);
                }
            }

            EmitListItem(pending, ordered, level, number, emitted, output);
            number++;
        }
    }

    private bool EmitListItem(List<HtmlNode> pending, bool ordered, int level, int number, bool emitted, List<WikiBlock> output)
    {
        if (pending.Count == 0)
            return emitted;

        var runs = CollectRuns(pending);
        pending.Clear();

        if (runs.Count == 0)
            return emitted;

        var first = true;
        foreach (var segment in SplitRuns(runs))
        {
            Add(output, new WikiBlock
            {
                Kind = WikiBlockKind.ListItem,
                Level = first ? level : level + 1,
                Ordered = first && ordered,
                Number = first && !emitted ? number : -1,
                Inlines = segment,
            });
            first = false;
        }

        return true;
    }

    private void HandleDefinitions(HtmlNode list, int level, List<WikiBlock> output)
    {
        foreach (var child in list.Children)
        {
            if (child.IsText || ShouldSkip(child) || (child.Tag != "dd" && child.Tag != "dt"))
                continue;

            foreach (var segment in SplitRuns(CollectRuns(child.Children)))
            {
                Add(output, new WikiBlock
                {
                    Kind = WikiBlockKind.ListItem,
                    Level = level,
                    Number = -1,
                    Inlines = segment,
                });
            }
        }
    }

    private static List<List<WikiInline>> SplitRuns(List<WikiInline> runs)
    {
        var result = new List<List<WikiInline>>();
        var current = new List<WikiInline>();

        void Flush()
        {
            if (current.Count > 0)
            {
                current[0].Text = current[0].Text.TrimStart(' ');
                current[^1].Text = current[^1].Text.TrimEnd(' ');
                current.RemoveAll(r => r.Text.Length == 0);
                if (current.Count > 0)
                    result.Add(current);
            }

            current = new List<WikiInline>();
        }

        foreach (var run in runs)
        {
            if (run.Text == "\n")
                Flush();
            else
                current.Add(run);
        }

        Flush();
        return result;
    }

    private static readonly Regex FontSize = new(@"font-size\s*:\s*([\d.]+)\s*(rem|em|px|pt|%)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static int StyledHeadingLevel(HtmlNode node)
    {
        var style = node.Attr("style");
        if (style == null)
            return 0;

        var match = FontSize.Match(style);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var em = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => value / 16.0,
            "pt" => value / 12.0,
            "%" => value / 100.0,
            _ => value,
        };

        return em >= 1.6 ? 1 : em >= 1.2 ? 2 : em >= 1.05 ? 3 : 0;
    }

    private void HandleTable(HtmlNode table, List<WikiBlock> output, int depth)
    {
        var rows = new List<HtmlNode>();
        CollectRows(table, rows);

        foreach (var child in table.Children)
        {
            if (child.Tag != "caption")
                continue;

            var runs = CollectRuns(child.Children);
            foreach (var run in runs)
            {
                run.Bold = true;
            }

            if (runs.Count > 0)
                Add(output, new WikiBlock { Kind = WikiBlockKind.Paragraph, Inlines = runs });
        }

        if (rows.Count == 0)
            return;

        if (table.HasClass("mw-collapsible"))
        {
            var rest = BuildTable(rows.Skip(1).ToList(), depth);
            var collapsible = new WikiBlock
            {
                Kind = WikiBlockKind.Collapsible,
                Title = PlainText(rows[0]),
                Collapsed = table.HasClass("mw-collapsed"),
            };

            if (rest.Rows.Count > 0)
                collapsible.Children.Add(rest);

            Add(output, collapsible);
            return;
        }

        output.Add(BuildTable(rows, depth));
    }

    private static void CollectRows(HtmlNode node, List<HtmlNode> rows)
    {
        foreach (var child in node.Children)
        {
            if (child.Tag == "tr")
                rows.Add(child);
            else if (child.Tag is "thead" or "tbody" or "tfoot")
                CollectRows(child, rows);
        }
    }

    private WikiBlock BuildTable(List<HtmlNode> rows, int depth)
    {
        var block = new WikiBlock { Kind = WikiBlockKind.Table };

        foreach (var tr in rows)
        {
            var row = new WikiTableRow();
            foreach (var td in tr.Children)
            {
                if ((td.Tag != "td" && td.Tag != "th") || ShouldSkip(td))
                    continue;

                var cell = new WikiTableCell { Header = td.Tag == "th" };
                Walk(td, cell.Blocks, depth + 1);
                row.Cells.Add(cell);
            }

            if (row.Cells.Count > 0)
                block.Rows.Add(row);
        }

        _blocks++;
        return block;
    }

    private void HandleDetails(HtmlNode details, List<WikiBlock> output, int depth)
    {
        var summary = details.Children.FirstOrDefault(c => c.Tag == "summary");
        var block = new WikiBlock
        {
            Kind = WikiBlockKind.Collapsible,
            Title = summary != null ? PlainText(summary) : string.Empty,
            Collapsed = !details.Attrs.ContainsKey("open"),
        };

        if (summary != null)
            summary.Attrs["data-skip"] = "1";

        Walk(details, block.Children, depth + 1);
        Add(output, block);
    }

    private void HandleCollapsibleDiv(HtmlNode div, List<WikiBlock> output, int depth)
    {
        var titleNode = Find(div, n => n.HasClass("mw-collapsible-title") || n.HasClass("mw-collapsible-header"), 0);
        var content = Find(div, n => n.HasClass("mw-collapsible-content"), 0);

        var block = new WikiBlock
        {
            Kind = WikiBlockKind.Collapsible,
            Title = titleNode != null ? PlainText(titleNode) : div.Attr("data-expandtext") ?? string.Empty,
            Collapsed = div.HasClass("mw-collapsed"),
        };

        if (titleNode != null)
            titleNode.Attrs["data-skip"] = "1";

        Walk(content ?? div, block.Children, depth + 1);
        Add(output, block);
    }

    private static HtmlNode? Find(HtmlNode node, Func<HtmlNode, bool> predicate, int depth)
    {
        if (depth > 6)
            return null;

        foreach (var child in node.Children)
        {
            if (child.IsText)
                continue;

            if (predicate(child))
                return child;

            var found = Find(child, predicate, depth + 1);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string RawText(HtmlNode node)
    {
        if (node.IsText)
            return node.Text ?? string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var child in node.Children)
        {
            sb.Append(RawText(child));
        }

        return sb.ToString();
    }

    private static string PlainText(HtmlNode node)
    {
        return Whitespace.Replace(RawText(node), " ").Trim();
    }

    private List<WikiInline> CollectRuns(IEnumerable<HtmlNode> nodes)
    {
        var runs = new List<WikiInline>();
        foreach (var node in nodes)
        {
            CollectInline(node, false, false, false, null, runs, 0);
        }

        Normalize(runs);
        return runs;
    }

    private static void CollectInline(HtmlNode node, bool bold, bool italic, bool underline, string? link, List<WikiInline> runs, int depth)
    {
        if (depth > 40 || ShouldSkip(node))
            return;

        if (node.IsText)
        {
            AddRun(runs, node.Text ?? string.Empty, bold, italic, underline, link);
            return;
        }

        if (node.Tag == "br")
        {
            AddRun(runs, "\n", bold, italic, underline, link);
            return;
        }

        var isBold = bold || node.Tag is "b" or "strong" or "th" or "dt";
        var isItalic = italic || node.Tag is "i" or "em" or "cite";
        var isUnderline = underline || node.Tag == "u";
        var nodeLink = node.Tag == "a" ? ExtractLink(node) : link;

        foreach (var child in node.Children)
        {
            CollectInline(child, isBold, isItalic, isUnderline, nodeLink, runs, depth + 1);
        }

        if (node.Tag is "p" or "div" or "li" or "dd" or "dt" or "tr" or "td" or "th")
            AddRun(runs, " ", bold, italic, underline, link);
    }

    private static void AddRun(List<WikiInline> runs, string text, bool bold, bool italic, bool underline, string? link)
    {
        if (text != "\n")
            text = Whitespace.Replace(text, " ");

        if (text.Length == 0)
            return;

        if (text != "\n" && runs.Count > 0)
        {
            var last = runs[^1];
            if (last.Text != "\n" && last.Bold == bold && last.Italic == italic && last.Underline == underline && last.Link == link)
            {
                last.Text += text;
                return;
            }
        }

        runs.Add(new WikiInline { Text = text, Bold = bold, Italic = italic, Underline = underline, Link = link });
    }

    private static void Normalize(List<WikiInline> runs)
    {
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.Text == "\n")
                continue;

            var afterBreak = i == 0 || runs[i - 1].Text == "\n" || runs[i - 1].Text.EndsWith(' ');
            if (afterBreak)
                run.Text = run.Text.TrimStart(' ');

            if (i + 1 < runs.Count && runs[i + 1].Text == "\n")
                run.Text = run.Text.TrimEnd(' ');
        }

        if (runs.Count > 0 && runs[^1].Text != "\n")
            runs[^1].Text = runs[^1].Text.TrimEnd(' ');

        runs.RemoveAll(r => r.Text.Length == 0);

        while (runs.Count > 0 && runs[0].Text == "\n")
        {
            runs.RemoveAt(0);
        }

        while (runs.Count > 0 && runs[^1].Text == "\n")
        {
            runs.RemoveAt(runs.Count - 1);
        }
    }

    private static string? ExtractLink(HtmlNode a)
    {
        var href = a.Attr("href");
        if (string.IsNullOrEmpty(href) || href.StartsWith('#'))
            return null;

        if (a.HasClass("external") || a.HasClass("new") || a.HasClass("extiw"))
            return null;

        if (href.Contains("://") || href.StartsWith("mailto:") || href.StartsWith("//"))
            return null;

        var title = a.Attr("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            var path = href;
            var query = path.IndexOf("title=", StringComparison.Ordinal);
            if (query >= 0)
            {
                path = path[(query + 6)..];
                var amp = path.IndexOf('&');
                if (amp >= 0)
                    path = path[..amp];
            }
            else
            {
                path = path.TrimStart('/');
            }

            var hash = path.IndexOf('#');
            if (hash >= 0)
                path = path[..hash];

            title = Uri.UnescapeDataString(path).Replace('_', ' ');
            if (title.StartsWith("wiki/"))
                title = title[5..];
        }

        title = title.Trim();
        return WikiTitle.IsValid(title) ? title : null;
    }
}
