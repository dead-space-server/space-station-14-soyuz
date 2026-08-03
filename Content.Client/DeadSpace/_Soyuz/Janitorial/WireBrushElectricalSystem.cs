// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Client.Items;
using Content.Client.Items.UI;
using Content.Client.Message;
using Content.Client.Stylesheets;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.Janitorial;

public sealed class WireBrushElectricalSystem : EntitySystem
{
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly ILocalizationManager _localization = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.ItemStatus<PowerCellSlotComponent>(CreateStatusControl);
    }

    private Control? CreateStatusControl(Entity<PowerCellSlotComponent> ent)
    {
        if (MetaData(ent).EntityPrototype?.ID != "WireBrushElectrical")
            return null;

        return new PowerCellStatusControl(ent, _powerCell, _battery, _localization);
    }

    private sealed class PowerCellStatusControl : PollingItemStatusControl<PowerCellStatusControl.Data>
    {
        private readonly Entity<PowerCellSlotComponent> _parent;
        private readonly PowerCellSystem _powerCell;
        private readonly SharedBatterySystem _battery;
        private readonly ILocalizationManager _localization;
        private readonly RichTextLabel _label;

        public PowerCellStatusControl(
            Entity<PowerCellSlotComponent> parent,
            PowerCellSystem powerCell,
            SharedBatterySystem battery,
            ILocalizationManager localization)
        {
            _parent = parent;
            _powerCell = powerCell;
            _battery = battery;
            _localization = localization;
            _label = new RichTextLabel { StyleClasses = { StyleClass.ItemStatus } };
            AddChild(_label);
        }

        protected override Data PollData()
        {
            if (!_powerCell.TryGetBatteryFromSlot(_parent.AsNullable(), out var battery))
                return new Data(false, 0f, 0);

            var chargeLevel = _battery.GetChargeLevel(battery.Value.AsNullable());
            var chargePercent = Math.Clamp((int) MathF.Round(chargeLevel * 100f), 0, 100);
            return new Data(true, chargeLevel, chargePercent);
        }

        protected override void Update(in Data data)
        {
            if (!data.HasBattery)
            {
                _label.SetMarkup(_localization.GetString("wire-brush-power-cell-status-empty"));
                return;
            }

            var color = data.ChargeLevel switch
            {
                <= 0.15f => "#d14c32",
                <= 0.5f => "#d7a72c",
                _ => "#8dc63f",
            };

            _label.SetMarkup(_localization.GetString("wire-brush-power-cell-status",
                ("color", color),
                ("charge", data.ChargePercent)));
        }

        public readonly record struct Data(bool HasBattery, float ChargeLevel, int ChargePercent);
    }
}
