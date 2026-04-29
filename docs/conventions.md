# Конвенции кода (Soyuz)

В SS14 один и тот же результат можно написать десятками способов, но в PR принимаются только предсказуемые и поддерживаемые решения.

Этот документ описывает соглашения для `space-station-14-soyuz`. Соблюдение этих правил напрямую влияет на скорость ревью и вероятность мерджа.

См. также:
- [Организация кодовой базы](./codebase-organization.md)
- [Руководство по Pull Request](./pull-request-guidelines.md)
- [Предложения фич](./feature-proposals.md)
- [Git для разработчика SS14](./git-for-the-ss14-developer.md)

```admonish info
В старых частях проекта могут встречаться отступления от этих правил. Новый код должен максимально следовать актуальным конвенциям.
```

## Общие правила программирования

### Не копируйте код без необходимости

Если вы хотите "сделать так же, как в другом месте", не копируйте блоки вслепую. Выделяйте общий код в метод/абстракцию.

Исключение: небольшой неизбежный boilerplate (например, типичный каркас `EntitySystem`) допустим.

### Не используйте magic values

Строки/числа, которые "должны совпасть где-то еще", нужно выносить в константы/типобезопасные ссылки:
- `const` / `static readonly` для фиксированных значений.
- `ProtoId<T>` для ссылок на prototype ID из C#.

Цель: если значения обязаны совпадать, несовпадение должно ловиться на этапе компиляции/валидации.

### Комментарии: объясняйте зачем

Комментарии должны объяснять:
- почему решение устроено именно так;
- какой инвариант/ограничение важно не сломать.

Что делает код, обычно видно из самого кода.

Для публичных API, `DataField` и значимых методов используйте XML-документацию.

```csharp
/// <summary>
/// Сбрасывает счетчик взаимодействий у компонента.
/// </summary>
/// <remarks>
/// Публичный метод для других систем.
/// </remarks>
[PublicAPI]
public void ResetInteractCounter(Entity<FooComponent?> ent)
```

### Строки и идентификаторы

Не смешивайте человекочитаемый текст и служебные идентификаторы:
- не используйте локализованный текст как ключи/ID;
- не показывайте пользователю `Enum.ToString()` напрямую.

```csharp
GenderLabel.Text = Loc.GetString($"gender-{gender}");
```

Для поиска/фильтра по отображаемому тексту используйте сравнение с `CurrentCulture`, а не invariant-сравнение.

### Свойства

Сеттер свойства не должен скрытно менять значение (например, локализовывать строку внутри сеттера). Переданный `value` должен становиться фактическим значением свойства.

### Порядок членов типа

Внутри класса держите порядок:
1. поля;
2. auto-properties;
3. остальные свойства/методы.

Это упрощает чтение структуры данных перед логикой.

## Конвенции проекта SS14/Soyuz

### Структура файла

1. `using` в начале файла.
2. File-scoped namespace (например, `namespace Content.Server.Atmos.EntitySystems;`).
3. Поля и auto-properties выше методов.

### Методы

Если сигнатура не помещается в строку, переносите на "один параметр на строку":

```csharp
public void CopyTo(
    ISerializationManager serializationManager,
    SortedDictionary<TKey, TValue> source,
    ref SortedDictionary<TKey, TValue> target,
    SerializationHookContext hookCtx,
    ISerializationContext? context = null)
```

### Константы и CVars

Значения, от которых зависит поведение, должны быть:
- либо `const` (если неизменяемо),
- либо CVar (если настраиваемо).

### Прототипы

- Не кэшируйте прототипы объектами без необходимости, храните ID и резолвьте через `IPrototypeManager`.
- В `DataField` для ID используйте `ProtoId<T>`.
- Для игровых типов предпочтительнее прототипы, а не enum.

```csharp
[DataField]
public List<ProtoId<ExamplePrototype>> ExampleTypes = new();
```

### Ресурсы

#### Звуки

Используйте `SoundSpecifier`, а при возможности `SoundCollectionSpecifier` вместо прямых путей.

