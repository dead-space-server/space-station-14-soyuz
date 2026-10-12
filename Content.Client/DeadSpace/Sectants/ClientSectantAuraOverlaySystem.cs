using System.Collections.Generic;
using System.Linq;
using Content.Shared.DeadSpace.Sectants;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;


namespace Content.Client.DeadSpace.Sectants;

public sealed class ClientSectantAuraOverlaySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private readonly Dictionary<EntityUid, int> _auraLayers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SectantAuraOverlayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SectantAuraOverlayComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<SectantAuraOverlayComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var layerIndex = sprite.AddLayer(new SpriteSpecifier.Rsi(new ResPath("_DeadSpace/TEMP_FOR_EVENT/little_gabry/aura.rsi"), "icon"));
        _auraLayers[ent.Owner] = layerIndex;

        var shader = _protoMan.Index<ShaderPrototype>(ent.Comp.Shader).InstanceUnique();
        shader.SetParameter("auraColor", ent.Comp.Color);
        shader.SetParameter("pulseSpeed", ent.Comp.PulseSpeed);
        shader.SetParameter("outlineWidth", ent.Comp.OutlineWidth);
        shader.SetParameter("spikeCount", ent.Comp.SpikeCount);

        sprite.LayerSetShader(layerIndex, shader);
    }

    private void OnShutdown(Entity<SectantAuraOverlayComponent> ent, ref ComponentShutdown args)
    {
        if (!_auraLayers.Remove(ent.Owner, out var layerIndex))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        sprite.RemoveLayer(layerIndex);
    }
}
