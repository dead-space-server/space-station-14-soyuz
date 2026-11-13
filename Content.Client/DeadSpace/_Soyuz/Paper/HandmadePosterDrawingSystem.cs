// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.Paper;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.DeadSpace._Soyuz.Paper;

public sealed class HandmadePosterDrawingSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IClyde _clyde = default!;

    private readonly Dictionary<EntityUid, (int Revision, OwnedTexture Texture)> _textures = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HandmadePosterComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<HandmadePosterComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<HandmadePosterComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown()
    {
        foreach (var entry in _textures.Values)
            entry.Texture.Dispose();
        _textures.Clear();
        base.Shutdown();
    }

    private void OnStartup(Entity<HandmadePosterComponent> ent, ref ComponentStartup args) => UpdateDrawing(ent);
    private void OnState(Entity<HandmadePosterComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateDrawing(ent);

    private void OnShutdown(Entity<HandmadePosterComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent.Owner, out var sprite) &&
            _sprite.LayerMapTryGet((ent.Owner, sprite), HandmadePosterVisualLayers.Drawing, out var layer, false))
        {
            _sprite.LayerSetTexture((ent.Owner, sprite), layer, Texture.Transparent);
            _sprite.LayerSetVisible((ent.Owner, sprite), layer, false);
        }
        if (_textures.Remove(ent.Owner, out var old))
            old.Texture.Dispose();
    }

    private void UpdateDrawing(Entity<HandmadePosterComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent.Owner, out var sprite) ||
            !_sprite.LayerMapTryGet((ent.Owner, sprite), HandmadePosterVisualLayers.Drawing, out var layer, false))
            return;

        if (_textures.TryGetValue(ent.Owner, out var cached) && cached.Revision == ent.Comp.DrawingRevision)
            return;

        if (!HandmadePosterDrawing.IsValid(ent.Comp.Pixels) || ent.Comp.Pixels.Length == 0)
        {
            _sprite.LayerSetTexture((ent.Owner, sprite), layer, Texture.Transparent);
            _sprite.LayerSetVisible((ent.Owner, sprite), layer, false);
            if (_textures.Remove(ent.Owner, out var old))
                old.Texture.Dispose();
            return;
        }

        // Four subpixels per source pixel, inside the white poster with a one-pixel margin.
        using var image = new Image<Rgba32>(128, 128);
        using var drawing = HandmadePosterTextures.CreateImage(ent.Comp.Pixels);
        // ImageSharp's pixel getter is sandbox-restricted; use the engine's image copy helper.
        drawing.Blit(UIBox2i.FromDimensions(0, 0, HandmadePosterDrawing.Width, HandmadePosterDrawing.Height),
            image, new Vector2i(6 * 4, 3 * 4));

        var texture = _clyde.LoadTextureFromImage(image, "handmade-poster");
        _sprite.LayerSetTexture((ent.Owner, sprite), layer, texture);
        _sprite.LayerSetScale((ent.Owner, sprite), layer, new Vector2(1f / 4));
        _sprite.LayerSetVisible((ent.Owner, sprite), layer, true);
        _textures[ent.Owner] = (ent.Comp.DrawingRevision, texture);
        cached.Texture?.Dispose();
    }
}

internal static class HandmadePosterTextures
{
    public static Image<Rgba32> CreateImage(uint[] pixels)
    {
        var image = new Image<Rgba32>(HandmadePosterDrawing.Width, HandmadePosterDrawing.Height);
        if (pixels.Length != HandmadePosterDrawing.PixelCount)
            return image;

        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var pixel = pixels[y * image.Width + x];
            image[x, y] = new Rgba32((byte) (pixel >> 24), (byte) (pixel >> 16),
                (byte) (pixel >> 8), (byte) pixel);
        }
        return image;
    }
}
