# AGENTS.md

This file provides guidance to Codex and other coding agents when working with code in this repository.

## Project Overview

Space Station 14 (Fobos fork) - a C# remake of SS13 running on the Robust Toolbox engine. This is a Russian community fork of the main Space Wizards repository.

## Build and Run Commands

```bash
# Initial setup (run once after cloning)
python RUN_THIS.py

# Build
dotnet build --configuration DebugOpt

# Run server and client
./runclient.bat   # or runclient.sh on Linux
./runserver.bat   # or runserver.sh on Linux

# Run tests
dotnet test Content.Tests/Content.Tests.csproj -- NUnit.ConsoleOut=0
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj

# Run single test
dotnet test Content.Tests/Content.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Database migrations
./Content.Server.Database/add-migration.sh <MigrationName>
```

## Architecture

### Project Structure

- **Content.Shared** - Code running on both server and client (components, shared systems, prototypes)
- **Content.Server** - Server-only logic and systems
- **Content.Client** - Client-only logic, UI, rendering
- **RobustToolbox** - Game engine (git submodule)
- **Resources/Prototypes** - YAML entity and data definitions

### Entity Component System (ECS)

**Components** are data containers:
```csharp
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ExampleComponent : Component
{
    [DataField]
    [AutoNetworkedField]
    public int Value = 0;
}
```

Key attributes:
- `[RegisterComponent]` - Required for all components
- `[NetworkedComponent]` - Syncs to clients
- `[AutoGenerateComponentState]` - Generates serialization code
- `[DataField]` - Field loads from YAML prototypes
- `[AutoNetworkedField]` - Individual field syncs

**Systems** process components:
```csharp
public sealed class ExampleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExampleComponent, MapInitEvent>(OnInit);
    }
}
```

Pattern: Create shared system in Content.Shared, extend in Content.Server/Client if needed.

### Prototypes (YAML)

Entity definitions in `Resources/Prototypes/Entities/`:
```yaml
- type: entity
  id: ExampleEntity
  parent: BaseItem
  components:
  - type: Example
    value: 10
```

Data prototypes for configuration:
```yaml
- type: examplePrototype
  id: ExampleId
  name: example-name
```

Prototype C# definition:
```csharp
[Prototype]
public sealed partial class ExamplePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
}
```

### Dependency Injection

Services register in IoC files:
- `Content.Shared/IoC/SharedContentIoC.cs`
- `Content.Server/IoC/ServerContentIoC.cs`

Usage: `[Dependency] private readonly IService _service = default!;`

### Network Events

```csharp
// Define event
[ByRefEvent]
public record struct ExampleEvent(EntityUid Entity);

// Raise event
RaiseLocalEvent(uid, ref exampleEvent);
```

## Code Conventions

- File-scoped namespaces: `namespace Content.Shared.Example;`
- Sealed partial classes for components
- Use `Dirty(uid, component)` to mark networked components as changed
- Access control: `[Access(typeof(ExampleSystem))]`
- Type-safe prototype references: `ProtoId<ExamplePrototype>`

## Testing

- Unit tests inherit from `ContentUnitTest`
- Integration tests use full game systems
- Test framework: NUnit

---

## Вклад в разработку Dead Space Soyuz

Если вы собираетесь внести вклад в разработку Dead Space Soyuz, обратитесь к [руководству по Pull Request'ам от Wizard's Den](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html) — оно послужит хорошей отправной точкой по качеству кода и работе с ветками. Обратите внимание, что у нас нет разделения на `master/stable` ветки.

> ⚠️ **Не используйте веб-редактор GitHub.** Pull Request'ы, созданные через веб-редактор, могут быть закрыты без рассмотрения.

