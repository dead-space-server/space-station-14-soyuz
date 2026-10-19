// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeadSpace._Soyuz.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace._Soyuz.CartridgeLoader;

public sealed partial class TimerCartridgeUi : UIFragment
{
    private TimerCartridgeControl? _control;

    public override Control GetUIFragmentRoot() => _control!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        if (_control == null || _control.Disposed)
            _control = new TimerCartridgeControl();

        _control.Send = message => userInterface.SendMessage(new CartridgeUiMessage(message));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is TimerCartridgeState timer)
            _control?.ApplyState(timer);
    }
}

public sealed class TimerCartridgeControl : BoxContainer
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public Action<TimerCartridgeMessage>? Send;
    private readonly SpinBox _hours;
    private readonly SpinBox _minutes;
    private readonly SpinBox _seconds;
    private readonly LineEdit _name;
    private readonly Label _countdown;
    private readonly Label _idleHint;
    private readonly Button _start;
    private readonly Button _pause;
    private readonly Button _reset;
    private TimerCartridgeState? _state;

    public TimerCartridgeControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        Margin = new Thickness(8);
        HorizontalExpand = true;

        _countdown = new Label { Text = "00:05:00" };
        _countdown.AddStyleClass("LabelHeading");
        AddChild(_countdown);
        _idleHint = new Label { Text = Loc.GetString("timer-cartridge-status-idle") };
        AddChild(_idleHint);

        var duration = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _hours = AddTimeField(duration, "timer-cartridge-hours", 24);
        _minutes = AddTimeField(duration, "timer-cartridge-minutes", 59);
        _seconds = AddTimeField(duration, "timer-cartridge-seconds", 59);
        _minutes.OverrideValue(5);
        AddChild(duration);

        _name = new LineEdit
        {
            PlaceHolder = Loc.GetString("timer-cartridge-name-placeholder"),
            IsValid = text => text.Length <= TimerCartridgeState.MaxLabelLength,
        };
        AddChild(_name);

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _start = new Button { Text = Loc.GetString("timer-cartridge-start"), HorizontalExpand = true, Disabled = true };
        _pause = new Button { Text = Loc.GetString("timer-cartridge-pause"), HorizontalExpand = true, Disabled = true };
        _reset = new Button { Text = Loc.GetString("timer-cartridge-reset"), HorizontalExpand = true, Disabled = true };
        buttons.AddChild(_start);
        buttons.AddChild(_pause);
        buttons.AddChild(_reset);
        AddChild(buttons);

        _start.OnPressed += _ => SendAction(PdaTimerAction.Start);
        _pause.OnPressed += _ => SendAction(_state?.Status == PdaTimerStatus.Paused
            ? PdaTimerAction.Resume : PdaTimerAction.Pause);
        _reset.OnPressed += _ => SendAction(PdaTimerAction.Reset);
        _hours.ValueChanged += _ => UpdateDisplay();
        _minutes.ValueChanged += _ => UpdateDisplay();
        _seconds.ValueChanged += _ => UpdateDisplay();
    }

    private static SpinBox AddTimeField(BoxContainer parent, string label, int max)
    {
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        column.AddChild(new Label { Text = Loc.GetString(label) });
        var input = new SpinBox { IsValid = value => value >= 0 && value <= max, HorizontalExpand = true };
        input.InitDefaultButtons();
        column.AddChild(input);
        parent.AddChild(column);
        return input;
    }

    public void ApplyState(TimerCartridgeState state)
    {
        _state = state;
        _hours.OverrideValue(state.DurationSeconds / 3600);
        _minutes.OverrideValue(state.DurationSeconds / 60 % 60);
        _seconds.OverrideValue(state.DurationSeconds % 60);
        _name.Text = state.Label;

        var active = state.Status is PdaTimerStatus.Running or PdaTimerStatus.Paused;
        foreach (var input in new[] { _hours, _minutes, _seconds })
        {
            input.LineEditDisabled = active;
            input.SetButtonDisabled(active);
        }
        _name.Editable = !active;
        _pause.Disabled = !active;
        _pause.Text = Loc.GetString(state.Status == PdaTimerStatus.Paused
            ? "timer-cartridge-resume" : "timer-cartridge-pause");
        _reset.Disabled = state.Status == PdaTimerStatus.Idle;
        _idleHint.Visible = state.Status == PdaTimerStatus.Idle;
        UpdateDisplay();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateDisplay();
    }

    private int SelectedSeconds => _hours.Value * 3600 + _minutes.Value * 60 + _seconds.Value;

    private void UpdateDisplay()
    {
        var active = _state?.Status is PdaTimerStatus.Running or PdaTimerStatus.Paused;
        _start.Disabled = _state == null || active || SelectedSeconds is < 1 or > TimerCartridgeState.MaxDurationSeconds;
        var remaining = _state switch
        {
            { Status: PdaTimerStatus.Running } timer => (timer.EndsAt - _timing.CurTime).TotalSeconds,
            { Status: PdaTimerStatus.Paused } timer => timer.Remaining.TotalSeconds,
            { Status: PdaTimerStatus.Finished } => 0,
            _ => SelectedSeconds,
        };
        var seconds = (int) Math.Max(0, Math.Ceiling(remaining));
        _countdown.Text = $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    private void SendAction(PdaTimerAction action)
    {
        if (_state != null)
            Send?.Invoke(new TimerCartridgeMessage(action, SelectedSeconds, _name.Text, _state.Revision));
    }
}
