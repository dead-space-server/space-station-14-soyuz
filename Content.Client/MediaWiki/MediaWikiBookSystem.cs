using Content.Client.MediaWiki.UI;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;

namespace Content.Client.MediaWiki;

public sealed class MediaWikiBookSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IResourceManager _resources = default!;

    private MediaWikiBookWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MediaWikiBookComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<MediaWikiBookComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        using var file = _resources.ContentFileReadText(ent.Comp.Text);
        var source = file.ReadToEnd();

        _window ??= new MediaWikiBookWindow();
        _window.SetDocument(ent.Comp.Title, ent.Comp.Page, source);

        if (_window.IsOpen)
            _window.MoveToFront();
        else
            _window.OpenCentered();

        args.Handled = true;
    }
}