"Upstream" означает [репозиторий `space-wizards/space-station-14`](https://github.com/space-wizards/space-station-14), из которого был сделан форк.

---

## Контент, специфичный для Союза

Всё, что вы создаёте с нуля (в отличие от изменений в существующем upstream-коде), должно размещаться в подкаталогах с префиксом `_Soyuz`.

**Примеры:**
- `Content.Server\DeadSpace\Soyuz\PoliticalLoudspeaker\PoliticalLoudspeakerSystem.cs`
- `Resources\Prototypes\_DeadSpace\_Soyuz\Objectives\goals.yml`
- `Resources\Textures\_DeadSpace\_Soyuz\Objects\Weapons\Guns\Rifles\asval.rsi\icon.png`
- `Resources\Locale\ru-RU\_deadspace\_Soyuz\traits\traits.ftl`
- `Resources\Maps\_Soyuz\train.yml`

НИКОГДА не изменяйте RobustToolbox в рамках своих пул реквестов. Его модификация возможна лишь в случае создания PR в отдельный репозиторий движка.

---

## Изменения файлов из upstream

Если вы правите существующие upstream-файлы (C#, YAML, FTL и т.д.), оставляйте пометки у изменённых мест. Это снижает стоимость мерджей и апстримов. Если вы создаете что то новое всегда создавайте новый файл FTL за исключением правки предыдущего уже существующего текста.

В репозитории используется префикс `DS14` (и уточнения вроде `DS14-Soyuz`). Используйте его же для новых пометок.

**Рекомендуемые форматы:**
- Точечное изменение: `# DS14` или `// DS14`
- Изменение значения: `# DS14-value: СТАРОЕ -> НОВОЕ`
- Блок изменений: `# DS14-start` / `# DS14-end` или `// DS14-start` / `// DS14-end`

**Для YAML:**
- Для одиночных правок оставляйте короткие пометки в строке.
- Для больших вставок используйте блочные маркеры `DS14-start/end`.

**Для C#:**
- Для небольших правок достаточно `// DS14`.
- Для крупных портов/вставок оборачивайте блок в `DS14-start/end`.
- Если код портирован из upstream PR, укажите номер PR в описании PR или рядом с блоком.

> ⚠️ В `.ftl` не ставьте комментарий в той же строке, что и ключ перевода. Комментарий должен быть строкой выше.

---

## Примеры комментариев

**Изменение поля в YAML:**
```yml
- type: entity
  id: ExampleEntity
  categories:
  - NewCategory # DS14
```

**Изменение значения:**
```yml
  - type: Gun
    fireRate: 4 # DS14-value: 3 -> 4
```

**Блочная вставка в YAML:**
```yml
# DS14-start
- type: entity
  id: ExampleSoyuzEntity
  parent: BaseItem
# DS14-end
```

**Точечное изменение в C#:**
```cs
using Content.Shared.Damage; // DS14
```

**Блочная вставка в C#:**
```cs
// DS14-start: синхронизация цвета штампа
if (TryComp<StampComponent>(uid, out var stamp))
{
    stamp.StampedColor = state.Color;
}
// DS14-end
```

**Изменение локализации (`.ftl`):**
```fluent
# DS14-value: "Job Whitelists" -> "Role Whitelists"
player-panel-job-whitelists = Role Whitelists
```

---

## Карты

Для карт Союза используйте каталог `Resources\Maps\_Soyuz`.

Если добавляете новую карту ротации:
- добавьте файл карты в `Resources\Maps\_Soyuz\...`;
- добавьте/обновите `gameMap`-прототип в `Resources\Prototypes\Maps\...`;
- при необходимости обновите map pool в `Resources\Prototypes\Maps\Pools\...`.

Если меняете существующую карту, заранее согласуйте изменения с мейнтейнером карты и не работайте параллельно над одной `.yml`-картой без координации.

---

## Перед отправкой PR

Перед отправкой PR:
- проверьте `git diff` и список файлов на случайные изменения;
- убедитесь, что нет лишних форматных изменений (пробелы, переносы строк, массовый рефакторинг не по задаче);
- запустите хотя бы базовую проверку сборки: `dotnet build SpaceStation14.slnx`.

Если PR долго живёт и в него случайно попали изменения в `RobustToolbox`, уберите их отдельным коммитом:

```bash
git fetch upstream
git restore --source upstream/master -- RobustToolbox
```

---

## Ченджлоги

Для контента Союза используйте:
- `Resources\Changelog\ChangelogDS14Soyuz.yml` — общий ченджлог Союза;
- `Resources\Changelog\Maps.yml` — изменения карт.

Следуйте существующему формату записей (`Add`, `Fix`, `Tweak`, `Remove`) и указывайте номер PR, если он есть.

---

## Дополнительные ресурсы

Если вы новичок в разработке SS14:
- [Документация SS14](https://docs.spacestation14.io/)
- [Обширный гайд по разработкке SS14](https://wiki.team.ss14.org/development)

---