```csharp
[DataField]
public SoundSpecifier Sound = new SoundCollectionSpecifier("MySoundCollection");
```

#### Спрайты/текстуры

Используйте `SpriteSpecifier`.

```csharp
[DataField]
public SpriteSpecifier Icon = SpriteSpecifier.Invalid;
```

#### `meta.json` в RSI

- Порядок полей: `version -> license -> copyright -> size -> states`.
- Форматирование: не minified JSON, отступ 4 пробела, без tabs.

### Логи и EntityUid

В админ-логах выводите сущности через `ToPrettyString`:

```csharp
_adminLogs.Add(LogType.MyLog, LogImpact.Medium, $"{ToPrettyString(uid)} did something!");
```

### Optional entities

Для необязательных сущностей используйте `EntityUid?` и `null`, не `EntityUid.Invalid`.

## Components

### Данные компонента

Поля данных компонента должны быть `public`.

### Логика в сеттерах

Сеттеры компонентов не должны содержать бизнес-логику. Логику изменения состояния выносите в методы системы.

### Ограничение доступа

Используйте `[Access(...)]` там, где можно ограничить запись только профильным системам.

### Shared-наследование

Если shared-компонент имеет server/client потомков, shared-тип делайте `abstract`.

## Entity Systems

### Где должна жить логика

Игровая логика живет в `EntitySystem`, компоненты хранят только данные.

### Proxy-методы

Предпочитайте proxy-методы `EntitySystem` вместо прямого обращения к `EntityManager`, где это возможно.

### Публичные API-методы систем

Сигнатуры публичных методов систем:
- сначала `Entity<T?>` / `EntityUid`;
- затем прочие аргументы;
- в начале метода выполняйте `Resolve(...)`.

```csharp
public void SetCount(Entity<StackComponent?> stack, int count)
{
    if (!Resolve(stack, ref stack.Comp))
        return;

    // logic
}
```

### Extension methods

Не добавляйте extension-методы для сущностей/компонентов/систем симуляции. Для этого используйте публичные методы систем.

### Зависимости

В `EntitySystem` используйте `[Dependency]`, а не `IoCManager.Resolve` в теле метода.

## Events

### Method Event vs System Method

Внешний API действия должен вызываться методами систем. "Method events" напрямую как API запрещены.

Допустимо: событие под капотом внутри метода системы.

### Именование событий

- Событие: суффикс `Event`.
- Обработчик: `OnXEvent`.

### By-ref события

Используйте struct-события с `[ByRefEvent]` и отправкой по `ref`.

```csharp
var ev = new MyEvent();
RaiseLocalEvent(ref ev);
```

### C# events

Для симуляции используйте EventBus. C# events допустимы в основном для UI/внесимуляционных сценариев, с обязательной отпиской.

### Async

Для игровой симуляции избегайте async, для сценариев вроде DoAfter используйте события.

## UI

Предпочитайте XAML-интерфейсы. C#-only UI допустимы как legacy, но новые экраны лучше делать в XAML.

## Производительность

### Итерирование

Где уместно, предпочитайте iterator methods вместо создания временных коллекций. В hot-path учитывайте аллокации.

### Модификаторы класса

Классы должны быть осознанно помечены: `sealed`, `abstract`, `static` или `[Virtual]`.

### Events вместо per-tick update

Запускайте логику по событиям, а не на каждом тике без необходимости.

### Захват переменных

Избегайте variable capture в lambda/local function на горячих путях. Для делегатов добавляйте перегрузки с `state`.

## Field Deltas

Для сетевых компонентов с несколькими независимыми полями используйте:

```csharp
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
```

При изменении одного поля отправляйте его через `DirtyField`, а не `Dirty`:

```csharp
comp.IsActive = true;
DirtyField(uid, comp, nameof(MyComponent.IsActive));
```

## Время и пауза

### TimeSpan

Для интервалов и таймеров используйте `TimeSpan` (не `float`). Сравнение времени симуляции - через `CurTime`.

