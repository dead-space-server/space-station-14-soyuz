// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderWindow
{
    private BoxContainer _specialOffers = default!;
    private BoxContainer _specialActive = default!;
    private BoxContainer _specialLast = default!;
    private Label _specialClock = default!;
    private readonly HashSet<int> _expandedSpecialOffers = new();

    private void InitializeSpecialContracts()
    {
        var page = Page(_tabs, "medical-special-tab");
        _specialClock = new Label { FontColorOverride = Color.LightBlue };
        page.AddChild(_specialClock);
        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        var content = Column();
        _specialActive = Section(content, "medical-orders-active");
        _specialLast = Section(content, "medical-orders-completed");
        _specialOffers = Section(content, "medical-orders-offers");
        scroll.AddChild(content);
        page.AddChild(scroll);
    }

    private void RebuildSpecialContracts()
    {
        if (_state == null)
            return;
        _specialOffers.RemoveAllChildren();
        _specialActive.RemoveAllChildren();
        _specialLast.RemoveAllChildren();
        var state = _state;
        _expandedSpecialOffers.RemoveWhere(id => !state.SpecialOffers.Any(o => o.RuntimeId == id));

        if (state.SpecialActive is { } active)
        {
            var content = SpecialCard(_specialActive, active, true);
            var action = state.Kind switch
            {
                MedicalOrderMachineKind.PatientReceiver => MedicalOrderAction.IssueSpecialPatient,
                MedicalOrderMachineKind.PatientSender => MedicalOrderAction.SubmitSpecialPatient,
                _ => MedicalOrderAction.SupplySpecialReagents,
            };
            var enabled = state.Kind switch
            {
                MedicalOrderMachineKind.PatientReceiver => active.CanIssue,
                MedicalOrderMachineKind.PatientSender => active.Patients.Any(p => p.CanSubmit),
                _ => active.Reagents.Any(r => r.Submitted < r.Required &&
                    state.Market.Any(m => m.Reagent == r.ID && m.Available > 0)),
            };
            if (state.Kind == MedicalOrderMachineKind.Reagent)
            {
                content.AddChild(Info(state.ReagentBeakerName is { } name
                    ? Loc.GetString("medical-orders-beaker-inserted", ("name", name))
                    : Loc.GetString("medical-orders-beaker-empty")));
                var beaker = new Button
                {
                    Text = Loc.GetString(state.ReagentBeakerName == null
                        ? "medical-orders-insert-beaker" : "medical-orders-eject-beaker"),
                };
                beaker.OnPressed += _ => OnBeakerSlot?.Invoke();
                content.AddChild(beaker);
            }
            else
                content.AddChild(WrappedText(Loc.GetString(state.Kind == MedicalOrderMachineKind.PatientReceiver
                    ? "medical-special-receiver-instructions" : "medical-special-sender-instructions")));

            var button = new Button
            {
                Text = Loc.GetString(state.Kind switch
                {
                    MedicalOrderMachineKind.PatientReceiver => "medical-special-issue-patient",
                    MedicalOrderMachineKind.PatientSender => "medical-special-submit-patient",
                    _ => "medical-special-supply",
                }),
                Disabled = !enabled || active.Deadline <= _timing.CurTime,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => OnRequest?.Invoke(new MedicalOrderRequestMessage(action, active.RuntimeId));
            content.AddChild(button);
        }
        else
            _specialActive.AddChild(Info(Loc.GetString("medical-orders-none")));

        if (state.LastSpecial is { } last)
            _specialLast.AddChild(WrappedText(Loc.GetString(last.Expired
                ? "medical-special-result-expired" : "medical-special-result-completed",
                ("title", Loc.GetString(last.Title, ("site", Loc.GetString(last.Site)))),
                ("points", last.Points), ("reputation", last.Reputation))));
        else
            _specialLast.AddChild(Info(Loc.GetString("medical-orders-none")));

        foreach (var offer in state.SpecialOffers)
        {
            var content = SpecialCard(_specialOffers, offer, false);
            var id = offer.RuntimeId;
            var details = new Button { Text = Loc.GetString("medical-special-patient-details") };
            details.OnPressed += _ =>
            {
                if (!_expandedSpecialOffers.Add(id))
                    _expandedSpecialOffers.Remove(id);
                RebuildSpecialContracts();
            };
            content.AddChild(details);
            var accept = new Button
            {
                Text = Loc.GetString("medical-orders-accept"),
                Disabled = state.SpecialActive != null || state.Active != null || !offer.CanIssue ||
                    state.Kind != MedicalOrderMachineKind.PatientReceiver || state.NextSpecialRefresh <= _timing.CurTime,
                HorizontalExpand = true,
            };
            accept.OnPressed += _ => OnRequest?.Invoke(
                new MedicalOrderRequestMessage(MedicalOrderAction.AcceptSpecial, id));
            content.AddChild(accept);
        }
        if (state.SpecialOffers.Length == 0)
            _specialOffers.AddChild(Info(Loc.GetString("medical-orders-none")));
        UpdateSpecialTimer();
    }

    private BoxContainer SpecialCard(BoxContainer parent, MedicalSpecialContractView order, bool active)
    {
        var panel = new PanelContainer { HorizontalExpand = true, Margin = new Thickness(2) };
        var content = Column();
        content.Margin = new Thickness(10);
        content.AddChild(new Label
        {
            Text = Loc.GetString(order.Title, ("site", Loc.GetString(order.Site))),
            StyleClasses = { "LabelHeading" },
        });
        content.AddChild(Info(Loc.GetString("medical-special-number", ("id", order.RuntimeId))));
        content.AddChild(WrappedText(Loc.GetString(order.Description,
            ("site", Loc.GetString(order.Site)), ("patients", order.Patients.Length),
            ("minutes", (int) order.TimeLimit.TotalMinutes))));
        content.AddChild(Info(Loc.GetString("medical-special-summary", ("difficulty", order.Difficulty + 1),
            ("minutes", (int) order.TimeLimit.TotalMinutes), ("points", order.MaximumPoints),
            ("reputation", order.Reputation))));
        content.AddChild(new Label
        {
            Text = Loc.GetString("medical-special-penalty", ("percent", order.PenaltyPercent)),
            FontColorOverride = Color.Orange,
        });
        foreach (var line in order.Reagents)
            content.AddChild(Info(LineText(line, MedicalOrderMachineKind.Reagent)));
        content.AddChild(Info(Loc.GetString("medical-special-patient-progress",
            ("submitted", order.Patients.Count(p => p.Status == MedicalSpecialPatientStatus.Submitted)),
            ("total", order.Patients.Length))));
        if (active || _expandedSpecialOffers.Contains(order.RuntimeId))
        {
            foreach (var patient in order.Patients)
            {
                var status = Loc.GetString(patient.Status switch
                {
                    MedicalSpecialPatientStatus.Pending => "medical-special-patient-pending",
                    MedicalSpecialPatientStatus.Issued => "medical-special-patient-issued",
                    MedicalSpecialPatientStatus.Submitted => "medical-special-patient-submitted",
                    _ => "medical-special-patient-lost",
                });
                content.AddChild(new Label
                {
                    Text = Loc.GetString("medical-special-patient-heading", ("id", patient.Order.RuntimeId),
                        ("name", patient.Name), ("difficulty", patient.Order.Difficulty + 1), ("status", status)),
                    FontColorOverride = patient.Status == MedicalSpecialPatientStatus.Lost ? Color.Orange : Color.LightBlue,
                    HorizontalExpand = true,
                    ClipText = true,
                    ToolTip = patient.Name,
                });
                content.AddChild(WrappedText(string.Join(", ", patient.Order.Lines.Select(l =>
                    LineText(l, MedicalOrderMachineKind.PatientReceiver)))));
                if (patient.CurrentDamage is { } damage)
                    content.AddChild(Info(Loc.GetString("medical-special-patient-damage", ("damage", damage))));
            }
        }
        panel.AddChild(content);
        parent.AddChild(panel);
        return content;
    }

    private static RichTextLabel WrappedText(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(text);
        return label;
    }

    private void UpdateSpecialTimer()
    {
        if (_state == null)
            return;
        var remaining = (_state.SpecialActive?.Deadline ?? _state.NextSpecialRefresh) - _timing.CurTime;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        _specialClock.Text = Loc.GetString(_state.SpecialActive == null
            ? "medical-orders-refresh-in" : "medical-orders-deadline-in",
            ("time", $"{(int) remaining.TotalMinutes:00}:{remaining.Seconds:00}"));
    }
}
