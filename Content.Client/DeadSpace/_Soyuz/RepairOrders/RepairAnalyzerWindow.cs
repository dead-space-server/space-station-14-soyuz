// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.RepairOrders;

public sealed class RepairAnalyzerWindow : FancyWindow
{
    private readonly BoxContainer _tasks;
    private readonly Label _target;
    private readonly bool _navigation;
    private string _signature = string.Empty;
    public event Action<RepairAnalyzerLayer>? OnLayer;
    public event Action<string?>? OnGroup;
    public event Action<int>? OnStep;

    public RepairAnalyzerWindow(bool navigation, RepairAnalyzerLayer layer)
    {
        _navigation = navigation;
        Title = Loc.GetString(navigation ? "repair-restorer-window-title" : "repair-analyzer-layers-title");
        MinSize = new Vector2(360, 180);
        SetSize = new Vector2(440, navigation ? 480 : 180);
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        var layers = new OptionButton { HorizontalExpand = true };
        foreach (var value in Enum.GetValues<RepairAnalyzerLayer>())
            layers.AddItem(Loc.GetString($"repair-analyzer-layer-{value}"), (int) value);
        layers.SelectId((int) layer);
        layers.OnItemSelected += args =>
        {
            layers.SelectId(args.Id);
            OnLayer?.Invoke((RepairAnalyzerLayer) args.Id);
        };
        content.AddChild(layers);
        _target = new Label { HorizontalExpand = true, ClipText = true, Visible = navigation };
        content.AddChild(_target);
        var controls = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Visible = navigation };
        var all = new Button { Text = Loc.GetString("repair-restorer-all"), HorizontalExpand = true };
        all.OnPressed += _ => OnGroup?.Invoke(null);
        controls.AddChild(all);
        var previous = new Button { Text = "◀" };
        previous.OnPressed += _ => OnStep?.Invoke(-1);
        controls.AddChild(previous);
        var next = new Button { Text = "▶" };
        next.OnPressed += _ => OnStep?.Invoke(1);
        controls.AddChild(next);
        content.AddChild(controls);
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true, Visible = navigation };
        _tasks = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 3 };
        scroll.AddChild(_tasks);
        content.AddChild(scroll);
        ContentsContainer.AddChild(content);
    }

    public static string GroupKey(RepairAnalyzerTaskData task)
        => $"{task.Type}:{task.Layer}:{task.ExpectedPrototype}";

    public void UpdateTasks(IReadOnlyList<RepairAnalyzerTaskData> tasks, Func<RepairAnalyzerTaskData, string> displayName, string? selected)
    {
        if (!_navigation)
            return;
        var groups = tasks.GroupBy(GroupKey).OrderBy(group => displayName(group.First()), StringComparer.CurrentCulture).ToArray();
        var signature = selected + string.Join(";", groups.Select(group => $"{group.Key}={group.Count()}"));
        if (_signature == signature)
            return;
        _signature = signature;
        _tasks.RemoveAllChildren();
        if (groups.Length == 0)
            _tasks.AddChild(new Label { Text = Loc.GetString("repair-restorer-no-tasks") });
        foreach (var group in groups)
        {
            var key = group.Key;
            var button = new Button
            {
                Text = Loc.GetString("repair-restorer-group", ("name", displayName(group.First())), ("count", group.Count())),
                HorizontalExpand = true,
                Pressed = key == selected,
                ToggleMode = true,
                ToolTip = displayName(group.First()),
            };
            button.Label.ClipText = true;
            button.OnPressed += _ =>
            {
                _signature = string.Empty;
                OnGroup?.Invoke(key);
            };
            _tasks.AddChild(button);
        }
    }

    public void SetTarget(string text)
    {
        _target.Text = text;
        _target.ToolTip = text;
    }
}
