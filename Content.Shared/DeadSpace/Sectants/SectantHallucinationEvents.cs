using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Sectants;

/// <summary>
/// Сервер→клиент: показать/обновить/убрать визуал галлюцинации.
/// Шлётся ТОЛЬКО целевому игроку — остальные её не видят.
/// </summary>
[Serializable, NetSerializable]
public sealed class SectantHallucinationVisualEvent : EntityEventArgs
{
    public NetEntity Id;
    public Vector2 Position;
    public int MapId;
    public float Lifetime;
    public bool Remove;
}

/// <summary>Иллюзия: видна только Target, атакует только его.</summary>
[RegisterComponent]
public sealed partial class SectantHallucinationIllusionComponent : Component
{
    [DataField] public NetEntity Target;
    [DataField] public float Lifetime = 20f;
    [DataField] public float AttackDamage = 5f;
    [DataField] public float AttackInterval = 1.2f;
    [DataField] public float MoveSpeed = 1.5f;

    [ViewVariables] public float Elapsed;
    [ViewVariables] public float AttackAccum;
    [ViewVariables] public float BroadcastAccum;
}
