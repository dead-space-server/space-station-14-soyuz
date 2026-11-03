// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.DeadSpace._Soyuz.EvacZones;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class EvacZoneStroke
{
    [DataField]
    public Color Color = Color.White;

    [DataField]
    public List<Vector2> Points = new();

    public EvacZoneStroke()
    {
    }

    public EvacZoneStroke(Color color, List<Vector2> points)
    {
        Color = color;
        Points = points;
    }

    public EvacZoneStroke Clone() => new(Color, new List<Vector2>(Points));
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class EvacZoneColor
{
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public Color Color = Color.White;

    public EvacZoneColor()
    {
    }

    public EvacZoneColor(string name, Color color)
    {
        Name = name;
        Color = color;
    }
}

public static class EvacZoneConstants
{
    public const int MaxStrokes = 300;
    public const int MaxPointsPerStroke = 400;
    public const float MaxCoordinate = 20000f;

    /// <summary>Ширина полосы зоны в тайлах</summary>
    public const float Thickness = 1.5f;

    /// <summary>Минимальный шаг между точками при рисовании в тайлах</summary>
    public const float MinPointStep = 0.4f;
}
