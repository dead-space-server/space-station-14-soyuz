// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.Lockdown;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace._Soyuz.Lockdown;

/// <summary>
/// Окно консоли локдауна.
/// </summary>
public sealed class LockdownConsoleWindow : DefaultWindow
{
    public event Action<string>? OnStartDepartment;
    public event Action<string>? OnCancelDepartment;
    public event Action? OnConfirmStation;
    public event Action? OnCancelStation;

    private readonly BoxContainer _root;
    private readonly Label _consoleStatus;

    private readonly BoxContainer _departmentList;
    private readonly Dictionary<string, DepartmentRow> _departments = new();

    private readonly Label _stationStatus;
    private readonly BoxContainer _keyList;
    private readonly Dictionary<string, Label> _keys = new();
    private readonly Button _confirmStation;
    private readonly Button _cancelStation;

    private bool _built;

    public LockdownConsoleWindow()
    {
        Title = Loc.GetString("lockdown-ui-title");
        MinSize = new Vector2(460, 420);

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HScrollEnabled = false,
        };

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        scroll.AddChild(_root);
        Contents.AddChild(scroll);

        _consoleStatus = new Label();
        _root.AddChild(_consoleStatus);

        _root.AddChild(new Label { Text = Loc.GetString("lockdown-ui-departments") });
        _departmentList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };
        _root.AddChild(_departmentList);

        _root.AddChild(new Label { Text = Loc.GetString("lockdown-ui-station") });
        _stationStatus = new Label();
        _root.AddChild(_stationStatus);

        _keyList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        _root.AddChild(_keyList);

        var stationButtons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };

        _confirmStation = new Button
        {
            Text = Loc.GetString("lockdown-ui-confirm-station"),
            HorizontalExpand = true,
        };
        _confirmStation.OnPressed += _ => OnConfirmStation?.Invoke();

        _cancelStation = new Button
        {
            Text = Loc.GetString("lockdown-ui-cancel"),
            HorizontalExpand = true,
        };
        _cancelStation.OnPressed += _ => OnCancelStation?.Invoke();

        stationButtons.AddChild(_confirmStation);
        stationButtons.AddChild(_cancelStation);
        _root.AddChild(stationButtons);
    }

    public void UpdateState(LockdownConsoleBoundUserInterfaceState state)
    {
        if (!_built)
            Build(state);

        var now = state.CurTime;
        var consoleLocked = state.ConsoleLockedUntil > now;

        if (!state.HasStation)
        {
            _consoleStatus.Text = Loc.GetString("lockdown-ui-no-station");
            _consoleStatus.FontColorOverride = Color.Red;
        }
        else if (consoleLocked)
        {
            _consoleStatus.Text = Loc.GetString("lockdown-ui-console-locked", ("time", FormatTime(state.ConsoleLockedUntil - now)));
            _consoleStatus.FontColorOverride = Color.Orange;
        }
        else
        {
            _consoleStatus.Text = Loc.GetString("lockdown-ui-console-ready");
            _consoleStatus.FontColorOverride = Color.LimeGreen;
        }

        var stationBusy = state.Station.Phase != LockdownPhase.None;
        var canStart = state.HasStation && !consoleLocked && !stationBusy;

        foreach (var entry in state.Departments)
        {
            if (!_departments.TryGetValue(entry.Id, out var row))
                continue;

            row.Status.Text = PhaseText(entry, now);
            row.Start.Disabled = !canStart || entry.Phase != LockdownPhase.None;
            row.Cancel.Disabled = entry.Phase == LockdownPhase.None;
        }

        _stationStatus.Text = PhaseText(state.Station, now);

        foreach (var key in state.Keys)
        {
            if (!_keys.TryGetValue(key.Job, out var label))
                continue;

            if (key.Confirmed)
            {
                label.Text = Loc.GetString("lockdown-ui-key-confirmed",
                    ("job", key.JobName),
                    ("time", FormatTime(key.ExpiresAt - now)));
                label.FontColorOverride = Color.LimeGreen;
            }
            else
            {
                label.Text = Loc.GetString("lockdown-ui-key-waiting", ("job", key.JobName));
                label.FontColorOverride = Color.Gray;
            }
        }

        _confirmStation.Disabled = !canStart;
        _cancelStation.Disabled = !stationBusy;
    }

    private void Build(LockdownConsoleBoundUserInterfaceState state)
    {
        _built = true;

        foreach (var entry in state.Departments)
        {
            var row = new DepartmentRow(entry.Name);
            var id = entry.Id;

            row.Start.OnPressed += _ => OnStartDepartment?.Invoke(id);
            row.Cancel.OnPressed += _ => OnCancelDepartment?.Invoke(id);

            _departments[id] = row;
            _departmentList.AddChild(row.Root);
        }

        foreach (var key in state.Keys)
        {
            var label = new Label();
            _keys[key.Job] = label;
            _keyList.AddChild(label);
        }
    }

    private static string PhaseText(LockdownEntry entry, TimeSpan now)
    {
        var left = FormatTime(entry.PhaseEnd - now);

        return entry.Phase switch
        {
            LockdownPhase.Pending => Loc.GetString("lockdown-ui-phase-pending", ("time", left)),
            LockdownPhase.Active => Loc.GetString("lockdown-ui-phase-active", ("time", left)),
            _ => Loc.GetString("lockdown-ui-phase-none"),
        };
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return $"{(int) time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private sealed class DepartmentRow
    {
        public readonly BoxContainer Root;
        public readonly Label Status;
        public readonly Button Start;
        public readonly Button Cancel;

        public DepartmentRow(string name)
        {
            Root = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 6,
            };

            var title = new Label
            {
                Text = name,
                MinWidth = 120,
            };

            Status = new Label
            {
                HorizontalExpand = true,
            };

            Start = new Button { Text = Loc.GetString("lockdown-ui-start") };
            Cancel = new Button { Text = Loc.GetString("lockdown-ui-cancel") };

            Root.AddChild(title);
            Root.AddChild(Status);
            Root.AddChild(Start);
            Root.AddChild(Cancel);
        }
    }
}
