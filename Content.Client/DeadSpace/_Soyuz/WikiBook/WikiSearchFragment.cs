// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace._Soyuz.WikiBook;

/// <summary>Интерфейс программы КПК Руководства</summary>
public sealed class WikiSearchFragment : BoxContainer
{
    public event Action<string>? OnSearch;
    public event Action<string>? OnOpen;

    private readonly LineEdit _search;
    private readonly ScrollContainer _resultsScroll;
    private readonly BoxContainer _results;
    private readonly BoxContainer _articlePanel;
    private readonly Button _backButton;
    private readonly Label _status;
    private readonly WikiDocumentView _view;
    private readonly BoxContainer _links;

    private WikiSearchUiState? _lastResults;
    private readonly List<string> _history = new();
    private string? _currentPage;

    public WikiSearchFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        _search = new LineEdit { PlaceHolder = Loc.GetString("wiki-search-placeholder"), HorizontalExpand = true };
        _search.OnTextChanged += args =>
        {
            var query = args.Text.Trim();
            if (query.Length == 1)
                return;

            OnSearch?.Invoke(query);
        };
        AddChild(_search);

        _results = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        _resultsScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            Children = { _results },
        };
        AddChild(_resultsScroll);

        _backButton = new Button { Text = Loc.GetString("wiki-book-back"), HorizontalExpand = true };
        _backButton.OnPressed += _ => GoBack();

        _status = new Label { Visible = false };
        _view = new WikiDocumentView();
        _links = new BoxContainer { Orientation = LayoutOrientation.Vertical };

        _articlePanel = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
        };
        _articlePanel.AddChild(_backButton);
        _articlePanel.AddChild(_status);
        _articlePanel.AddChild(_view);
        _articlePanel.AddChild(new Label { Text = Loc.GetString("wiki-book-links"), Margin = new Thickness(0, 4, 0, 0) });
        _articlePanel.AddChild(new ScrollContainer
        {
            MinSize = new Vector2(0, 70),
            HScrollEnabled = false,
            Children = { _links },
        });
        AddChild(_articlePanel);
    }

    public void ShowResults(WikiSearchUiState state)
    {
        _lastResults = state;
        _history.Clear();
        ShowResultsMode();
    }

    private void ShowResultsMode()
    {
        _articlePanel.Visible = false;
        _resultsScroll.Visible = true;
        _results.RemoveAllChildren();

        var state = _lastResults;
        if (state == null)
            return;

        if (state.Results.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(state.Query))
                _results.AddChild(new Label { Text = Loc.GetString("wiki-search-empty"), Margin = new Thickness(6) });

            return;
        }

        foreach (var result in state.Results)
        {
            var text = result.Title;
            if (!string.IsNullOrWhiteSpace(result.Category))
                text += $"  [{result.Category}]";

            if (result.TextMatch)
                text += $"  ({Loc.GetString("wiki-search-text-match")})";

            var button = new Button { Text = text, ToolTip = result.Page, ClipText = true, HorizontalExpand = true };
            var page = result.Page;
            button.OnPressed += _ =>
            {
                _history.Clear();
                OnOpen?.Invoke(page);
            };
            _results.AddChild(button);
        }
    }

    public void ShowArticle(WikiArticleUiState state)
    {
        _currentPage = state.Page;
        _resultsScroll.Visible = false;
        _articlePanel.Visible = true;
        _links.RemoveAllChildren();

        switch (state.Status)
        {
            case WikiLoadStatus.Loading:
                _status.Visible = false;
                _view.ShowMessage(Loc.GetString("wiki-book-loading"));
                return;
            case WikiLoadStatus.NotFound:
                _status.Visible = false;
                _view.ShowMessage(Loc.GetString("wiki-book-not-found"));
                return;
            case WikiLoadStatus.Disabled:
                _status.Visible = false;
                _view.ShowMessage(Loc.GetString("wiki-book-disabled"));
                return;
            case WikiLoadStatus.Error:
                _status.Visible = false;
                _view.ShowMessage(Loc.GetString("wiki-book-error"));
                return;
        }

        if (state.Document == null)
        {
            _status.Visible = false;
            _view.ShowMessage(Loc.GetString("wiki-book-empty"));
            return;
        }

        if (state.Status == WikiLoadStatus.Stale)
        {
            _status.Visible = true;
            _status.Text = Loc.GetString("wiki-book-stale");
        }
        else if (state.Specialized)
        {
            _status.Visible = true;
            _status.Text = Loc.GetString("wiki-book-specialized");
        }
        else
        {
            _status.Visible = false;
        }

        _view.Show(state.Document);

        foreach (var (page, label) in _view.Links)
        {
            var button = new Button { Text = label, ToolTip = page, ClipText = true };
            button.OnPressed += _ => Navigate(page);
            _links.AddChild(button);
        }
    }

    private void Navigate(string page)
    {
        if (_currentPage != null && (_history.Count == 0 || _history[^1] != _currentPage))
            _history.Add(_currentPage);

        OnOpen?.Invoke(page);
    }

    private void GoBack()
    {
        if (_history.Count == 0)
        {
            ShowResultsMode();
            return;
        }

        var previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        OnOpen?.Invoke(previous);
    }
}
