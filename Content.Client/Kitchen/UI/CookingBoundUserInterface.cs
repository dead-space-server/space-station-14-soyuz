using Content.Shared.Kitchen;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Kitchen.UI;

[UsedImplicitly]
public sealed class CookingBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CookingMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<CookingMenu>();
        _menu.RecipeSelected += (id, portions) => SendMessage(new CookingSelectRecipeMessage(id, portions));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is CookingUiState cooking && _menu != null)
            _menu.ApplyState(cooking);
    }
}
