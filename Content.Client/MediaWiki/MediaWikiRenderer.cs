using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Content.Client.Message;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.MediaWiki;

public sealed partial class MediaWikiRenderer
{
    private static readonly Regex HeadingRegex = new(@"^(=+)\s*(.*?)\s*\1$", RegexOptions.Compiled);
    private static readonly Regex AnchorRegex = new(@"\{\{Anchor\|([^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WikiLinkWithLabelRegex = new(@"\[\[([^\]|]+)\|([^\]]+)\]\]", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);
    private static readonly Regex BoldItalicRegex = new(@"'''''(.*?)'''''", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BoldRegex = new(@"'''(.*?)'''", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ItalicRegex = new(@"''(.*?)''", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex SpanRegex = new(@"<span(?<attrs>[^>]*)>(?<content>.*?)</span>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlTagRegex = new(@"</?([a-z0-9]+)(?:\s[^>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AlertTextRegex = new(@"text\s*=\s*(.*?)\s*\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BackgroundColorRegex = new(@"background(?:-color)?\s*:\s*(#[0-9a-fA-F]{3,8}|[a-zA-Z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TextColorRegex = new(@"(?:^|;)\s*color\s*:\s*(#[0-9a-fA-F]{3,8}|[a-zA-Z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ColSpanRegex = new(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RowSpanRegex = new(@"rowspan\s*=\s*[""']?(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StyleAttributeRegex = new(@"style\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlCellRegex = new(@"<t(?<type>[hd])(?<attrs>[^>]*)>(?<content>.*?)</t[hd]>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlRowRegex = new(@"<tr(?<attrs>[^>]*)>(?<content>.*?)</tr>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BrRegex = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HrRegex = new(@"<hr[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlBoldRegex = new(@"<(?:b|strong)>(.*?)</(?:b|strong)>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlItalicRegex = new(@"<(?:i|em)>(.*?)</(?:i|em)>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public void Render(Control container, string source)
    {
        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        RenderLines(container, lines);
    }

    private void RenderLines(Control container, IReadOnlyList<string> lines)
    {
        string? pendingAnchor = null;
        var index = 0;

        while (index < lines.Count)
        {
            var rawLine = lines[index];
            var trimmed = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "__NOTOC__")
            {
                index++;
                continue;
            }

            if (TryExtractAnchor(trimmed, out var anchor))
            {
                pendingAnchor = anchor;
                index++;
                continue;
            }

            if (trimmed.Contains("{{Alert", StringComparison.OrdinalIgnoreCase))
            {
                var alertText = ConsumeAlert(lines, ref index);
                AddAlert(container, alertText);
                continue;
            }

            if (trimmed.StartsWith("{|"))
            {
                var tableLines = ConsumeUntil(lines, ref index, line => line.Trim() == "|}");
                RenderWikiTable(container, tableLines);
                continue;
            }

            if (trimmed.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
            {
                var tableBlock = ConsumeHtmlTable(lines, ref index);
                RenderHtmlTable(container, tableBlock);
                continue;
            }

            if (TryParseHeading(trimmed, out var headingLevel, out var headingText))
            {
                var heading = CreateHeading(headingText, headingLevel);
                AttachAnchor(heading, pendingAnchor ?? StripPlainText(headingText));
                pendingAnchor = null;
                container.AddChild(heading);
                index++;
                continue;
            }

            var paragraphLines = ConsumeParagraph(lines, ref index);
            if (paragraphLines.Count == 0)
                continue;

            var paragraph = CreateParagraph(string.Join("\n", paragraphLines));
            AttachAnchor(paragraph, pendingAnchor);
            pendingAnchor = null;
            container.AddChild(paragraph);
        }
    }

    private static List<string> ConsumeParagraph(IReadOnlyList<string> lines, ref int index)
    {
        var result = new List<string>();

        while (index < lines.Count)
        {
            var trimmed = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed == "__NOTOC__" ||
                TryExtractAnchor(trimmed, out _) ||
                trimmed.Contains("{{Alert", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("{|") ||
                trimmed.StartsWith("<table", StringComparison.OrdinalIgnoreCase) ||
                TryParseHeading(trimmed, out _, out _))
            {
                break;
            }

            result.Add(lines[index]);
            index++;
        }

        return result;
    }

    private static string ConsumeAlert(IReadOnlyList<string> lines, ref int index)
    {
        var builder = new StringBuilder();

        while (index < lines.Count)
        {
            builder.AppendLine(lines[index]);
            if (lines[index].Contains("}}", StringComparison.Ordinal))
            {
                index++;
                break;
            }

            index++;
        }

        var block = builder.ToString();
        var match = AlertTextRegex.Match(block);
        return match.Success ? match.Groups[1].Value : block;
    }

    private static List<string> ConsumeUntil(IReadOnlyList<string> lines, ref int index, Func<string, bool> predicate)
    {
        var result = new List<string>();
        while (index < lines.Count)
        {
            var line = lines[index];
            result.Add(line);
            index++;

            if (predicate(line))
                break;
        }

        return result;
    }

    private static string ConsumeHtmlTable(IReadOnlyList<string> lines, ref int index)
    {
        var builder = new StringBuilder();
        while (index < lines.Count)
        {
            builder.AppendLine(lines[index]);
            if (lines[index].Contains("</table>", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                break;
            }

            index++;
        }

        return builder.ToString();
    }

    private void RenderWikiTable(Control container, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return;

        var defaultBackground = ExtractBackgroundColor(lines[0]) ?? "#2a3342b2";
        var rows = ParseWikiRows(lines.Skip(1).Take(lines.Count - 2).ToList());
        if (rows.Count == 0)
            return;

        if (lines[0].Contains("mw-collapsible", StringComparison.OrdinalIgnoreCase) &&
            IsCollapsibleSection(rows))
        {
            RenderCollapsibleWikiSection(container, rows);
            return;
        }

        container.AddChild(CreateTable(rows, defaultBackground));
    }

    private void RenderCollapsibleWikiSection(Control container, IReadOnlyList<WikiTableRow> rows)
    {
        var titleCell = rows[0].Cells[0];
        var bodyCell = rows[1].Cells[0];

        if (!string.IsNullOrWhiteSpace(titleCell.Content))
        {
            var heading = CreateHeading(titleCell.Content, 2);
            AttachAnchor(heading, titleCell.Anchor ?? StripPlainText(titleCell.Content));
            container.AddChild(heading);
        }

        var bodyLines = bodyCell.Content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (!string.IsNullOrWhiteSpace(bodyCell.Anchor))
            bodyLines.Insert(0, $"{{{{Anchor|{bodyCell.Anchor}}}}}");

        RenderLines(container, bodyLines);
    }

    private void RenderHtmlTable(Control container, string tableBlock)
    {
        var defaultBackground = ExtractBackgroundColor(tableBlock) ?? "#2a3342b2";
        var rows = ParseHtmlRows(tableBlock);
        if (rows.Count == 0)
            return;

        container.AddChild(CreateTable(rows, defaultBackground));
    }

    private static List<WikiTableRow> ParseWikiRows(IReadOnlyList<string> lines)
    {
        var rows = new List<WikiTableRow>();
        WikiTableRow? currentRow = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith("|-"))
            {
                currentRow = null;
                continue;
            }

            if (trimmed.StartsWith("!") || trimmed.StartsWith("|"))
            {
                currentRow ??= new WikiTableRow();
                if (!rows.Contains(currentRow))
                    rows.Add(currentRow);

                var isHeader = trimmed[0] == '!';
                var body = trimmed[1..];
                var separator = isHeader ? "!!" : "||";
                var parts = SplitTopLevel(body, separator);

                foreach (var part in parts)
                {
                    currentRow.Cells.Add(ParseWikiCell(part, isHeader));
                }

                continue;
            }

            if (currentRow?.Cells.Count > 0)
            {
                var lastCell = currentRow.Cells[^1];
                lastCell.Content = string.IsNullOrWhiteSpace(lastCell.Content)
                    ? trimmed
                    : $"{lastCell.Content}\n{trimmed}";
            }
        }

        return rows;
    }

    private static WikiTableCell ParseWikiCell(string spec, bool isHeader)
    {
        var separatorIndex = FindFirstTopLevelPipe(spec);
        var attributeText = separatorIndex >= 0 ? spec[..separatorIndex] : string.Empty;
        var contentText = separatorIndex >= 0 ? spec[(separatorIndex + 1)..] : spec;
        var anchor = ExtractAnchor(contentText);
        contentText = AnchorRegex.Replace(contentText, string.Empty);

        return new WikiTableCell
        {
            Header = isHeader,
            Content = contentText.Trim(),
            Anchor = anchor,
            BackgroundColor = ExtractBackgroundColor(attributeText),
            TextColor = ExtractTextColor(attributeText),
            AlignCenter = attributeText.Contains("text-align: center", StringComparison.OrdinalIgnoreCase) ||
                          contentText.Contains("<center>", StringComparison.OrdinalIgnoreCase),
            Colspan = ParseSpan(attributeText, ColSpanRegex),
            Rowspan = ParseSpan(attributeText, RowSpanRegex),
        };
    }

    private static List<WikiTableRow> ParseHtmlRows(string tableBlock)
    {
        var rows = new List<WikiTableRow>();
        var pendingRowSpans = new Dictionary<int, PendingHtmlSpan>();

        foreach (Match rowMatch in HtmlRowRegex.Matches(tableBlock))
        {
            var row = new WikiTableRow();
            var column = 0;
            PendingHtmlSpan? pending;

            while (pendingRowSpans.TryGetValue(column, out pending))
            {
                row.Cells.Add(pending.Cell.CloneForSpan());
                pending.RemainingRows--;
                if (pending.RemainingRows <= 0)
                    pendingRowSpans.Remove(column);
                else
                    pendingRowSpans[column] = pending;
                column++;
            }

            foreach (Match cellMatch in HtmlCellRegex.Matches(rowMatch.Groups["content"].Value))
            {
                while (pendingRowSpans.TryGetValue(column, out pending))
                {
                    row.Cells.Add(pending.Cell.CloneForSpan());
                    pending.RemainingRows--;
                    if (pending.RemainingRows <= 0)
                        pendingRowSpans.Remove(column);
                    else
                        pendingRowSpans[column] = pending;
                    column++;
                }

                var attrs = cellMatch.Groups["attrs"].Value;
                var header = string.Equals(cellMatch.Groups["type"].Value, "h", StringComparison.OrdinalIgnoreCase);
                var inner = cellMatch.Groups["content"].Value;
                var anchor = ExtractAnchor(inner);
                inner = AnchorRegex.Replace(inner, string.Empty);
                var colspan = ParseSpan(attrs, ColSpanRegex);
                var rowspan = ParseSpan(attrs, RowSpanRegex);

                var cell = new WikiTableCell
                {
                    Header = header,
                    Content = inner.Trim(),
                    Anchor = anchor,
                    BackgroundColor = ExtractBackgroundColor(attrs),
                    TextColor = ExtractTextColor(attrs),
                    AlignCenter = attrs.Contains("text-align:center", StringComparison.OrdinalIgnoreCase) ||
                                  attrs.Contains("text-align: center", StringComparison.OrdinalIgnoreCase),
                    Colspan = colspan,
                    Rowspan = rowspan,
                };

                for (var i = 0; i < colspan; i++)
                {
                    var cellToAdd = i == 0 ? cell : cell.CloneEmpty();
                    row.Cells.Add(cellToAdd);

                    if (rowspan > 1)
                    {
                        pendingRowSpans[column] = new PendingHtmlSpan(cellToAdd, rowspan - 1);
                    }

                    column++;
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private Control CreateTable(IReadOnlyList<WikiTableRow> rows, string defaultBackground)
    {
        var columnCount = rows.Max(static row => row.Cells.Count);

        var table = new TableContainer
        {
            Columns = columnCount,
            HorizontalExpand = true,
        };

        foreach (var row in rows)
        {
            foreach (var cell in row.Cells)
            {
                table.AddChild(CreateCellControl(cell, defaultBackground));
            }

            for (var i = row.Cells.Count; i < columnCount; i++)
            {
                table.AddChild(CreateCellControl(new WikiTableCell(), defaultBackground));
            }
        }

        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                table,
            },
        };
    }

    private Control CreateCellControl(WikiTableCell cell, string defaultBackground)
    {
        var background = ParseColor(cell.BackgroundColor) ?? ParseColor(defaultBackground) ?? new Color(42, 51, 66, 178);
        var border = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = Color.White,
            BorderThickness = new Thickness(1),
        };

        var label = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(6),
        };

        var text = ConvertBlockMarkup(cell.Content);
        if (cell.Header && !string.IsNullOrWhiteSpace(text))
            text = $"[bold]{text}[/bold]";

        if (cell.TextColor != null && !string.IsNullOrWhiteSpace(text))
            text = $"[color={cell.TextColor}]{text}[/color]";

        label.SetMarkupPermissive(string.IsNullOrWhiteSpace(text) ? " " : text);

        var content = new PanelContainer
        {
            PanelOverride = border,
            MinHeight = cell.Header ? 36 : 24,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        if (cell.AlignCenter || cell.Header)
        {
            var center = new CenterContainer
            {
                HorizontalExpand = true,
                VerticalExpand = true,
            };
            center.AddChild(label);
            content.AddChild(center);
        }
        else
        {
            content.AddChild(label);
        }

        AttachAnchor(content, cell.Anchor);
        return content;
    }

    private Control CreateHeading(string text, int level)
    {
        var heading = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 12, 0, 6),
        };

        heading.StyleClasses.Add(level <= 2 ? "LabelHeadingBigger" : level <= 4 ? "LabelHeading" : "LabelKeyText");
        heading.SetMarkupPermissive($"[bold]{FormattedMessage.EscapeText(StripPlainText(text))}[/bold]");
        return heading;
    }

    private Control CreateParagraph(string text)
    {
        var paragraph = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 10),
        };

        paragraph.SetMarkupPermissive(ConvertBlockMarkup(text));
        return paragraph;
    }

    private void AddAlert(Control container, string text)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 12),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new Color(91, 31, 31, 220),
                BorderColor = new Color(255, 214, 0),
                BorderThickness = new Thickness(2),
            },
        };

        var label = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(10),
        };
        label.SetMarkupPermissive($"[bold]{ConvertBlockMarkup(text)}[/bold]");

        panel.AddChild(label);
        container.AddChild(panel);
    }

    private static void AttachAnchor(Control control, string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
            return;

        anchor = StripPlainText(anchor).Trim().Replace('_', ' ');
        if (anchor.Length == 0)
            return;

        control.Name = anchor;
    }

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        var match = HeadingRegex.Match(line);
        if (!match.Success)
        {
            level = 0;
            text = string.Empty;
            return false;
        }

        level = match.Groups[1].Value.Length;
        text = match.Groups[2].Value;
        return true;
    }

    private static bool TryExtractAnchor(string line, out string anchor)
    {
        var match = AnchorRegex.Match(line);
        if (!match.Success)
        {
            anchor = string.Empty;
            return false;
        }

        anchor = match.Groups[1].Value.Trim();
        return true;
    }

    private static string ConvertBlockMarkup(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = NormalizeHtmlTagWhitespace(text);
        text = BrRegex.Replace(text, "\n");
        text = HrRegex.Replace(text, "\n--------------------\n");

        var lines = text.Split('\n');
        var builder = new StringBuilder();
        var paragraph = new StringBuilder();
        var orderedIndex = 1;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;

            if (builder.Length > 0 && builder[^1] != '\n')
                builder.AppendLine();

            builder.AppendLine(paragraph.ToString());
            paragraph.Clear();
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph();
                if (builder.Length > 0 && builder[^1] != '\n')
                    builder.AppendLine();
                orderedIndex = 1;
                continue;
            }

            var listMarker = '\0';
            if (trimmed.StartsWith("<li>", StringComparison.OrdinalIgnoreCase))
            {
                listMarker = '*';
                trimmed = trimmed[4..].Trim();
            }
            else if (trimmed.StartsWith('*'))
            {
                listMarker = '*';
                trimmed = trimmed[1..].Trim();
            }
            else if (trimmed.StartsWith('#'))
            {
                listMarker = '#';
                trimmed = trimmed[1..].Trim();
            }

            trimmed = trimmed.Replace("</li>", "", StringComparison.OrdinalIgnoreCase);

            if (listMarker != '\0')
            {
                FlushParagraph();
                var prefix = listMarker == '#'
                    ? $"{orderedIndex++}. "
                    : "• ";

                if (listMarker != '#')
                    prefix = "- ";

                builder.Append(prefix);
                builder.AppendLine(ConvertInlineMarkup(trimmed));
                continue;
            }

            if (paragraph.Length > 0)
                paragraph.Append(' ');

            paragraph.Append(ConvertInlineMarkup(trimmed));
        }

        FlushParagraph();
        return builder.ToString().Trim();
    }

    private static string ConvertInlineMarkup(string text)
    {
        text = text.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        text = AnchorRegex.Replace(text, string.Empty);

        text = SpanRegex.Replace(text, match =>
        {
            var inner = ConvertInlineMarkup(match.Groups["content"].Value);
            var color = ExtractTextColor(match.Groups["attrs"].Value);
            return color != null ? $"[color={color}]{inner}[/color]" : inner;
        });

        text = text.Replace("<center>", "", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("</center>", "", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("<div>", "", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("</div>", "", StringComparison.OrdinalIgnoreCase);

        text = WikiLinkWithLabelRegex.Replace(text, static match =>
            CreateTextLinkMarkup(match.Groups[1].Value, StripPlainText(match.Groups[2].Value)));
        text = WikiLinkRegex.Replace(text, static match =>
        {
            var target = match.Groups[1].Value;
            var label = target;
            var hashIndex = label.LastIndexOf('#');
            if (hashIndex >= 0 && hashIndex < label.Length - 1)
                label = label[(hashIndex + 1)..];
            return CreateTextLinkMarkup(target, StripPlainText(label));
        });

        text = HtmlBoldRegex.Replace(text, "[bold]$1[/bold]");
        text = HtmlItalicRegex.Replace(text, "[italic]$1[/italic]");
        text = BoldItalicRegex.Replace(text, "[bold][italic]$1[/italic][/bold]");
        text = BoldRegex.Replace(text, "[bold]$1[/bold]");
        text = ItalicRegex.Replace(text, "[italic]$1[/italic]");

        text = HtmlTagRegex.Replace(text, string.Empty);

        return text.Trim();
    }

    private static string NormalizeHtmlTagWhitespace(string text)
    {
        return HtmlTagRegex.Replace(text, static match =>
        {
            var tag = match.Value;
            if (!tag.Contains('\r') && !tag.Contains('\n') && !tag.Contains('\t'))
                return tag;

            tag = string.Join(" ", tag.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
            tag = tag.Replace(" >", ">", StringComparison.Ordinal);
            tag = tag.Replace(" />", "/>", StringComparison.Ordinal);
            return tag;
        });
    }

    private static string StripPlainText(string text)
    {
        return FormattedMessage.RemoveMarkupPermissive(ConvertBlockMarkup(text));
    }

    private static string CreateTextLinkMarkup(string target, string label)
    {
        target = StripPlainText(target).Trim();
        label = StripPlainText(label).Trim();

        if (target.Length == 0 || label.Length == 0)
            return FormattedMessage.EscapeText(label);

        return $"[textlink link=\"{target.Replace("\"", "'")}\"]{FormattedMessage.EscapeText(label)}[/textlink]";
    }

    private static List<string> SplitTopLevel(string text, string separator)
    {
        var result = new List<string>();
        var lastIndex = 0;
        var squareDepth = 0;
        var braceDepth = 0;
        var angleDepth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '[' && text[i + 1] == '[')
            {
                squareDepth++;
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == ']' && text[i + 1] == ']')
            {
                squareDepth = Math.Max(0, squareDepth - 1);
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{')
            {
                braceDepth++;
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}')
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                i++;
                continue;
            }

            if (text[i] == '<')
            {
                angleDepth++;
                continue;
            }

            if (text[i] == '>' && angleDepth > 0)
            {
                angleDepth--;
                continue;
            }

            if (squareDepth == 0 &&
                braceDepth == 0 &&
                angleDepth == 0 &&
                i + separator.Length <= text.Length &&
                string.Compare(text, i, separator, 0, separator.Length, StringComparison.Ordinal) == 0)
            {
                result.Add(text[lastIndex..i]);
                i += separator.Length - 1;
                lastIndex = i + 1;
            }
        }

        result.Add(text[lastIndex..]);
        return result;
    }

    private static int FindFirstTopLevelPipe(string text)
    {
        var squareDepth = 0;
        var braceDepth = 0;
        var angleDepth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '[' && text[i + 1] == '[')
            {
                squareDepth++;
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == ']' && text[i + 1] == ']')
            {
                squareDepth = Math.Max(0, squareDepth - 1);
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{')
            {
                braceDepth++;
                i++;
                continue;
            }

            if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}')
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                i++;
                continue;
            }

            if (text[i] == '<')
            {
                angleDepth++;
                continue;
            }

            if (text[i] == '>' && angleDepth > 0)
            {
                angleDepth--;
                continue;
            }

            if (text[i] == '|' && squareDepth == 0 && braceDepth == 0 && angleDepth == 0)
                return i;
        }

        return -1;
    }

    private static string? ExtractBackgroundColor(string text)
    {
        return ExtractColorFromText(text, BackgroundColorRegex);
    }

    private static string? ExtractTextColor(string text)
    {
        var styleMatch = StyleAttributeRegex.Match(text);
        if (styleMatch.Success)
            return ExtractColorFromText(styleMatch.Groups[1].Value, TextColorRegex);

        return ExtractColorFromText(text, TextColorRegex);
    }

    private static string? ExtractColorFromText(string text, Regex regex)
    {
        var match = regex.Match(text);
        if (!match.Success)
            return null;

        return NormalizeColor(match.Groups[1].Value);
    }

    private static int ParseSpan(string attributes, Regex regex)
    {
        var match = regex.Match(attributes);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) && value > 0
            ? value
            : 1;
    }

    private static string? ExtractAnchor(string text)
    {
        var match = AnchorRegex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static bool IsCollapsibleSection(IReadOnlyList<WikiTableRow> rows)
    {
        if (rows.Count != 2 || rows[0].Cells.Count != 1 || rows[1].Cells.Count != 1)
            return false;

        var body = rows[1].Cells[0].Content;
        return body.Contains("{{Anchor", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("====", StringComparison.Ordinal) ||
               body.Contains("==", StringComparison.Ordinal);
    }

    private static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        color = color.Trim();
        if (color.StartsWith('#'))
            return color;

        return color.ToLowerInvariant() switch
        {
            "white" => "#ffffff",
            "black" => "#000000",
            "red" => "#ff0000",
            "green" => "#00ff00",
            "blue" => "#0000ff",
            "yellow" => "#ffff00",
            _ => null
        };
    }

    private static Color? ParseColor(string? color)
    {
        var normalized = NormalizeColor(color);
        if (normalized == null)
            return null;

        return Color.FromHex(normalized);
    }

    private sealed class WikiTableRow
    {
        public List<WikiTableCell> Cells { get; } = new();
    }

    private sealed class WikiTableCell
    {
        public bool Header { get; init; }
        public string Content { get; set; } = string.Empty;
        public string? Anchor { get; init; }
        public string? BackgroundColor { get; init; }
        public string? TextColor { get; init; }
        public bool AlignCenter { get; init; }
        public int Colspan { get; init; } = 1;
        public int Rowspan { get; init; } = 1;

        public WikiTableCell CloneEmpty()
        {
            return new WikiTableCell
            {
                Header = Header,
                Anchor = Anchor,
                BackgroundColor = BackgroundColor,
                TextColor = TextColor,
                AlignCenter = AlignCenter,
            };
        }

        public WikiTableCell CloneForSpan()
        {
            return new WikiTableCell
            {
                Header = Header,
                Content = Content,
                Anchor = Anchor,
                BackgroundColor = BackgroundColor,
                TextColor = TextColor,
                AlignCenter = AlignCenter,
            };
        }
    }

    private sealed class PendingHtmlSpan
    {
        public WikiTableCell Cell { get; }
        public int RemainingRows { get; set; }

        public PendingHtmlSpan(WikiTableCell cell, int remainingRows)
        {
            Cell = cell;
            RemainingRows = remainingRows;
        }
    }
}
