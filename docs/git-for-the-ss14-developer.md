# Git для разработчика SS14 (Soyuz)

См. также:
- [Конвенции кода](./conventions.md)
- [Руководство по Pull Request](./pull-request-guidelines.md)
- [Предложения фич](./feature-proposals.md)


Этот гайд - практичный минимум Git для контрибьюта в `space-station-14-soyuz`.

Полезные ссылки:
- [Git Book](https://git-scm.com/book/en/v2)
- [Atlassian Git Tutorials](https://www.atlassian.com/git/tutorials)
- [Oh Shit, Git?!](https://ohshitgit.com/)
- [Learn Git Branching](https://learngitbranching.js.org/)

## 1. Базовая настройка Git

Убедитесь, что установлены:
- Git;
- Python 3.7+;
- любой удобный клиент (CLI/IDE/GUI).

Важно: `user.name` и `user.email` попадают в историю коммитов. Если нужна приватность, используйте GitHub noreply email.

## 2. Репозитории, форк и remotes

### 2.1 Термины

- **Локальный репозиторий**: код у вас на машине.
- **Remote**: ссылка на удаленный репозиторий.
- **origin**: обычно ваш форк.
- **upstream**: основной репозиторий, куда вы хотите отправлять PR.

Для Soyuz обычно:
- `origin` -> ваш форк `space-station-14-soyuz`;
- `upstream` -> основной репозиторий Soyuz (не `space-wizards`, если вы работаете именно в Soyuz).

### 2.2 Клонирование

```powershell
git clone https://github.com/<you>/space-station-14-soyuz.git
Set-Location .\space-station-14-soyuz
```

```bash
git clone https://github.com/<you>/space-station-14-soyuz.git
cd space-station-14-soyuz
```

### 2.3 Проверка/добавление upstream

```powershell
git remote -v
git remote add upstream https://github.com/dead-space-server/space-station-14-soyuz.git
```

```bash
git remote -v
git remote add upstream https://github.com/dead-space-server/space-station-14-soyuz.git
```

Если remote уже существует, команда `add` не нужна.

## 3. Субмодули и первичный запуск

После клонирования выполните:

```powershell
python .\RUN_THIS.py
```

```bash
python RUN_THIS.py
```

Скрипт подготавливает окружение и субмодули.

Если нужна ручная работа с submodules:

```powershell
git submodule update --init --recursive
```

```bash
git submodule update --init --recursive
```

Для обычных PR в Soyuz **не коммитьте изменения в `RobustToolbox`**.

## 4. Рабочий цикл веток

### 4.1 Обновить локальный `master`

```powershell
git checkout master
git fetch upstream
git merge upstream/master
```

```bash
git checkout master
git fetch upstream
git merge upstream/master
```

### 4.2 Создать рабочую ветку

```powershell
git checkout -b feat/my-change
```

```bash
git checkout -b feat/my-change
```

Никогда не открывайте PR из `master`.

### 4.3 Внести изменения и закоммитить

```powershell
git status
git add <files>
git commit -m "Краткое описание изменения"
```

```bash
git status
git add <files>
git commit -m "Краткое описание изменения"
```

Рекомендации:
- делайте небольшие осмысленные коммиты;
- перед коммитом проверяйте diff;
- не включайте случайные форматные изменения.

### 4.4 Отправить ветку

```powershell
git push -u origin feat/my-change
```

```bash
git push -u origin feat/my-change
```

## 5. Создание Pull Request

Откройте PR из вашей ветки в целевую ветку репозитория Soyuz (обычно `master`).

Перед отправкой PR:
- заполните актуальный шаблон PR;
- приложите медиа, если изменение игровое;
- добавьте `:cl:` при необходимости;
- убедитесь, что нет изменений `RobustToolbox`.

## 6. Как держать ветку актуальной

Если в `master` уже влились изменения, подтяните их в вашу ветку:

```powershell
git fetch upstream
git checkout master
git merge upstream/master
git checkout feat/my-change
git merge master
```

```bash
git fetch upstream
git checkout master
git merge upstream/master
git checkout feat/my-change
git merge master
```

После этого при необходимости снова `git push`.

## 7. Частые операции

### Проверить историю

```powershell
git log --oneline --decorate --graph
```

```bash
git log --oneline --decorate --graph
```

### Убрать файл из staging

```powershell
git reset HEAD <file>
```

```bash
git reset HEAD <file>
```

### Отменить локальные незакоммиченные изменения

```powershell
git reset --hard HEAD
```

```bash
git reset --hard HEAD
```

Используйте осторожно: изменения будут потеряны.

### Откатить коммит безопасно (без переписывания истории)

```powershell
git revert <commit>
```

```bash
git revert <commit>
```

### Разрешение merge conflict

1. Сделайте merge/rebase.
2. Откройте конфликтующие файлы.
3. Удалите конфликтные маркеры и соберите корректный итог.
4. Добавьте файлы в staging и завершите коммит.

## 8. Мини-чеклист перед PR

- Ветка создана от актуального `master`.
- Изменения только по задаче.
- Нет случайных правок/пробелов/переформатирования.
- Нет изменений `RobustToolbox`.
- Код и контент соответствуют правилам Soyuz (`_Soyuz`, `DS14`-пометки для правок upstream-файлов).
- PR-шаблон заполнен.

## 9. Глоссарий

- **Commit**: зафиксированный снимок изменений.
- **Branch**: отдельная линия разработки.
- **Merge**: объединение изменений веток.
- **Conflict**: конфликт при объединении.
- **Fetch**: скачать изменения remote без применения.
- **Pull**: `fetch + merge`.
- **Push**: отправить локальные коммиты на remote.
- **PR**: запрос на merge вашей ветки в целевую ветку репозитория.
