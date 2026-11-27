// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace._Soyuz.WikiBook;

/// <summary>Окно руководства:</summary>
public sealed class WikiBookWindow : DefaultWindow
{
    public event Action<string>? OnNavigate;

    private readonly Button _backButton;
    private readonly LineEdit _search;
    private readonly Label _status;
    private readonly BoxContainer _toc;
    private readonly BoxContainer _links;
    private readonly WikiDocumentView _view;

    private readonly List<string> _history = new();
    private string? _currentPage;

    public WikiBookWindow()
    {
        Title = Loc.GetString("wiki-book-title");
        MinSize = new Vector2(780, 520);

        _backButton = new Button { Text = Loc.GetString("wiki-book-back"), Disabled = true };
        _backButton.OnPressed += _ => GoBack();

        _search = new LineEdit
        {
            PlaceHolder = Loc.GetString("wiki-book-search"),
            HorizontalExpand = true,
        };
        _search.OnTextChanged += args => _view!.ApplySearch(args.Text);

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.AddChild(_backButton);
        header.AddChild(_search);

        _status = new Label { Margin = new Thickness(0, 0, 0, 4), Visible = false };

        _toc = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _links = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        var left = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinSize = new Vector2(250, 0),
            Margin = new Thickness(0, 0, 6, 0),
        };
        left.AddChild(new Label { Text = Loc.GetString("wiki-book-toc") });
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _toc } });
        left.AddChild(new Label { Text = Loc.GetString("wiki-book-links"), Margin = new Thickness(0, 6, 0, 0) });
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _links } });

        _view = new WikiDocumentView();

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        body.AddChild(left);
        body.AddChild(_view);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        root.AddChild(header);
        root.AddChild(_status);
        root.AddChild(body);

        Contents.AddChild(root);
    }

    public void UpdateState(WikiBookState state)
    {
        _currentPage = state.Page;
        _backButton.Disabled = _history.Count == 0;

        switch (state.Status)
        {
            case WikiLoadStatus.Loading:
                ShowMessage(Loc.GetString("wiki-book-loading"));
                return;
            case WikiLoadStatus.NotFound:
                ShowMessage(Loc.GetString("wiki-book-not-found"));
                return;
            case WikiLoadStatus.Disabled:
                ShowMessage(Loc.GetString("wiki-book-disabled"));
                return;
            case WikiLoadStatus.Error:
                ShowMessage(Loc.GetString("wiki-book-error"));
                return;
        }

        if (state.Document == null)
        {
            ShowMessage(Loc.GetString("wiki-book-empty"));
            return;
        }

        Title = $"{Loc.GetString("wiki-book-title")}: {WikiTitleHelper.Short(state.Document.Title)}";

        if (state.Status == WikiLoadStatus.Stale)
            SetStatus(Loc.GetString("wiki-book-stale"));
        else if (state.Specialized)
            SetStatus(Loc.GetString("wiki-book-specialized"));
        else
            SetStatus(null);

        _view.Show(state.Document);
        _toc.RemoveAllChildren();
        _links.RemoveAllChildren();

        foreach (var (heading, control) in _view.Headings)
        {
            var indent = new string(' ', Math.Max(0, heading.Level - 1) * 2);
            var button = new Button { Text = indent + WikiDocumentText.Plain(heading), ClipText = true };
            button.OnPressed += _ => _view.ScrollTo(control);
            _toc.AddChild(button);
        }

        foreach (var (page, label) in _view.Links)
        {
            var button = new Button { Text = label, ToolTip = page, ClipText = true };
            button.OnPressed += _ => Navigate(page);
            _links.AddChild(button);
        }

        _view.ApplySearch(_search.Text);
    }

    private void SetStatus(string? text)
    {
        _status.Visible = text != null;
        _status.Text = text ?? string.Empty;
    }

    private void ShowMessage(string text)
    {
        _toc.RemoveAllChildren();
        _links.RemoveAllChildren();
        SetStatus(null);
        _view.ShowMessage(text);
    }

    private void Navigate(string page)
    {
        if (_currentPage != null && (_history.Count == 0 || _history[^1] != _currentPage))
            _history.Add(_currentPage);

        _backButton.Disabled = _history.Count == 0;
        OnNavigate?.Invoke(page);
    }

    private void GoBack()
    {
        if (_history.Count == 0)
            return;

        var previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _backButton.Disabled = _history.Count == 0;
        OnNavigate?.Invoke(previous);
    }
}
