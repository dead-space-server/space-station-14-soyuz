# Вклад в разработку Dead Space Soyuz

Если вы собираетесь внести вклад в разработку Dead Space Soyuz, обратитесь к [руководству по Pull Request'ам](docs/pull-request-guidelines.md) — оно послужит хорошей отправной точкой по качеству кода и работе с ветками. Обратите внимание, что у нас нет разделения на `master/stable` ветки.

> ⚠️ **Не используйте веб-редактор GitHub.** Pull Request'ы, созданные через веб-редактор, могут быть закрыты без рассмотрения.

"Upstream" означает [репозиторий `space-wizards/space-station-14`](https://github.com/dead-space-server/dead-space-14), из которого был сделан форк.

---

## Контент, специфичный для Союза

Всё, что вы создаёте с нуля (в отличие от изменений в существующем upstream-коде), должно размещаться в подкаталогах с префиксом `_Soyuz`.

**Примеры:**
- `Content.Server\DeadSpace\Soyuz\PoliticalLoudspeaker\PoliticalLoudspeakerSystem.cs`
- `Resources\Prototypes\_DeadSpace\_Soyuz\Objectives\goals.yml`
- `Resources\Textures\_DeadSpace\_Soyuz\Objects\Weapons\Guns\Rifles\asval.rsi\icon.png`
- `Resources\Locale\ru-RU\_deadspace\_Soyuz\traits\traits.ftl`
- `Resources\Maps\_Soyuz\train.yml`

---

## Изменения файлов из upstream

Если вы правите существующие upstream-файлы (C#, YAML, FTL и т.д.), оставляйте пометки у изменённых мест. Это снижает стоимость мерджей и апстримов. Если вы создаете что то новое всегда создавайте новый файл FTL за исключением правки предыдущего уже существующего текста.

В репозитории используется префикс `DS14-Soyuz` (и уточнения вроде ``). Используйте его же для новых пометок.

**Рекомендуемые форматы:**
- Точечное изменение: `# DS14-Soyuz` или `// DS14-Soyuz`
- Изменение значения: `# DS14-Soyuz-value: СТАРОЕ -> НОВОЕ`
- Блок изменений: `# DS14-Soyuz-start` / `# DS14-Soyuz-end` или `// DS14-Soyuz-start` / `// DS14-Soyuz-end`

**Для YAML:**
- Для одиночных правок оставляйте короткие пометки в строке.
- Для многострочных вставок используйте блочные маркеры `DS14-Soyuz-start/end`.

**Для C#:**
- Для небольших правок достаточно `// DS14-Soyuz`.
- Для многострочных портов/вставок оборачивайте блок в `DS14-Soyuz-start/end`.
- Если код портирован из upstream PR, укажите номер PR в описании PR или рядом с блоком.

> ⚠️ В `.ftl` не ставьте комментарий в той же строке, что и ключ перевода. Комментарий должен быть строкой выше.

---

## Примеры комментариев

**Изменение поля в YAML:**
```yml
- type: entity
  id: ExampleEntity
  categories:
  - NewCategory # DS14-Soyuz
```

**Изменение значения:**
```yml
  - type: Gun
    fireRate: 4 # DS14-Soyuz-value: 3 -> 4
```

**Блочная вставка в YAML:**
```yml
# DS14-Soyuz-start
- type: entity
  id: ExampleSoyuzEntity
  parent: BaseItem
# DS14-Soyuz-end
```

**Точечное изменение в C#:**
```cs
using Content.Shared.Damage; // DS14-Soyuz
```

**Блочная вставка в C#:**
```cs
// DS14-Soyuz-start: синхронизация цвета штампа
if (TryComp<StampComponent>(uid, out var stamp))
{
    stamp.StampedColor = state.Color;
}
// DS14-Soyuz-end
```

**Изменение локализации (`.ftl`):**
```fluent
# DS14-Soyuz-value: "Job Whitelists" -> "Role Whitelists"
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
- `Resources\Changelog\ChangelogDS14-SoyuzSoyuz.yml` — общий ченджлог Союза;
- `Resources\Changelog\Maps.yml` — изменения карт.

Следуйте существующему формату записей (`Add`, `Fix`, `Tweak`, `Remove`) и указывайте номер PR, если он есть.

---

## Дополнительные ресурсы

Если вы новичок в разработке SS14:
- [Документация SS14](https://docs.spacestation14.io/)
- [Обширный гайд по разработкке SS14](https://wiki.team.ss14.org/development)

---

## Генерированный ИИ-контент

Контент, созданный ИИ (код, спрайты и т.п.), **запрещено** добавлять в репозиторий.

Попытка отправить такой контент может привести к **бану на участие в разработке**.
