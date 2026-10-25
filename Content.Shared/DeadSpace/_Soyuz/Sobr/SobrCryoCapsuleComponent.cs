// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Shared.DeadSpace._Soyuz.Sobr;

/// <summary>
/// Криокапсула патриота СОБРа СССП. Когда пульт будит её отряд, перед капсулой
/// спавнится богатырь из <see cref="Fighters"/>.
/// </summary>
[RegisterComponent]
public sealed partial class SobrCryoCapsuleComponent : Component
{
    /// <summary>
    /// Лэ брат какой отряд и боец тебе нужен
    /// В одной капсуле можно указать сразу несколько отрядов
    /// Указывайте в каждую криокапсулу только один вид бойцов (1 отряд = 1 вид бойца)
    /// Капсула одна, поэтмоу после пробуждения одного отряда, другой отряд уже нельзя будет вызвать
    /// Ну все дальше развлекайся сам поймешь держим связь ☺
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<SobrSquadPrototype>, EntProtoId> Fighters = new();

    /// <summary>
    /// Скок тебе долбаебиков нужно в криокапсуле. (Считается на все отряды, а не на один)
    /// </summary>
    [DataField]
    public int Count = 1;

    /// <summary>
    /// Где боец появится относительно криокапсулы. (0, -1) = 1 тайл на юг
    /// </summary>
    [DataField]
    public Vector2 SpawnOffset = new(0, -1);

    /// <summary>
    /// Если true, то как тока роль исчезнет (Пикнули или удалили), появляется возможность заспавнить ещё раз. Если false, то срабатывает один раз за раунд
    /// </summary>
    [DataField]
    public bool Reusable;

    /// <summary>
    /// С какой мамаджан музыкой будет просыпаться наш богатырь
    /// </summary>
    [DataField]
    public SoundSpecifier? WakeSound;

    /// <summary>
    /// Через сколько минут новый слоняра появляется в криокапсуле. Если 0, то не появится никогда. (Считается на все отряды, а не на один)
    /// </summary>
    [DataField]
    public float RefillMinutes;

    [ViewVariables]
    public TimeSpan? NextRefill;

    [ViewVariables]
    public bool Used;

    [ViewVariables]
    public List<EntityUid> Occupants = new();
}