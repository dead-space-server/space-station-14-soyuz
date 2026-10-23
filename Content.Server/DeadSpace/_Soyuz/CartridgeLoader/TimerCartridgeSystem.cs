// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Server.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeadSpace._Soyuz.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.CartridgeLoader;

[RegisterComponent, Access(typeof(TimerCartridgeSystem))]
public sealed partial class TimerCartridgeComponent : Component
{
    [DataField] public int DurationSeconds = 300;
    [DataField] public string Label = string.Empty;
    [DataField] public PdaTimerStatus Status;
    [DataField] public TimeSpan Remaining;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan EndsAt;
    [DataField] public int Revision;
}

public sealed class TimerCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _loader = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PdaComponent, MapInitEvent>(OnPdaInit, after: new[] { typeof(CartridgeLoaderSystem) });
        SubscribeLocalEvent<TimerCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<TimerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<TimerCartridgeComponent, CartridgeRemovedEvent>(OnRemoved);
        SubscribeLocalEvent<TimerCartridgeComponent, EntityUnpausedEvent>(OnUnpaused);
    }

    private void OnPdaInit(EntityUid uid, PdaComponent component, MapInitEvent args)
    {
        if (!TryComp<CartridgeLoaderComponent>(uid, out var loader) ||
            _loader.HasProgram<TimerCartridgeComponent>(uid, installedOnly: true, loader: loader))
            return;

        // Reserve a built-in program slot, including for PDAs with their own preinstalled list.
        loader.DiskSpace++;
        if (!_loader.InstallProgram(uid, "TimerCartridge", deinstallable: false, loader: loader))
            loader.DiskSpace--;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<TimerCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var timer, out var cartridge))
        {
            if (timer.Status != PdaTimerStatus.Running || MetaData(uid).EntityPaused ||
                TerminatingOrDeleted(uid) || cartridge.LoaderUid is not { } loaderUid ||
                TerminatingOrDeleted(loaderUid) || timer.EndsAt > _timing.CurTime)
                continue;

            Finish(uid, timer, loaderUid);
        }
    }

    private void OnUnpaused(EntityUid uid, TimerCartridgeComponent timer, ref EntityUnpausedEvent args)
    {
        if (timer.Status != PdaTimerStatus.Running)
            return;

        timer.EndsAt += args.PausedTime;
        if (TryComp<CartridgeComponent>(uid, out var cartridge) && cartridge.LoaderUid is { } loaderUid)
            UpdateUi(uid, timer, loaderUid);
    }

    private void OnUiReady(EntityUid uid, TimerCartridgeComponent timer, CartridgeUiReadyEvent args)
    {
        UpdateUi(uid, timer, args.Loader);
    }

    private void OnRemoved(EntityUid uid, TimerCartridgeComponent timer, CartridgeRemovedEvent args)
    {
        Reset(timer);
    }

    private void OnMessage(EntityUid uid, TimerCartridgeComponent timer, CartridgeMessageEvent args)
    {
        if (args is not TimerCartridgeMessage message ||
            !TryComp<CartridgeComponent>(uid, out var cartridge) ||
            cartridge.LoaderUid is not { } loaderUid || loaderUid != GetEntity(args.LoaderUid) ||
            !TryComp<CartridgeLoaderComponent>(loaderUid, out var loader) || loader.ActiveProgram != uid ||
            MetaData(uid).EntityPaused)
            return;

        // Resolve expiration before accepting a command, including one received between ticks.
        if (timer.Status == PdaTimerStatus.Running && timer.EndsAt <= _timing.CurTime)
            Finish(uid, timer, loaderUid);

        if (message.Revision != timer.Revision)
        {
            UpdateUi(uid, timer, loaderUid);
            return;
        }

        switch (message.Action)
        {
            case PdaTimerAction.Start when timer.Status is PdaTimerStatus.Idle or PdaTimerStatus.Finished:
                if (message.DurationSeconds is < 1 or > TimerCartridgeState.MaxDurationSeconds ||
                    message.Label == null || message.Label.Length > TimerCartridgeState.MaxLabelLength)
                    return;

                timer.DurationSeconds = message.DurationSeconds;
                timer.Label = message.Label.Trim();
                timer.Remaining = TimeSpan.FromSeconds(timer.DurationSeconds);
                timer.EndsAt = _timing.CurTime + timer.Remaining;
                timer.Status = PdaTimerStatus.Running;
                timer.Revision++;
                break;
            case PdaTimerAction.Pause when timer.Status == PdaTimerStatus.Running:
                timer.Remaining = timer.EndsAt - _timing.CurTime;
                timer.EndsAt = TimeSpan.Zero;
                timer.Status = PdaTimerStatus.Paused;
                timer.Revision++;
                break;
            case PdaTimerAction.Resume when timer.Status == PdaTimerStatus.Paused:
                timer.EndsAt = _timing.CurTime + timer.Remaining;
                timer.Status = PdaTimerStatus.Running;
                timer.Revision++;
                break;
            case PdaTimerAction.Reset:
                Reset(timer);
                break;
            default:
                return;
        }

        UpdateUi(uid, timer, loaderUid);
    }

    private static void Reset(TimerCartridgeComponent timer)
    {
        timer.Status = PdaTimerStatus.Idle;
        timer.Remaining = TimeSpan.Zero;
        timer.EndsAt = TimeSpan.Zero;
        timer.Revision++;
    }

    private void Finish(EntityUid uid, TimerCartridgeComponent timer, EntityUid loaderUid)
    {
        // Commit completion first: a timer can notify only once, regardless of open windows.
        timer.Status = PdaTimerStatus.Finished;
        timer.Remaining = TimeSpan.Zero;
        timer.EndsAt = TimeSpan.Zero;
        timer.Revision++;
        UpdateUi(uid, timer, loaderUid);
        _loader.SendNotification(loaderUid, Loc.GetString("timer-cartridge-program-name"),
            string.IsNullOrWhiteSpace(timer.Label)
                ? Loc.GetString("timer-cartridge-notification")
                : Loc.GetString("timer-cartridge-notification-named", ("name", timer.Label)));
    }

    private void UpdateUi(EntityUid uid, TimerCartridgeComponent timer, EntityUid loaderUid)
    {
        // Sending a timer state to another active program would replace that program's state.
        if (!TryComp<CartridgeLoaderComponent>(loaderUid, out var loader) || loader.ActiveProgram != uid ||
            !TryComp<CartridgeComponent>(uid, out var cartridge) || cartridge.LoaderUid != loaderUid)
            return;

        _loader.UpdateCartridgeUiState(loaderUid, new TimerCartridgeState(timer.DurationSeconds, timer.Label,
            timer.Status, timer.EndsAt, timer.Remaining, timer.Revision), loader: loader);
    }
}
