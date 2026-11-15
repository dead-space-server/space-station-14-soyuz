// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Client.Inventory;
using Content.Client.Lobby;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
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

    private static readonly Color PanelBg = Color.FromHex("#171a21");
    private static readonly Color PanelBorder = Color.FromHex("#323746");
    private static readonly Color PreviewBg = Color.FromHex("#14161c");
    private static readonly Color TextMuted = Color.FromHex("#8a90a3");
    private static readonly Color Confirm = Color.FromHex("#4caf7a");

    private readonly OptionButton _categorySelector;
    private readonly BoxContainer _costumesContainer;
    private readonly SpriteView _previewView;
    private readonly Label _emptyLabel;

    private GhostBarEuiState? _state;
    private string _activeCategory = string.Empty;
    private readonly List<string> _categoryIds = new();
    private readonly Dictionary<string, GhostBarCostumeCard> _cards = new();

    private EntityUid? _previewDummy;
    private readonly List<EntityUid> _previewItems = new();

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;

    public GhostBarWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("ghost-bar-window-title");
        MinSize = new Vector2(760, 560);
        SetSize = new Vector2(760, 560);

        var outer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(8),
        };

        outer.AddChild(new Label
        {
            Text = Loc.GetString("ghost-bar-window-description"),
            HorizontalAlignment = HAlignment.Center,
            FontColorOverride = TextMuted,
        });

        outer.AddChild(MakeSeparator());

        var bodyRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 10,
        };

        var listPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = MakePanelStyle(PanelBg, PanelBorder),
        };

        _costumesContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(6),
        };

        _emptyLabel = new Label
        {
            Text = Loc.GetString("ghost-bar-window-empty"),
            FontColorOverride = TextMuted,
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(0, 24),
            Visible = false,
        };

        var listContent = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        listContent.AddChild(_costumesContainer);
        listContent.AddChild(_emptyLabel);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        scroll.AddChild(listContent);
        listPanel.AddChild(scroll);
        bodyRow.AddChild(listPanel);

        var previewPanel = new PanelContainer
        {
            VerticalExpand = true,
            MinWidth = 220,
            PanelOverride = MakePanelStyle(PreviewBg, PanelBorder),
        };

        var previewBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };

        _categorySelector = new OptionButton
        {
            HorizontalExpand = true,
        };
        _categorySelector.OnItemSelected += args =>
        {
            _categorySelector.SelectId(args.Id);

            if (args.Id < 0 || args.Id >= _categoryIds.Count)
                return;

            _activeCategory = _categoryIds[args.Id];
            UpdateCostumeList(forceRebuild: true);
        };
        previewBox.AddChild(_categorySelector);

        previewBox.AddChild(MakeSeparator());

        _previewView = new SpriteView
        {
            MinSize = new Vector2(180, 240),
            SetSize = new Vector2(180, 240),
            OverrideDirection = Direction.South,
            Scale = new Vector2(2, 2),
            SpriteOffset = true,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            VerticalExpand = true,
        };
        previewBox.AddChild(_previewView);

        previewPanel.AddChild(previewBox);
        bodyRow.AddChild(previewPanel);

        outer.AddChild(bodyRow);

        outer.AddChild(MakeSeparator());

        var confirm = new Button
        {
            Text = Loc.GetString("ghost-bar-window-confirm-button"),
            MinSize = new Vector2(240, 38),
            HorizontalAlignment = HAlignment.Center,
            ModulateSelfOverride = Confirm,
        };
        confirm.OnPressed += _ => OnConfirm?.Invoke();
        outer.AddChild(confirm);

        Contents.AddChild(outer);
    }

    private static StyleBoxFlat MakePanelStyle(Color bg, Color border) => new()
    {
        BackgroundColor = bg,
        BorderColor = border,
        BorderThickness = new Thickness(1),
    };

    private static PanelContainer MakeSeparator() => new()
    {
        MinHeight = 1,
        HorizontalExpand = true,
        PanelOverride = new StyleBoxFlat { BackgroundColor = PanelBorder },
    };

    public void UpdateState(GhostBarEuiState state)
    {
        _state = state;

        RebuildCategories();
        UpdateCostumeList();
        RebuildPreview();
    }

    private void RebuildCategories()
    {
        if (_state == null)
            return;

        var categories = _state.Costumes
            .Where(c => !c.Default)
            .Select(c => c.Category)
            .Distinct()
            .ToList();

        if (categories.Count == 0)
        {
            _categorySelector.Clear();
            _categoryIds.Clear();
            return;
        }

        if (string.IsNullOrEmpty(_activeCategory) || !categories.Contains(_activeCategory))
            _activeCategory = categories[0];

        if (!_categoryIds.SequenceEqual(categories))
        {
            _categorySelector.Clear();
            _categoryIds.Clear();

            foreach (var category in categories)
            {
                _categorySelector.AddItem(Loc.GetString($"ghost-bar-costume-category-{category}"));
                _categoryIds.Add(category);
            }
        }

        var selectedId = _categoryIds.IndexOf(_activeCategory);
        if (selectedId >= 0)
            _categorySelector.SelectId(selectedId);
    }

    private void UpdateCostumeList(bool forceRebuild = false)
    {
        if (_state == null)
            return;

        var options = _state.Costumes
            .Where(o => o.Category == _activeCategory && !o.Default)
            .ToList();

        var sameSet = !forceRebuild
                      && _cards.Count == options.Count
                      && options.All(o => _cards.ContainsKey(o.Id));

        if (sameSet)
        {
            foreach (var option in options)
            {
                _cards[option.Id].SetSelected(_state.Selected.Contains(option.Id));
            }

            return;
        }

        _costumesContainer.RemoveAllChildren();
        _cards.Clear();

        foreach (var option in options)
        {
            var card = new GhostBarCostumeCard(option, _state.Selected.Contains(option.Id));
            card.OnToggled += id => OnCostumeToggle?.Invoke(id);
            _costumesContainer.AddChild(card);
            _cards[option.Id] = card;
        }

        _emptyLabel.Visible = options.Count == 0;
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
            var show = _state.Selected.Contains(option.Id) || option.Default;
            if (!show)
                continue;

            if (!_prototypeManager.HasIndex<EntityPrototype>(option.ClothingProto))
                continue;

            var item = _entManager.SpawnEntity(option.ClothingProto, MapCoordinates.Nullspace);
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