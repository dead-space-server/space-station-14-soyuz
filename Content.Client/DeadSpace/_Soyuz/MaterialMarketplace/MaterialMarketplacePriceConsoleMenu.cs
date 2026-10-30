// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.MaterialMarketplace;

public sealed class MaterialMarketplacePriceConsoleMenu : FancyWindow
{
    private readonly LineEdit _searchBar;
    private readonly BoxContainer _list;
    private readonly Label _dynamicLabel;
    private readonly Button _dynamicButton;

    private MaterialMarketplacePriceConsoleState? _state;
    private string _searchFilter = string.Empty;

    /// <summary>
    /// isItem, id, price (null - сбросить).
    /// </summary>
    public event Action<bool, string, double?>? OnSetPrice;

    /// <summary>
    /// true - включить динамическую цену, false - выключить.
    /// </summary>
    public event Action<bool>? OnSetDynamicPricing;

    public MaterialMarketplacePriceConsoleMenu()
    {
        Title = "Консоль цен торгомата";
        SetSize = new Vector2(860, 700);
        Resizable = false;

        var main = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true,
            Margin = new Thickness(10, 30, 10, 10),
        };

        var dynamicRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 10,
            Margin = new Thickness(0, 0, 0, 10),
        };

        _dynamicLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        _dynamicButton = new Button { MinWidth = 260 };
        _dynamicButton.OnPressed += _ =>
        {
            if (_state != null)
                OnSetDynamicPricing?.Invoke(!_state.DynamicPricing);
        };

        dynamicRow.AddChild(_dynamicLabel);
        dynamicRow.AddChild(_dynamicButton);
        main.AddChild(dynamicRow);

        _searchBar = new LineEdit
        {
            PlaceHolder = "Поиск...",
            HorizontalExpand = true,
        };
        _searchBar.OnTextChanged += args =>
        {
            _searchFilter = args.Text.Trim().ToLowerInvariant();
            Rebuild();
        };
        main.AddChild(_searchBar);

        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
            Margin = new Thickness(0, 10, 0, 0),
        };

        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        scroll.AddChild(_list);
        main.AddChild(scroll);
        AddChild(main);
    }

    public void UpdateState(MaterialMarketplacePriceConsoleState state)
    {
        _state = state;
        Rebuild();
    }

    private void Rebuild()
    {
        _list.RemoveAllChildren();
        if (_state == null)
            return;

        if (_state.DynamicPricing)
        {
            _dynamicLabel.Text = "Динамическая цена: включена (от 50% до 200% от базовой)";
            _dynamicLabel.FontColorOverride = Color.LightGreen;
            _dynamicButton.Text = "Выключить динамическую цену";
        }
        else
        {
            _dynamicLabel.Text = "Динамическая цена: выключена";
            _dynamicLabel.FontColorOverride = Color.Orange;
            _dynamicButton.Text = "Включить динамическую цену";
        }

        AddSection("Материалы (цена за единицу товара)", false);
        AddSection("Предметы (цена за штуку)", true);
    }

    private void AddSection(string title, bool items)
    {
        var entries = _state!.Entries
            .Where(e => e.IsItem == items)
            .Where(e => string.IsNullOrEmpty(_searchFilter) || e.Name.ToLowerInvariant().Contains(_searchFilter))
            .OrderBy(e => e.Name)
            .ToList();

        if (entries.Count == 0)
            return;

        var label = new Label
        {
            Text = title.ToUpper(),
            FontColorOverride = Color.White,
            Margin = new Thickness(4, 10, 0, 4),
        };
        label.StyleClasses.Add("LabelHeading");
        _list.AddChild(label);

        foreach (var entry in entries)
        {
            _list.AddChild(CreateRow(entry));
        }
    }

    private Control CreateRow(MarketplacePriceEntry entry)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };

        row.AddChild(new Label
        {
            Text = entry.Name,
            HorizontalExpand = true,
            ClipText = true,
        });

        string info;
        Color infoColor;
        if (entry.CustomPrice is { } custom)
        {
            info = $"{custom:0.##}¢";
            infoColor = Color.LightGreen;
        }
        else if (entry.DefaultPrice is { } def)
        {
            info = $"{def:0.##}¢ (по умолч.)";
            infoColor = Color.LightGray;
        }
        else
        {
            info = "не выставлена";
            infoColor = Color.Orange;
        }

        row.AddChild(new Label
        {
            Text = info,
            FontColorOverride = infoColor,
            MinWidth = 140,
        });

        var showMultiplier = _state!.DynamicPricing && !entry.IsItem;
        var multiplierColor = entry.Multiplier > 1.005
            ? Color.OrangeRed
            : entry.Multiplier < 0.995 ? Color.LightSkyBlue : Color.LightGray;

        row.AddChild(new Label
        {
            Text = showMultiplier ? $"{entry.Multiplier * 100:0}%" : string.Empty,
            FontColorOverride = multiplierColor,
            MinWidth = 50,
            HorizontalAlignment = HAlignment.Right,
        });

        var edit = new LineEdit
        {
            MinWidth = 90,
            Text = (entry.CustomPrice ?? entry.DefaultPrice)?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
        };
        row.AddChild(edit);

        var set = new Button { Text = "Задать" };
        set.OnPressed += _ =>
        {
            var text = edit.Text.Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                OnSetPrice?.Invoke(entry.IsItem, entry.Id, value);
        };
        row.AddChild(set);

        var reset = new Button
        {
            Text = "Сброс",
            Disabled = entry.CustomPrice == null,
        };
        reset.OnPressed += _ => OnSetPrice?.Invoke(entry.IsItem, entry.Id, null);
        row.AddChild(reset);

        return row;
    }
}