### Пауза сущностей

Для runtime-таймеров в компонентах используйте:
- `[AutoGenerateComponentPause]` на компоненте;
- `[AutoPausedField]` на нужных `TimeSpan`-полях.

### TimeOffsetSerializer

Для абсолютного времени, смещенного относительно времени игры, используйте `TimeOffsetSerializer`.

## Нейминг

### Shared типы

Префикс `Shared` используйте только когда существует одноименный server/client тип.

- `FooComponent` только в shared -> без префикса.
- Тип есть в shared+server+client -> `SharedFooComponent`.

## Физика

Для якорения используйте методы `TransformSystem`.
`PhysicsComponent` static-body anchoring - только если вы точно понимаете последствия.

## YAML-конвенции

- Внутри `components:` не делайте пустые строки между `- type`.
- Между прототипами - одна пустая строка.
- Порядок полей в прототипе: `type -> abstract -> parent -> id -> categories -> name -> suffix -> description -> components`.
- Для списков `categories` используйте inline-список, для остальных обычный список.
- Не задавайте текстуры в abstract/parent-прототипах.
- В `name`/`description` не ставьте кавычки без необходимости.

Пример структуры сущности:

```yaml
- type: entity
  abstract: true
  parent: BaseItem
  id: ExampleEntity
  name: Example
  description: Example description.
  components:
  - type: Sprite
```

### Имена в YAML/DataField

- `PascalCase` для ID и имен компонентов.
- `camelCase` для остального (включая имена prototype type).
- Не используйте `prefix.Something` как ID.

## Локализация

Любой текст, который видит игрок, должен быть локализован.

### Имена localization ID

- Только `kebab-case`.
- Без заглавных букв.
- ID должны быть достаточно специфичны, чтобы не конфликтовать.

```ftl
antag-traitor-user-was-traitor-message = ...
```

## In-simulation vs out-of-simulation

```admonish warning
Это правило исторически не везде соблюдено, но новый код должен его придерживаться.
```

Разделяйте код симуляции и внешние сервисные процессы.

Внутри симуляции:
- сущности, физика, атмос, IC-чат, состояние раунда.

Вне симуляции:
- OOC, adminhelp/votes, БД, Discord/webhook-интеграции.

Проверочный вопрос: "эта логика должна работать, если симуляция поставлена на паузу?"

| Задача | In-simulation | Out-of-simulation |
|---|---|---|
| Базовая singleton-логика | `EntitySystem` | Менеджер (IoC + EntryPoint) |
| Измерение времени | `IGameTiming.CurTime` | `IGameTiming.RealTime`, `Stopwatch`, `DateTime` |
| Кастомные network-сообщения | networked entity events | custom `NetMessage` |

## Дополнения для Soyuz

### Размещение нового контента

Новый контент Soyuz размещайте в `_Soyuz`-подкаталогах (где это применимо), например:
- `Resources/Prototypes/_DeadSpace/_Soyuz/...`
- `Resources/Textures/_DeadSpace/_Soyuz/...`
- `Resources/Locale/ru-RU/_deadspace/_Soyuz/...`
- `Resources/Maps/_Soyuz/...`

### Правки upstream-файлов

При правках существующих upstream-файлов оставляйте пометки `DS14` у измененных мест:
- `# DS14` / `// DS14`
- `# DS14-value: OLD -> NEW`
- `DS14-start` / `DS14-end` для крупных блоков

Для `.ftl`: комментарий должен быть строкой выше ключа.

### RobustToolbox

Не вносите изменения в `RobustToolbox` в рамках PR этого репозитория.

### Changelog Soyuz

Для пользовательских изменений в PR используйте блок `:cl:` из PR-шаблона.
Категории: `Добавлено`, `Исправлено`, `Удалено`, `Изменено`.

По категориям файлов:
- общий: `Resources/Changelog/ChangelogDS14Soyuz.yml`
- карты: `Resources/Changelog/Maps.yml` (через `MAPS:`)
- админские изменения: через `ADMIN:`
