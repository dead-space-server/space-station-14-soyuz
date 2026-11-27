// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace._Soyuz.WikiBook;

/// <summary>Прокручиваемый показ документа вики</summary>
public sealed class WikiDocumentView : ScrollContainer
{
    private readonly BoxContainer _content = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        HorizontalExpand = true,
    };

    private readonly List<(Control Control, string Text)> _topLevel = new();
    private readonly List<(WikiBlock Heading, Control Control)> _headings = new();
    private readonly Dictionary<string, string> _links = new();

    /// <summary>Заголовки верхнего уровня для оглавлени.</summary>
    public IReadOnlyList<(WikiBlock Heading, Control Control)> Headings => _headings;

    /// <summary>Ссылки на другие страницы</summary>
    public IReadOnlyDictionary<string, string> Links => _links;

    public WikiDocumentView()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        HScrollEnabled = false;
        AddChild(_content);
    }

    public void Clear()
    {
        _content.RemoveAllChildren();
        _topLevel.Clear();
        _headings.Clear();
        _links.Clear();
    }

    public void ShowMessage(string text)
    {
        Clear();
        _content.AddChild(new Label { Text = text, Margin = new Thickness(8) });
    }

    public void Show(WikiDocument document)
    {
        Clear();

        foreach (var block in document.Blocks)
        {
            var control = BuildBlock(block, false);
            if (control == null)
                continue;

            _content.AddChild(control);
            _topLevel.Add((control, WikiDocumentText.Plain(block)));

            if (block.Kind == WikiBlockKind.Heading)
                _headings.Add((block, control));
        }
    }
    public void ApplySearch(string query)
    {
        var empty = string.IsNullOrWhiteSpace(query);
        foreach (var (control, text) in _topLevel)
        {
            control.Visible = empty || text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void ScrollTo(Control target)
    {
        var y = target.GlobalPosition.Y - _content.GlobalPosition.Y;
        SetScrollValue(new Vector2(0, Math.Max(0, y)));
    }

    private Control? BuildBlock(WikiBlock block, bool bold)
    {
        switch (block.Kind)
        {
            case WikiBlockKind.Heading:
                {
                    var level = Math.Clamp(block.Level, 1, 3);
                    var label = MakeRich($"[head={level}]{Markup(block.Inlines, true)}[/head]");
                    label.Margin = new Thickness(0, 8, 0, 2);
                    return label;
                }
            case WikiBlockKind.Paragraph:
                {
                    var label = MakeRich(Markup(block.Inlines, bold));
                    label.Margin = new Thickness(0, 0, 0, 4);
                    return label;
                }
            case WikiBlockKind.ListItem:
                {
                    var row = new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Horizontal,
                        Margin = new Thickness(12 * (block.Level + 1), 0, 0, 2),
                    };

                    var marker = block.Number < 0 ? string.Empty : block.Ordered ? $"{block.Number}." : "•";
                    row.AddChild(new Label { Text = marker, MinSize = new Vector2(24, 0), VerticalAlignment = Control.VAlignment.Top });
                    row.AddChild(MakeRich(Markup(block.Inlines, bold)));
                    return row;
                }
            case WikiBlockKind.Rule:
                return new PanelContainer
                {
                    MinSize = new Vector2(0, 2),
                    Margin = new Thickness(0, 6, 0, 6),
                    PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#666666") },
                };
            case WikiBlockKind.Table:
                return BuildTable(block);
            case WikiBlockKind.Collapsible:
                return BuildCollapsible(block);
        }

        return null;
    }

    private Control BuildTable(WikiBlock block)
    {
        var columns = block.Rows.Count == 0 ? 1 : Math.Max(1, block.Rows.Max(r => r.Cells.Count));
        var grid = new GridContainer
        {
            Columns = columns,
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 6),
        };

        foreach (var row in block.Rows)
        {
            for (var i = 0; i < columns; i++)
            {
                grid.AddChild(BuildCell(i < row.Cells.Count ? row.Cells[i] : null));
            }
        }

        return grid;
    }

    private Control BuildCell(WikiTableCell? cell)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(cell is { Header: true } ? "#2b303b" : "#1c1f27"),
                BorderColor = Color.FromHex("#4a4f5c"),
                BorderThickness = new Thickness(1),
            },
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(4),
        };

        if (cell != null)
        {
            foreach (var child in cell.Blocks)
            {
                var control = BuildBlock(child, cell.Header);
                if (control != null)
                    box.AddChild(control);
            }
        }

        panel.AddChild(box);
        return panel;
    }

    private Control BuildCollapsible(WikiBlock block)
    {
        var title = string.IsNullOrWhiteSpace(block.Title) ? Loc.GetString("wiki-book-more") : block.Title;

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Visible = !block.Collapsed,
            Margin = new Thickness(12, 2, 0, 4),
        };

        foreach (var child in block.Children)
        {
            var control = BuildBlock(child, false);
            if (control != null)
                body.AddChild(control);
        }

        var button = new Button
        {
            Text = (block.Collapsed ? "[+] " : "[-] ") + title,
            HorizontalExpand = true,
            ClipText = true,
        };

        button.OnPressed += _ =>
        {
            body.Visible = !body.Visible;
            button.Text = (body.Visible ? "[-] " : "[+] ") + title;
        };

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(0, 2, 0, 2) };
        box.AddChild(button);
        box.AddChild(body);
        return box;
    }

    private static RichTextLabel MakeRich(string markup)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromMarkupPermissive(markup));
        return label;
    }

    private string Markup(List<WikiInline> inlines, bool bold)
    {
        var sb = new StringBuilder();
        foreach (var run in inlines)
        {
            var text = FormattedMessage.EscapeText(run.Text);
            if (text.Length == 0)
                continue;

            if (run.Link != null)
            {
                text = $"[color=#7fb2ff]{text}[/color]";
                _links.TryAdd(run.Link, WikiTitleHelper.Short(run.Link));
            }

            if (run.Italic)
                text = $"[italic]{text}[/italic]";

            if (run.Bold || bold)
                text = $"[bold]{text}[/bold]";

            sb.Append(text);
        }

        return sb.ToString();
    }
}
