// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Client.Inventory;
using Content.Client.Lobby;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace._Soyuz.GhostBar;

public sealed class GhostBarWindow : DefaultWindow
{
    public event Action? OnConfirm;
    public event Action<string>? OnCostumeToggle;

    private readonly OptionButton _categorySelector;
    private readonly BoxContainer _costumesContainer;
    private readonly SpriteView _previewView;

    private GhostBarEuiState? _state;
    private string _activeCategory = string.Empty;
    private readonly List<string> _categoryIds = new();

    private EntityUid? _previewDummy;
    private readonly List<EntityUid> _previewItems = new();

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;

    public GhostBarWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("ghost-bar-window-title");
        MinSize = new Vector2(560, 520);
        SetSize = new Vector2(560, 520);

        var outer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        outer.AddChild(new Label
        {
            Text = Loc.GetString("ghost-bar-window-description"),
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8),
        });

        _categorySelector = new OptionButton
        {
            HorizontalExpand = true,
            Margin = new Thickness(4, 0, 4, 4),
        };
        _categorySelector.OnItemSelected += args =>
        {
            _categorySelector.SelectId(args.Id);

            if (args.Id < 0 || args.Id >= _categoryIds.Count)
                return;

            _activeCategory = _categoryIds[args.Id];
            RebuildCostumeList();
        };
        outer.AddChild(_categorySelector);

        var bodyRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8,
        };

        _costumesContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 4,
        };

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        scroll.AddChild(_costumesContainer);
        bodyRow.AddChild(scroll);

        _previewView = new SpriteView
        {
            MinSize = new Vector2(180, 240),
            SetSize = new Vector2(180, 240),
            OverrideDirection = Direction.South,
            Scale = new Vector2(2, 2),
            SpriteOffset = true,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Top,
        };
        bodyRow.AddChild(_previewView);

        outer.AddChild(bodyRow);

        var confirm = new Button
        {
            Text = Loc.GetString("ghost-bar-window-confirm-button"),
            Margin = new Thickness(8, 8),
            HorizontalAlignment = HAlignment.Center,
            MinSize = new Vector2(200, 36),
        };
        confirm.OnPressed += _ => OnConfirm?.Invoke();
        outer.AddChild(confirm);

        Contents.AddChild(outer);
    }

    public void UpdateState(GhostBarEuiState state)
    {
        _state = state;

        RebuildCategories();
        RebuildCostumeList();
        RebuildPreview();
    }

    private void RebuildCategories()
    {
        _categorySelector.Clear();
        _categoryIds.Clear();

        if (_state == null)
            return;

        var categories = _state.Costumes
            .Select(c => c.Category)
            .Distinct()
            .ToList();

        if (categories.Count == 0)
            return;

        if (string.IsNullOrEmpty(_activeCategory) || !categories.Contains(_activeCategory))
            _activeCategory = categories[0];

        var selectedId = 0;
        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            var label = Loc.GetString($"ghost-bar-costume-category-{category}");
            _categorySelector.AddItem(label);
            _categoryIds.Add(category);

            if (category == _activeCategory)
                selectedId = i;
        }

        _categorySelector.SelectId(selectedId);
    }

    private void RebuildCostumeList()
    {
        _costumesContainer.RemoveAllChildren();

        if (_state == null)
            return;

        foreach (var option in _state.Costumes)
        {
            if (option.Category != _activeCategory)
                continue;

            // Дефолтные предметы не показываем — они применяются автоматически
            if (option.Default)
                continue;

            var selected = _state.Selected.Contains(option.Id);
            var card = new GhostBarCostumeCard(option, selected);
            card.OnToggled += id => OnCostumeToggle?.Invoke(id);
            _costumesContainer.AddChild(card);
        }
    }

    private void RebuildPreview()
    {
        CleanupPreview();

        _previewView.SetEntity(null);

        if (_state == null)
            return;

        var profile = _preferencesManager.Preferences?.SelectedCharacter as HumanoidCharacterProfile;
        if (profile == null)
            return;

        if (!_prototypeManager.TryIndex<SpeciesPrototype>(profile.Species, out var species))
            return;

        var dummy = _entManager.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
        _previewDummy = dummy;

        _entManager.System<HumanoidAppearanceSystem>().LoadProfile(dummy, profile);

        var inventory = _entManager.System<ClientInventorySystem>();

        foreach (var option in _state.Costumes)
        {
            // В превью показываем и выбранные, и дефолтные
            var show = _state.Selected.Contains(option.Id) || option.Default;
            if (!show)
                continue;

            if (!_prototypeManager.HasIndex<EntityPrototype>(option.ItemPrototype))
                continue;

            var item = _entManager.SpawnEntity(option.ItemPrototype, MapCoordinates.Nullspace);
            _previewItems.Add(item);
            inventory.TryEquip(dummy, item, option.Slot, silent: true, force: true);
        }

        _previewView.SetEntity(dummy);
    }

    private void CleanupPreview()
    {
        foreach (var item in _previewItems)
        {
            if (_entManager.EntityExists(item))
                _entManager.DeleteEntity(item);
        }
        _previewItems.Clear();

        if (_previewDummy is { } dummy && _entManager.EntityExists(dummy))
            _entManager.DeleteEntity(dummy);
        _previewDummy = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        CleanupPreview();
    }
}