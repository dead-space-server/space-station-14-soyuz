using System.Numerics;
using Content.Client.Resources;
using Content.Shared.Kitchen;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client.Kitchen;

/// <summary>
/// Persistent step and countdown over the cookware, independently of the recipe window.
/// </summary>
public sealed class CookingPromptOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly Font _font;
    private readonly IPlayerManager _players;

    private const float TextScale = 1.15f;
    private const float HorizontalPadding = 12f;
    private const float VerticalPadding = 8f;
    private const float LineGap = 3f;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public CookingPromptOverlay(IEntityManager entities, IResourceCache resources, IPlayerManager players)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
        _font = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        _players = players;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null || _players.LocalEntity is not { } viewer)
            return;

        var query = _entities.EntityQueryEnumerator<CookingPromptComponent, TransformComponent>();
        while (query.MoveNext(out _, out var prompt, out var transform))
        {
            if (prompt.Viewer != viewer || prompt.Stage.Length == 0 || transform.MapID != args.MapId)
                continue;

            var center = args.ViewportControl.WorldToScreen(_transform.GetWorldPosition(transform));
            var width = Math.Max(180f * TextScale,
                Math.Max(args.ScreenHandle.GetDimensions(_font, prompt.Stage, TextScale).X,
                    args.ScreenHandle.GetDimensions(_font, prompt.Detail, TextScale).X) + HorizontalPadding * 2f);
            var lineHeight = _font.GetLineHeight(TextScale);
            var height = lineHeight * 2f + VerticalPadding * 2f + LineGap;
            var size = new Vector2(width, height);
            // Keep the same gap above the cookware; the larger box grows upwards.
            var topLeft = center - new Vector2(width / 2f, height + 34f);
            var rect = UIBox2.FromDimensions(topLeft, size);

            args.ScreenHandle.DrawRect(rect, Color.FromHex("#172027").WithAlpha(0.93f));
            args.ScreenHandle.DrawRect(rect, Color.FromHex("#e5c68a"), false);
            args.ScreenHandle.DrawString(_font,
                topLeft + new Vector2(HorizontalPadding, VerticalPadding),
                prompt.Stage, TextScale, Color.FromHex("#e5c68a"));
            args.ScreenHandle.DrawString(_font,
                topLeft + new Vector2(HorizontalPadding, VerticalPadding + lineHeight + LineGap),
                prompt.Detail, TextScale, Color.White);
        }
    }
}
