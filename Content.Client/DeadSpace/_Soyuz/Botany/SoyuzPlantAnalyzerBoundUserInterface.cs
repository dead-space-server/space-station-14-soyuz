using Content.Shared.DeadSpace._Soyuz.Botany;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace._Soyuz.Botany;

[UsedImplicitly]
public sealed class SoyuzPlantAnalyzerBoundUserInterface : BoundUserInterface
{
    private SoyuzPlantAnalyzerWindow? _window;

    public SoyuzPlantAnalyzerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window ??= this.CreateWindow<SoyuzPlantAnalyzerWindow>();
        _window.OnAdvancedModeChanged -= OnAdvancedModeChanged;
        _window.OnAdvancedModeChanged += OnAdvancedModeChanged;
    }

    private void OnAdvancedModeChanged(bool advanced)
    {
        SendMessage(new SoyuzPlantAnalyzerSetMode(advanced));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is SoyuzPlantAnalyzerReport report)
            _window?.Populate(report);
    }
}
