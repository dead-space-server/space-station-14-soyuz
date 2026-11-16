// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace._Soyuz.MilitiaOperations;

public sealed class MilitiaCustodyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private MilitiaCustodyWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MilitiaCustodyWindow>();
        _window.OnRequest += request => SendMessage(request);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MilitiaCustodyState custody)
            _window?.UpdateState(custody);
    }
}

public sealed class MilitiaCustodyWindow : FancyWindow
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private static readonly Color Ink = Color.FromHex("#EADCE0");
    private static readonly Color Muted = Color.FromHex("#BFA7AE");
    private static readonly Color Accent = Color.FromHex("#E16E79");
    private readonly BoxContainer _entries = Column();
    private readonly Label _balance = new();
    private readonly Label _count = new();
    private readonly Label _total = new();
    private readonly Label _status = new();
    private readonly Button _insert = new();
    private readonly Button _submit = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 3, MinHeight = 6 };
    private MilitiaCustodyState? _state;
    private TimeSpan? _displayDeadline;
    private string _rendered = string.Empty;
    public event Action<MilitiaCustodyRequest>? OnRequest;

    public MilitiaCustodyWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("militia-custody-title");
        MinSize = new Vector2(620, 480);
        SetSize = new Vector2(740, 620);
        var root = Column();
        root.Margin = new Thickness(14);
        var header = new BoxContainer { SeparationOverride = 14 };
        var icon = new EntityPrototypeView { SetSize = new Vector2(52), Stretch = SpriteView.StretchMode.Fit };
        icon.SetPrototype("MilitiaCustodyCabinet");
        header.AddChild(icon);
        var heading = Column();
        var title = Text(Loc.GetString("militia-custody-title"), Accent);
        title.StyleClasses.Add("LabelHeading");
        heading.AddChild(title);
        heading.AddChild(Text(Loc.GetString("militia-custody-subtitle"), Muted));
        header.AddChild(heading);
        root.AddChild(Surface(header));
        _balance.FontColorOverride = Ink;
        root.AddChild(Surface(_balance));
        _count.FontColorOverride = Muted;
        root.AddChild(_count);
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        scroll.AddChild(_entries);
        root.AddChild(scroll);
        var footer = Column();
        _total.FontColorOverride = Accent;
        footer.AddChild(_total);
        footer.AddChild(Text(Loc.GetString("militia-custody-ammo-notice"), Muted));
        _status.FontColorOverride = Ink;
        footer.AddChild(_status);
        _progress.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Accent };
        _progress.BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#150F15") };
        footer.AddChild(_progress);
        var actions = new BoxContainer { SeparationOverride = 10 };
        _insert.Text = Loc.GetString("militia-custody-insert");
        _submit.Text = Loc.GetString("militia-custody-submit");
        _insert.HorizontalExpand = _submit.HorizontalExpand = true;
        _insert.MinHeight = _submit.MinHeight = 42;
        _insert.OnPressed += _ => Request(MilitiaCustodyAction.Insert);
        _submit.OnPressed += _ => Request(MilitiaCustodyAction.Submit);
        actions.AddChild(_insert);
        actions.AddChild(_submit);
        footer.AddChild(actions);
        root.AddChild(Surface(footer));
        ContentsContainer.AddChild(new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1E151C") },
            Children = { root },
        });
    }

    private void Request(MilitiaCustodyAction action, NetEntity? item = null)
    {
        if (_state == null || _state.Busy)
            return;
        OnRequest?.Invoke(new MilitiaCustodyRequest(action, _state.Token, item));
    }

    public void UpdateState(MilitiaCustodyState state)
    {
        _state = state;
        _balance.Text = Loc.GetString("militia-custody-balance", ("points", state.Points), ("reputation", state.Reputation));
        _count.Text = Loc.GetString("militia-custody-count", ("count", state.Entries.Count), ("capacity", state.Capacity));
        _total.Text = Loc.GetString("militia-custody-total", ("points", state.Entries.Sum(entry => entry.Points)),
            ("reputation", state.Entries.Sum(entry => entry.Reputation)));
        _status.Text = Loc.GetString(state.Status);
        _progress.Visible = state.Busy;
        if (state.Deadline != _displayDeadline)
        {
            _displayDeadline = state.Deadline;
            if (state.Deadline is { } end)
                _progress.MaxValue = Math.Max(0.1f, (float) (end - _timing.CurTime).TotalSeconds);
        }
        _insert.Disabled = state.Busy || state.Entries.Count >= state.Capacity;
        _submit.Disabled = state.Busy || !state.Online || !state.Entries.Any(entry => entry.Reason.Length == 0);
        var key = $"{state.Token}:{state.Busy}";
        if (key == _rendered)
            return;
        _rendered = key;
        _entries.RemoveAllChildren();
        if (state.Entries.Count == 0)
            _entries.AddChild(Surface(Text(Loc.GetString("militia-custody-empty"), Muted)));
        foreach (var entry in state.Entries)
        {
            var row = new BoxContainer { SeparationOverride = 12 };
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
            {
                var icon = new EntityPrototypeView { SetSize = new Vector2(42), Stretch = SpriteView.StretchMode.Fit };
                icon.SetPrototype(entry.Prototype);
                row.AddChild(icon);
            }
            var details = Column();
            details.HorizontalExpand = true;
            var name = Text(entry.Name, Ink);
            name.ClipText = true;
            name.ToolTip = entry.Name;
            details.AddChild(name);
            details.AddChild(Text(entry.Reason.Length == 0
                ? Loc.GetString("militia-custody-value", ("points", entry.Points), ("reputation", entry.Reputation))
                : Loc.GetString(entry.Reason), entry.Reason.Length == 0 ? Accent : Muted));
            row.AddChild(details);
            var eject = new Button { Text = Loc.GetString("militia-custody-eject"), Disabled = state.Busy, MinWidth = 90 };
            eject.OnPressed += _ => Request(MilitiaCustodyAction.Eject, entry.Item);
            row.AddChild(eject);
            _entries.AddChild(Surface(row));
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_state?.Deadline is { } deadline)
            _progress.Value = Math.Clamp(_progress.MaxValue - (float) (deadline - _timing.CurTime).TotalSeconds, 0f, _progress.MaxValue);
    }

    private static BoxContainer Column() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
    private static Label Text(string value, Color color) => new() { Text = value, FontColorOverride = color };
    private static PanelContainer Surface(Control child) => new()
    {
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#2D2028") },
        Margin = new Thickness(0, 2),
        Children = { new BoxContainer { Margin = new Thickness(10), Children = { child } } },
    };
}
