// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;
using Content.Shared.FixedPoint;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed class PharmaceuticalWindow : FancyWindow
{
    private static readonly Color Accent = Color.FromHex("#74E1CA");
    private readonly RichTextLabel _input = Text(string.Empty);
    private readonly RichTextLabel _output = Text(string.Empty);
    private readonly RichTextLabel _status = Text(string.Empty);
    private readonly RichTextLabel _analysis = Text(string.Empty);
    private readonly RichTextLabel _studiedDetail = Text(string.Empty);
    private readonly RichTextLabel _formulaDetail = Text(string.Empty);
    private readonly RichTextLabel _trialResults = Text(string.Empty);
    private readonly Label _operation = new() { FontColorOverride = Accent };
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, MinHeight = 7, HorizontalExpand = true };
    private readonly Button _cancel = new() { HorizontalAlignment = HAlignment.Right };
    private readonly Button _analyze;
    private readonly Button _synthesize;
    private readonly Button _assay;
    private readonly Button _purify;
    private readonly Button _register;
    private readonly Button _reproduce;
    private readonly Button _print;
    private readonly BoxContainer _sample = Vertical();
    private readonly BoxContainer _ingredients = Vertical();
    private readonly BoxContainer _formulas = Vertical();
    private readonly BoxContainer _studied = Vertical();
    private readonly BoxContainer _journal = Vertical();
    private readonly LineEdit _amount = new() { Text = "20.0", MinWidth = 110 };
    private readonly LineEdit _name = new() { HorizontalExpand = true };
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly List<Button> _actions = new();
    private readonly List<(CheckBox Control, bool Allowed)> _choices = new();
    private readonly HashSet<string> _selectedIngredients = new();
    private readonly HashSet<string> _seenIngredients = new();
    private PharmaceuticalState? _state;
    private string _sampleSignature = "\0";
    private string _formulaSignature = "\0";
    private string _studySignature = "\0";
    private string _journalSignature = string.Empty;
    private string _selectedFormula = string.Empty;
    private string _selectedStudy = string.Empty;
    public event Action<PharmaceuticalCommand>? OnCommand;

    public PharmaceuticalWindow()
    {
        Title = Loc.GetString("pharma-title");
        SetSize = new Vector2(1000, 740);
        MinSize = new Vector2(760, 520);
        var root = Vertical();
        root.Margin = new Thickness(10);
        root.VerticalExpand = true;
        var heading = Vertical();
        heading.AddChild(new Label { Text = Loc.GetString("pharma-title"), StyleClasses = { "LabelHeading" }, FontColorOverride = Accent });
        heading.AddChild(Text(Loc.GetString("pharma-subtitle")));
        root.AddChild(Card(heading, true));
        var ports = new BoxContainer { HorizontalExpand = true, SeparationOverride = 10 };
        var input = Vertical();
        input.AddChild(Heading("pharma-input"));
        input.AddChild(_input);
        input.AddChild(ActionButton("pharma-eject", PharmaceuticalAction.EjectInput));
        ports.AddChild(Card(input));
        var output = Vertical();
        output.AddChild(Heading("pharma-output"));
        output.AddChild(_output);
        output.AddChild(ActionButton("pharma-eject", PharmaceuticalAction.EjectOutput));
        ports.AddChild(Card(output));
        var batch = Vertical();
        batch.AddChild(Heading("pharma-amount"));
        batch.AddChild(_amount);
        ports.AddChild(Card(batch));
        root.AddChild(ports);
        var working = Vertical();
        working.AddChild(_operation);
        working.AddChild(_progress);
        working.AddChild(_status);
        _cancel.Text = Loc.GetString("pharma-cancel");
        _cancel.OnPressed += _ => Request(PharmaceuticalAction.Cancel);
        _cancel.Visible = false;
        working.AddChild(_cancel);
        root.AddChild(working);

        var tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true };
        var sample = Tab(tabs, "pharma-tab-sample");
        _analyze = ActionButton("pharma-analyze", PharmaceuticalAction.Analyze);
        sample.AddChild(_analyze);
        sample.AddChild(_sample);
        sample.AddChild(ActionButton("pharma-dispose", PharmaceuticalAction.Dispose));
        sample.AddChild(Card(_analysis));

        var studied = Tab(tabs, "pharma-tab-studied");
        _search.PlaceHolder = Loc.GetString("pharma-search");
        studied.AddChild(_search);
        studied.AddChild(_studied);
        studied.AddChild(Card(_studiedDetail));
        _search.OnTextChanged += _ => RebuildStudies();

        var synthesis = Tab(tabs, "pharma-tab-synthesis");
        synthesis.AddChild(Heading("pharma-composition"));
        synthesis.AddChild(_ingredients);
        _synthesize = ActionButton("pharma-synthesize", PharmaceuticalAction.Synthesize);
        synthesis.AddChild(_synthesize);

        var assay = Tab(tabs, "pharma-tab-assay");
        _assay = ActionButton("pharma-assay", PharmaceuticalAction.Assay);
        assay.AddChild(_assay);
        _purify = ActionButton("pharma-purify", PharmaceuticalAction.Separate);
        assay.AddChild(_purify);
        assay.AddChild(Card(_trialResults));
        _name.PlaceHolder = Loc.GetString("pharma-name-placeholder");
        assay.AddChild(Heading("pharma-drug-name"));
        assay.AddChild(_name);
        _register = ActionButton("pharma-register", PharmaceuticalAction.Register);
        assay.AddChild(_register);
        _name.OnTextChanged += _ => UpdateActions();

        var registry = Tab(tabs, "pharma-tab-registry");
        registry.AddChild(_formulas);
        registry.AddChild(Card(_formulaDetail));
        _reproduce = ActionButton("pharma-reproduce", PharmaceuticalAction.Reproduce);
        _print = ActionButton("pharma-print", PharmaceuticalAction.Print);
        registry.AddChild(_reproduce);
        registry.AddChild(_print);
        Tab(tabs, "pharma-tab-journal").AddChild(_journal);
        root.AddChild(tabs);
        ContentsContainer.AddChild(root);
        _amount.OnTextChanged += _ =>
        {
            ShowSelectedFormula();
            UpdateActions();
        };
    }

    public void UpdateState(PharmaceuticalState state)
    {
        _state = state;
        SetText(_input, state.Input);
        SetText(_output, state.Output);
        SetText(_status, state.Status);
        SetText(_analysis, state.Analysis);
        SetText(_trialResults, state.Sample.Length == 1 ? state.Sample[0].Properties : string.Empty);
        _operation.Text = !state.Powered ? Loc.GetString("pharma-offline") : state.Busy ? state.Operation : Loc.GetString("pharma-ready");
        _progress.Visible = state.Busy;
        _progress.Value = state.Progress;
        _cancel.Visible = state.Busy;
        var sampleSignature = string.Join("|", state.Sample.Select(r => r.Id + r.Name + r.Amount + r.CanUse + r.Analyzed));
        if (_sampleSignature != sampleSignature)
        {
            _sampleSignature = sampleSignature;
            _sample.RemoveAllChildren();
            _ingredients.RemoveAllChildren();
            _choices.Clear();
            _selectedIngredients.IntersectWith(state.Sample.Where(r => r.CanUse).Select(r => r.Id));
            foreach (var reagent in state.Sample)
            {
                _sample.AddChild(Card(Text(Loc.GetString("pharma-sample-row", ("name", reagent.Name), ("amount", Number(reagent.Amount))) + "\n" +
                    Loc.GetString(reagent.Analyzed ? "pharma-analyzed" : "pharma-not-analyzed"))));
                if (reagent.IsProduct)
                    continue;
                if (reagent.CanUse && !_seenIngredients.Contains(reagent.Id))
                    _selectedIngredients.Add(reagent.Id);
                var choice = new CheckBox
                {
                    Text = Loc.GetString("pharma-sample-row", ("name", reagent.Name), ("amount", Number(reagent.Amount))),
                    Pressed = _selectedIngredients.Contains(reagent.Id), HorizontalExpand = true,
                };
                choice.OnToggled += args =>
                {
                    if (args.Pressed) _selectedIngredients.Add(reagent.Id);
                    else _selectedIngredients.Remove(reagent.Id);
                    UpdateActions();
                };
                _choices.Add((choice, reagent.CanUse));
                _ingredients.AddChild(Card(choice));
            }
            _seenIngredients.Clear();
            _seenIngredients.UnionWith(state.Sample.Where(r => r.CanUse).Select(r => r.Id));
            if (state.Sample.Length == 0)
                _sample.AddChild(Text(Loc.GetString("pharma-slot-empty")));
        }
        var formulaSignature = string.Join("|", state.Formulas.Select(f => f.Id + f.Name + f.Findings));
        if (_formulaSignature != formulaSignature)
        {
            _formulaSignature = formulaSignature;
            _formulas.RemoveAllChildren();
            foreach (var formula in state.Formulas)
            {
                var select = new Button { Text = formula.Name, HorizontalExpand = true, MinHeight = 34, Modulate = formula.Tint };
                select.OnPressed += _ =>
                {
                    _selectedFormula = formula.Id;
                    ShowSelectedFormula();
                    UpdateActions();
                };
                _formulas.AddChild(Card(select));
            }
            if (state.Formulas.Length == 0)
                _formulas.AddChild(Text(Loc.GetString("pharma-registry-empty")));
            ShowSelectedFormula();
        }
        var studySignature = string.Join("|", state.Studied.Select(r => r.Id + r.Name + r.Properties));
        if (_studySignature != studySignature)
        {
            _studySignature = studySignature;
            RebuildStudies();
        }
        var journalSignature = string.Join("|", state.Journal);
        if (_journalSignature != journalSignature)
        {
            _journalSignature = journalSignature;
            _journal.RemoveAllChildren();
            foreach (var entry in state.Journal)
                _journal.AddChild(Card(Text(entry)));
        }
        UpdateActions();
    }

    private void UpdateActions()
    {
        if (_state == null)
            return;
        var blocked = _state.Busy || !_state.Powered;
        foreach (var action in _actions)
            action.Disabled = blocked;
        foreach (var (control, allowed) in _choices)
            control.Disabled = blocked || !allowed;
        var validAmount = TryAmount(out _);
        _analyze.Disabled = blocked || _state.Sample.Length == 0;
        _synthesize.Disabled = blocked || !validAmount || _selectedIngredients.Count < 2;
        _assay.Disabled = blocked || _state.Sample.Length != 1 || !_state.Sample[0].IsProduct || _state.Sample[0].Amount < 1;
        _purify.Disabled = blocked || _state.Sample.Length != 1 || !_state.Sample[0].CanPurify;
        _register.Disabled = blocked || string.IsNullOrWhiteSpace(_name.Text) || _name.Text.Length > 48;
        var chosen = _state.Formulas.Any(f => f.Id == _selectedFormula);
        _reproduce.Disabled = blocked || !chosen || !validAmount;
        _print.Disabled = blocked || !chosen;
    }

    private void ShowSelectedFormula()
    {
        if (_state?.Formulas.FirstOrDefault(f => f.Id == _selectedFormula) is not { } formula)
            return;
        var volume = TryAmount(out var amount) ? amount : 20f;
        var cents = FixedPoint2.New(volume).Value;
        var total = formula.Parts.Sum(p => (int) p.Amount);
        var parts = formula.Parts.Select(p =>
        {
            var raw = ((long) cents * (int) p.Amount + total - 1) / total;
            return Loc.GetString("pharma-recipe-row", ("name", p.Name), ("amount", Number((raw + 9) / 10 * 0.1f)));
        });
        SetText(_formulaDetail, formula.Name + "\n" + formula.Id + "\n" + formula.Author + "\n\n" + formula.Findings + "\n\n" +
            Loc.GetString("pharma-recipe-title", ("amount", Number(volume))) + "\n" + string.Join("\n", parts));
    }

    private void RebuildStudies()
    {
        _studied.RemoveAllChildren();
        if (_state == null)
            return;
        foreach (var reagent in _state.Studied.Where(r => r.Name.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            var button = new Button { Text = reagent.Name, HorizontalExpand = true, MinHeight = 32 };
            button.OnPressed += _ =>
            {
                _selectedStudy = reagent.Id;
                SetText(_studiedDetail, reagent.Name + "\n\n" + reagent.Description + "\n\n" + reagent.Properties);
            };
            _studied.AddChild(button);
        }
        if (_state.Studied.FirstOrDefault(r => r.Id == _selectedStudy) is { } chosen)
            SetText(_studiedDetail, chosen.Name + "\n\n" + chosen.Description + "\n\n" + chosen.Properties);
        if (_state.Studied.Length == 0)
            _studied.AddChild(Text(Loc.GetString("pharma-studies-empty")));
    }

    private bool TryAmount(out float amount)
    {
        if (!float.TryParse(_amount.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || !float.IsFinite(amount))
            return false;
        amount = MathF.Round(amount, 1);
        return amount >= 0.1f && amount <= 200f;
    }

    private void Request(PharmaceuticalAction action)
    {
        if (_state == null)
            return;
        var usesAmount = action is PharmaceuticalAction.Synthesize or PharmaceuticalAction.Reproduce;
        var amount = 20f;
        if (usesAmount && !TryAmount(out amount))
        {
            SetText(_status, Loc.GetString("pharma-error-number"));
            return;
        }
        if (usesAmount)
            _amount.Text = Number(amount);
        OnCommand?.Invoke(new PharmaceuticalCommand(action, _state.Revision, _selectedFormula, amount,
            action == PharmaceuticalAction.Register ? _name.Text : string.Empty,
            action == PharmaceuticalAction.Synthesize ? _selectedIngredients.ToArray() : null));
    }

    private Button ActionButton(string key, PharmaceuticalAction action)
    {
        var button = new Button { Text = Loc.GetString(key), HorizontalExpand = true, MinHeight = 34 };
        button.OnPressed += _ => Request(action);
        _actions.Add(button);
        return button;
    }

    private static string Number(float value) => MathF.Round(value, 1).ToString("0.0", CultureInfo.InvariantCulture);
    private static BoxContainer Vertical() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 7, HorizontalExpand = true };
    private static Label Heading(string key) => new() { Text = Loc.GetString(key), FontColorOverride = Accent };
    private static RichTextLabel Text(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        SetText(label, text);
        return label;
    }
    private static void SetText(RichTextLabel label, string text) => label.SetMessage(FormattedMessage.FromUnformatted(text));
    private static PanelContainer Card(Control child, bool highlighted = false) => new()
    {
        HorizontalExpand = true,
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(highlighted ? "#173A40" : "#202C3B"),
            BorderColor = highlighted ? Accent : Color.FromHex("#384D63"),
            BorderThickness = new Thickness(highlighted ? 2 : 1),
            ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 8, ContentMarginBottomOverride = 8,
        },
        Children = { child },
    };
    private static BoxContainer Tab(TabContainer tabs, string title)
    {
        var page = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(0, 10) };
        TabContainer.SetTabTitle(page, Loc.GetString(title));
        var content = Vertical();
        page.AddChild(content);
        tabs.AddChild(page);
        return content;
    }
}
