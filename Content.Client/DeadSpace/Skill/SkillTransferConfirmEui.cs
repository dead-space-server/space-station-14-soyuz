using Content.Client.Eui;
using Content.Shared.DeadSpace.Skills.Events;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using System.Numerics;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.DeadSpace.Skill;

[UsedImplicitly]
public sealed class SkillTransferConfirmEui : BaseEui
{
    private readonly SkillTransferConfirmWindow _window;
    private bool _responded;

    public SkillTransferConfirmEui()
    {
        _window = new SkillTransferConfirmWindow();

        _window.YesButton.OnPressed += _ => Respond(true);
        _window.NoButton.OnPressed += _ => Respond(false);
        _window.OnClose += () => Respond(false);
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not SkillTransferConfirmEuiState confirm)
            return;

        _window.Title = confirm.Title;
        _window.MessageLabel.Text = confirm.Message;
    }

    public override void Closed()
    {
        _window.Close();
    }

    private void Respond(bool accepted)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new SkillTransferConfirmResponseMessage(accepted));

        if (_window.IsOpen)
            _window.Close();
    }
}

public sealed class SkillTransferConfirmWindow : DefaultWindow
{
    public readonly Button YesButton;
    public readonly Button NoButton;
    public readonly Label MessageLabel;

    public SkillTransferConfirmWindow()
    {
        Title = Loc.GetString("skill-share-transfer-confirm-title");

        MessageLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center
        };

        Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Children =
                    {
                        MessageLabel,
                        new Control { MinSize = new Vector2(0, 20) },
                        new BoxContainer
                        {
                            Orientation = LayoutOrientation.Horizontal,
                            Align = AlignMode.Center,
                            Children =
                            {
                                (YesButton = new Button { Text = Loc.GetString("skill-share-yes") }),
                                new Control { MinSize = new Vector2(20, 0) },
                                (NoButton = new Button { Text = Loc.GetString("skill-share-no") })
                            }
                        }
                    }
                }
            }
        });
    }
}
