// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.Radio;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.Radio;

[UsedImplicitly]
public sealed class TunableRadioBoundUserInterface : BoundUserInterface
{
    private TunableRadioWindow? _window;

    public TunableRadioBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TunableRadioWindow>();
        _window.OnFrequency += frequency => SendMessage(new TunableRadioSetFrequencyMessage(frequency));
        _window.OnToggle += (microphone, enabled) => SendMessage(new TunableRadioToggleMessage(microphone, enabled));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TunableRadioUiState radio)
            _window?.Update(radio);
    }
}

public sealed class TunableRadioWindow : FancyWindow
{
    private readonly SpinBox _frequency;
    private readonly Button _microphone;
    private readonly Button _speaker;
    private int? _lastFrequency;

    public event Action<int>? OnFrequency;
    public event Action<bool, bool>? OnToggle;

    public TunableRadioWindow()
    {
        Title = Loc.GetString("soyuz-radio-title");
        MinSize = new Vector2(330, 170);
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(10),
        };
        content.AddChild(new Label { Text = Loc.GetString("soyuz-radio-frequency") });
        var row = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true };
        _frequency = new SpinBox
        {
            HorizontalExpand = true,
            IsValid = frequency => frequency is >= TunableRadioComponent.MinimumFrequency and <= TunableRadioComponent.MaximumFrequency,
        };
        _frequency.OverrideValue(1330);
        _frequency.InitDefaultButtons();
        _frequency.LineEditControl.IsValid = text => text.Length == 0 ||
            int.TryParse(text, out var frequency) && frequency is >= 0 and <= TunableRadioComponent.MaximumFrequency;
        row.AddChild(_frequency);
        var apply = new Button { Text = Loc.GetString("soyuz-radio-apply") };
        _frequency.LineEditControl.OnTextChanged += _ => apply.Disabled =
            !int.TryParse(_frequency.LineEditControl.Text, out var frequency) ||
            frequency is < TunableRadioComponent.MinimumFrequency or > TunableRadioComponent.MaximumFrequency;
        apply.OnPressed += _ => OnFrequency?.Invoke(_frequency.Value);
        row.AddChild(apply);
        content.AddChild(row);
        _microphone = new Button { Text = Loc.GetString("soyuz-radio-microphone"), ToggleMode = true };
        _microphone.OnPressed += _ => OnToggle?.Invoke(true, _microphone.Pressed);
        content.AddChild(_microphone);
        _speaker = new Button { Text = Loc.GetString("soyuz-radio-speaker"), ToggleMode = true };
        _speaker.OnPressed += _ => OnToggle?.Invoke(false, _speaker.Pressed);
        content.AddChild(_speaker);
        ContentsContainer.AddChild(content);
    }

    public void Update(TunableRadioUiState state)
    {
        if (_lastFrequency != state.Frequency)
        {
            _frequency.OverrideValue(state.Frequency);
            _lastFrequency = state.Frequency;
        }
        _microphone.Pressed = state.MicrophoneEnabled;
        _speaker.Pressed = state.SpeakerEnabled;
    }
}
