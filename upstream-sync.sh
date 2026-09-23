#!/usr/bin/env bash
#
# Помощник для мерджа апстрима (fobos) в форке Soyuz.
# Не завязан конкретно на SS14 — просто набор git-примитивов вокруг rerere.
#
# Команды:
#   ./upstream-sync.sh setup
#       Разово: включить rerere и показывать общего предка в конфликтах (zdiff3).
#
#   ./upstream-sync.sh train <диапазон-коммитов>
#       Прогнать историю мерджей в диапазоне и научить rerere на уже
#       разрешённых в прошлом конфликтах. Работает только если в истории
#       реально ЕСТЬ старые merge-коммиты (не squash) с апстримом —
#       проверь: git log --merges
#       Пример: ./upstream-sync.sh train fobos/master
#       ВАЖНО: коммить/стэшни все локальные изменения перед запуском —
#       скрипт временно переключает HEAD между коммитами и делает hard reset.
#       Можно прерывать в любой момент (Ctrl+C, закрытие терминала, сбой,
#       segfault git'а) — всё уже изученное остаётся в rr-cache, прогресс
#       по коммитам пишется в .git/rerere-train-done, повторный запуск
#       с тем же (или другим) диапазоном продолжит с места остановки,
#       а не начнёт заново. Скрипт сам игнорирует SIGHUP (обрыв терминала
#       не убьёт процесс), но для реально долгого прогона всё равно лучше
#       запускать через nohup, чтобы не зависеть от держащегося окна:
#         nohup bash upstream-sync.sh train fobos/master > train.log 2>&1 &
#         tail -f train.log
#
#   ./upstream-sync.sh sync [remote] [branch]
#       git fetch + git merge с апстримом (remote по умолчанию "fobos",
#       branch по умолчанию "master"), с -X patience и с find-renames=90 —
#       поднятый порог схожести для определения переименований, чтобы
#       разные мелкие однотипные файлы (как meta.json у .rsi) реже
#       путались друг с другом как "один и тот же переименованный файл".
#       После неудачного мерджа печатает список конфликтующих файлов от
#       простых к сложным.
#
#   ./upstream-sync.sh resolve-assets
#       .png и .ogg принимаются СРАЗУ, без проверок (git add) — в SS14 это
#       всегда настоящие бинарники, разбираться незачем.
#       .svg, *.png.yml, *.svg.yml — текстовые форматы, поэтому проверяются:
#       если для пути уже настроен merge=binary/ours в .gitattributes,
#       конфликт разрешается сам и файл просто принимается; если маркеры
#       конфликта всё ещё внутри — файл не трогается и явно перечисляется
#       как пропущенный, а не портится слепым git add.
#
#   ./upstream-sync.sh resolve-renames
#       Чинит случаи, когда git при мердже перепутал переименования РАЗНЫХ
#       файлов между собой (типично для мелких однотипных meta.json —
#       конфликт тогда выглядит как <<<<<<< HEAD:путь1 / ||||||| база:путь2
#       / ======= / >>>>>>> апстрим:путь3 с ТРЕМЯ разными путями). Такой
#       конфликт разбирать построчно бессмысленно — это мешанина из
#       нескольких файлов, а не правки одного. Если "наш" путь лежит под
#       _Soyuz — просто восстанавливает его настоящее содержимое из HEAD
#       напрямую, в обход всей этой путаницы.
#
#   ./upstream-sync.sh resolve-deleted
#       Конфликты типа "добавлено/удалено" (git status показывает AA, AU,
#       UA, DU, UD или DD — а не обычный UU "обе стороны изменили
#       содержимое"). Часто это тот же побочный эффект путаницы с
#       переименованиями похожих meta.json. Разбирать содержимое смысла
#       нет — просто принимает то, что сейчас в рабочем дереве (git add),
#       а если файла там больше нет — снимает его с индекса (git rm --cached).
#
#   ./upstream-sync.sh resolve-map
#       Resources/Maps/_Soyuz/** — это только наши карты, апстрим их не
#       трогает: любой конфликт там просто заменяется на нашу версию (HEAD).
#       Resources/Maps/** в остальных папках — это НЕ наш контент, поэтому
#       там конфликт разрешается в пользу входящей версии.
#       В обоих случаях берётся содержимое напрямую из стадий индекса
#       (:2: — наша сторона, :3: — входящая), а не разбирается построчно —
#       карты и так идут через отдельный merge=mapping-merge-driver,
#       парсить его результат текстом смысла нет.
#
#   ./upstream-sync.sh resolve-marked [доп. маркер ...]
#       Разрешает конфликты, где наша сторона явно помечена как союзовская.
#       Для .yml и .cs действует отдельное, более широкое правило:
#         - если в НАШЕЙ стороне конфликта НЕТ вообще ни одного тега союза
#           (ни "#DS14-Soyuz", ни "-start"/"-end") — автоматически берётся
#           входящая версия, независимо от числа строк;
#         - если есть хотя бы ОДИН тег в нашей стороне — входящая версия
#           НИКОГДА не принимается автоматически (дальше решает уже
#           обычное правило ниже — может взять наше, может оставить как есть).
#       Для остальных файлов (не .yml/.cs) — прежнее правило:
#         - 1 строка с нашей стороны и 1 строка со входящей — если в них есть
#           "# DS14-Soyuz"/"#DS14-Soyuz", берётся наша, иначе входящая;
#         - блок в обёртке "#DS14-Soyuz-start" ... "#DS14-Soyuz-end", ИЛИ
#           несколько идущих подряд строк, КАЖДАЯ из которых индивидуально
#           помечена "#DS14-Soyuz" — берётся наш блок целиком, но только если
#           число строк кода в нём <= числа строк со входящей стороны.
#       Всё остальное не трогает. Файл без конфликтов после этого сам
#       добавляется в индекс. Доп. маркеры через пробел — добавятся к списку.
#
#   ./upstream-sync.sh resolve-locale
#       Только *.ftl. Без всяких тегов — чисто по числу строк в блоке
#       конфликта: если строк РОВНО поровну — берётся наша версия как есть.
#       Любая другая разница в числе строк (в любую сторону) — конфликт
#       не трогается.
#
#   ./upstream-sync.sh resolve-conflicts [доп. маркер ...]
#       Прогоняет ПОДРЯД все существующие в скрипте способы авторазрешения
#       (сейчас: resolve-renames + resolve-deleted + resolve-map +
#       resolve-assets + resolve-marked + resolve-locale) и в конце
#       печатает сводную таблицу — сколько конфликтов разрешил каждый
#       способ и сколько осталось. Это единая точка входа: если в скрипт
#       добавится ещё один resolve-* способ — впиши вызов его функции сюда
#       же одной строкой (и его счётчик — в сводку), и resolve-conflicts
#       начнёт применять и учитывать его тоже.
#
#   ./upstream-sync.sh check-duplicates
#       НЕ резолвер, а диагностика — ничего не меняет, только печатает
#       отчёт. Ищет дублирующиеся id среди Resources/Prototypes/**/*.yml
#       (в пределах одного type — совпадение id у РАЗНЫХ типов в SS14
#       нормально, поэтому сравнение идёт по паре type+id) и дублирующиеся
#       ключи среди Resources/Locale/**/*.ftl (в пределах одного языка —
#       совпадение ключа МЕЖДУ языками нормально). Стоит прогонять после
#       того, как конфликты разрешены (в том числе руками) — это частый
#       побочный эффект автоматического разрешения.
#
#   ./upstream-sync.sh status
#       Просто показать текущие незакрытые конфликты, отсортированные
#       по числу конфликтующих кусков (по возрастанию сложности).

set -euo pipefail
trap '' HUP   # закрытие терминала/обрыв связи не должны убивать долгий train
# Перед любой командой сам убирает .git/index.lock, если он старше 60 сек
# (остаток от аварийно прерванного git-процесса) — см. clear_stale_lock ниже.

triage() {
  git diff --name-only --diff-filter=U 2>/dev/null | while IFS= read -r f; do
    # {7,} — у git маркеры конфликта могут быть ДЛИННЕЕ 7 символов при
    # вложенных/рекурсивных мерджах с несколькими общими предками.
    n=$(grep -cE '^<{7,}' "$f" 2>/dev/null || true)
    n=${n:-0}
    printf '%4d  %s\n' "$n" "$f"
  done | sort -n
}

# --- строительные блоки авторазрешения конфликтов ---
# Каждый следующий такой блок оформляется как функция resolve_*,
# а resolve-conflicts просто вызывает их все по очереди.
#
# Каждая функция в конце пишет свои счётчики в глобальные переменные
# DSC_* (без "local") — resolve-conflicts потом суммирует их в сводку.

count_conflicted() {
  # Считает, сколько конфликтующих путей матчит переданный pathspec.
  local f n=0
  while IFS= read -r -d '' f; do
    n=$((n + 1))
  done < <(git diff --name-only --diff-filter=U -z -- "$@")
  echo "$n"
}

resolve_asset_conflicts() {
  # png и ogg — всегда настоящие бинарники в SS14, разбираться незачем:
  # принимаем текущее содержимое сразу, без построчных проверок.
  DSC_PNG=$(count_conflicted '*.png')
  [ "$DSC_PNG" -gt 0 ] && git_add_retry '*.png'
  DSC_OGG=$(count_conflicted '*.ogg')
  [ "$DSC_OGG" -gt 0 ] && git_add_retry '*.ogg'
  echo "  png: приняты сразу — $DSC_PNG"
  echo "  ogg: приняты сразу — $DSC_OGG"

  # А вот .svg/.png.yml/.svg.yml — текстовые форматы. Если для конкретного
  # пути НЕ настроен merge=binary/ours в .gitattributes, git может оставить
  # внутри файла настоящие маркеры конфликта — такие мы не трогаем не глядя.
  local f count=0 skipped=0
  while IFS= read -r -d '' f; do
    # {7,} — маркеры конфликта у git могут быть длиннее 7 символов при
    # вложенных/рекурсивных мерджах (несколько общих предков) — проверяем
    # "7 и больше", а не ровно 7, иначе такие конфликты можно случайно
    # пропустить и слепо принять файл с мусором внутри.
    if grep -IqE '^<{7,}' "$f" 2>/dev/null; then
      echo "  пропущено (внутри остались маркеры конфликта — добавь merge=binary/ours для этого пути в .gitattributes): $f"
      skipped=$((skipped + 1))
      continue
    fi
    git_add_retry "$f" && count=$((count + 1))
  done < <(git diff --name-only --diff-filter=U -z -- '*.svg' '*.png.yml' '*.svg.yml')
  DSC_SVGYML=$count
  DSC_SVGYML_SKIPPED=$skipped
  echo "  svg/png.yml/svg.yml: принято — $count, пропущено с маркерами внутри — $skipped"
}

resolve_marked_blocks() {
  python3 - "$gitdir" "$@" <<'PYEOF'
import os
import re
import subprocess
import sys
import time

GITDIR = sys.argv[1]
# Маркеры, которые означают "эту строку/блок трогать нельзя, это наша
# (Союз) правка". Можно дописать свои через аргументы командной строки.
LINE_MARKERS = ["# DS14-Soyuz", "#DS14-Soyuz"] + sys.argv[2:]
START_MARKERS = ["# DS14-Soyuz-start", "#DS14-Soyuz-start"]
END_MARKERS = ["# DS14-Soyuz-end", "#DS14-Soyuz-end"]
ALL_MARKERS = LINE_MARKERS + START_MARKERS + END_MARKERS
# Для .yml/.cs действует отдельное, более широкое правило (см. ниже).
YML_CS_EXT = (".yml", ".cs")

# {7,} — у git маркеры конфликта могут быть ДЛИННЕЕ 7 символов при
# вложенных/рекурсивных мерджах с несколькими общими предками.
RE_START = re.compile(r"^<{7,}")
RE_BASE = re.compile(r"^\|{7,}")
RE_MID = re.compile(r"^={7,}")
RE_END = re.compile(r"^>{7,}")


def git_add_retry(path, max_attempts=20):
    lockfile = os.path.join(GITDIR, "index.lock")
    for attempt in range(1, max_attempts + 1):
        result = subprocess.run(
            ["git", "add", "--", path], capture_output=True, text=True
        )
        if result.returncode == 0:
            return True
        stderr = result.stderr or ""
        if "did not match any files" in stderr:
            return True  # нечего добавлять — это не ошибка
        if "index.lock" not in stderr:
            print(stderr, end="", file=sys.stderr)
            return False
        if os.path.exists(lockfile):
            age = time.time() - os.path.getmtime(lockfile)
            if age > 60:
                try:
                    os.remove(lockfile)
                except OSError:
                    pass
                continue
        if attempt >= max_attempts:
            print(
                f"  не получилось добавить в индекс: {path} — index.lock не отпускает. "
                "Похоже, что-то реально держит репозиторий (открытый редактор коммита, "
                "GUI-клиент git, антивирус) — закрой это и попробуй снова.",
                file=sys.stderr,
            )
            return False
        time.sleep(0.5)
    return False


def conflicted_files():
    out = subprocess.check_output(
        ["git", "diff", "--name-only", "--diff-filter=U", "-z"],
        universal_newlines=True,
    )
    return [f for f in out.split("\x00") if f]


def marked_block(ours):
    """Проверяет, считается ли ours целиком "союзовским" блоком, и что
    именно считать "строками кода" для сравнения длины с входящими.
    Возвращает (is_marked, code_lines)."""
    if not ours:
        return False, []
    # Вариант 1: явная обёртка #DS14-Soyuz-start ... #DS14-Soyuz-end —
    # сами строки-обёртки в счёт "строк кода" не идут.
    if (any(m in ours[0] for m in START_MARKERS)
            and any(m in ours[-1] for m in END_MARKERS)):
        return True, ours[1:-1]
    # Вариант 2: каждая строка блока по отдельности помечена #DS14-Soyuz —
    # тогда весь блок целиком (включая эти строки) и есть "строки кода".
    if all(any(m in line for m in LINE_MARKERS) for line in ours):
        return True, ours
    return False, []


def resolve_block(ours, theirs, is_yml_cs):
    """Решает, что делать с одним блоком конфликта. Возвращает (True, lines)
    если разрешили, иначе (False, None)."""
    if is_yml_cs:
        # Отдельное, более широкое правило для .yml/.cs: если в нашей
        # стороне НЕТ вообще ни одного тега союза (в любом виде) — входящее
        # принимается автоматически, независимо от числа строк. Если есть
        # хотя бы один тег — входящее НИКОГДА не принимается автоматически,
        # даже если он не складывается в аккуратный "помеченный блок".
        has_any_tag = any(any(m in line for m in ALL_MARKERS) for line in ours)
        if not has_any_tag:
            return True, list(theirs)
        if len(ours) == 1 and len(theirs) == 1:
            return True, list(ours)
        is_marked, code_lines = marked_block(ours)
        if is_marked and len(code_lines) <= len(theirs):
            return True, list(ours)
        return False, None

    # Обычные файлы (не .yml/.cs) — прежнее поведение без изменений.
    if len(ours) == 1 and len(theirs) == 1:
        text = ours[0] + theirs[0]
        keep_ours = any(m in text for m in LINE_MARKERS)
        return True, [ours[0] if keep_ours else theirs[0]]
    is_marked, code_lines = marked_block(ours)
    if is_marked and len(code_lines) <= len(theirs):
        return True, list(ours)
    return False, None


def process_file(path):
    # newline='' — чтобы не трогать CRLF/LF, читаем и пишем байт-в-байт как есть
    try:
        with open(path, encoding="utf-8", errors="surrogateescape", newline="") as f:
            lines = f.readlines()
    except OSError:
        return None

    is_yml_cs = path.endswith(YML_CS_EXT)
    out = []
    i, n = 0, len(lines)
    resolved = 0
    remaining = 0
    changed = False

    while i < n:
        if RE_START.match(lines[i]):
            start = i
            ours, theirs = [], []
            j = i + 1

            while j < n and not RE_BASE.match(lines[j]) and not RE_MID.match(lines[j]):
                ours.append(lines[j])
                j += 1

            if j < n and RE_BASE.match(lines[j]):
                j += 1
                while j < n and not RE_MID.match(lines[j]):
                    j += 1  # содержимое общего предка (zdiff3) для решения не нужно

            if j < n and RE_MID.match(lines[j]):
                j += 1
            else:
                out.extend(lines[start:])
                i = n
                break

            while j < n and not RE_END.match(lines[j]):
                theirs.append(lines[j])
                j += 1

            if j >= n:
                out.extend(lines[start:])
                i = n
                break

            end = j  # индекс закрывающей строки

            resolved_here, chosen = resolve_block(ours, theirs, is_yml_cs)
            if resolved_here:
                out.extend(chosen)
                resolved += 1
                changed = True
            else:
                out.extend(lines[start:end + 1])
                remaining += 1

            i = end + 1
        else:
            out.append(lines[i])
            i += 1

    if changed:
        with open(path, "w", encoding="utf-8", errors="surrogateescape", newline="") as f:
            f.writelines(out)

    return resolved, remaining


total_resolved = 0
total_remaining = 0
any_resolved = False
for path in conflicted_files():
    result = process_file(path)
    if result is None:
        print(f"  пропущено (не читается как текст): {path}")
        continue

    resolved, remaining = result
    if resolved == 0 and remaining == 0:
        print(f"  нет текстовых маркеров конфликта (бинарный/структурный) — не трогаю: {path}")
        continue

    total_resolved += resolved
    total_remaining += remaining
    if resolved:
        any_resolved = True

    status = "полностью разрешён" if remaining == 0 else f"осталось {remaining} конфликтов"
    print(f"  {path}: помеченных блоков разрешено — {resolved}, {status}")

    if remaining == 0 and resolved:
        git_add_retry(path)

if not any_resolved:
    print("  помеченных конфликтов не найдено")

with open(os.path.join(GITDIR, ".dsresolve-count-marked"), "w") as f:
    f.write(f"{total_resolved} {total_remaining}\n")
PYEOF
  if [ -f "$gitdir/.dsresolve-count-marked" ]; then
    read -r DSC_MARKED DSC_MARKED_REMAINING < "$gitdir/.dsresolve-count-marked"
    rm -f "$gitdir/.dsresolve-count-marked"
  else
    DSC_MARKED=0
    DSC_MARKED_REMAINING=0
  fi
}

# Локализация (.ftl) — свой отдельный, простой критерий, без завязки на
# теги союза: если строк с обеих сторон конфликта поровну — берём нашу
# версию как есть. Если у входящей стороны РОВНО на одну строку больше —
# считаем, что это просто добавили один новый перевод/строку, и берём
# нашу версию ПЛЮС эту недостающую строку (довешиваем её в конец блока).
# Любая другая разница в числе строк — не трогаем, слишком похоже на
# настоящую переработку, а не просто добавление.
resolve_locale_conflicts() {
  python3 - "$gitdir" <<'PYEOF'
import os
import re
import subprocess
import sys
import time

GITDIR = sys.argv[1]

# {7,} — у git маркеры конфликта могут быть ДЛИННЕЕ 7 символов при
# вложенных/рекурсивных мерджах с несколькими общими предками.
RE_START = re.compile(r"^<{7,}")
RE_BASE = re.compile(r"^\|{7,}")
RE_MID = re.compile(r"^={7,}")
RE_END = re.compile(r"^>{7,}")


def git_add_retry(path, max_attempts=20):
    lockfile = os.path.join(GITDIR, "index.lock")
    for attempt in range(1, max_attempts + 1):
        result = subprocess.run(
            ["git", "add", "--", path], capture_output=True, text=True
        )
        if result.returncode == 0:
            return True
        stderr = result.stderr or ""
        if "did not match any files" in stderr:
            return True
        if "index.lock" not in stderr:
            print(stderr, end="", file=sys.stderr)
            return False
        if os.path.exists(lockfile):
            age = time.time() - os.path.getmtime(lockfile)
            if age > 60:
                try:
                    os.remove(lockfile)
                except OSError:
                    pass
                continue
        if attempt >= max_attempts:
            print(
                f"  не получилось добавить в индекс: {path} — index.lock не отпускает. "
                "Похоже, что-то реально держит репозиторий (открытый редактор коммита, "
                "GUI-клиент git, антивирус) — закрой это и попробуй снова.",
                file=sys.stderr,
            )
            return False
        time.sleep(0.5)
    return False


def conflicted_files():
    out = subprocess.check_output(
        ["git", "diff", "--name-only", "--diff-filter=U", "-z", "--", "*.ftl"],
        universal_newlines=True,
    )
    return [f for f in out.split("\x00") if f]


def resolve_block(ours, theirs):
    # Строго: только если строк с обеих сторон конфликта РОВНО поровну —
    # берём нашу версию как есть. Любая другая разница (в любую сторону)
    # конфликт не трогает.
    if len(ours) == len(theirs):
        return True, list(ours)
    return False, None


def process_file(path):
    try:
        with open(path, encoding="utf-8", errors="surrogateescape", newline="") as f:
            lines = f.readlines()
    except OSError:
        return None

    out = []
    i, n = 0, len(lines)
    resolved = 0
    remaining = 0
    changed = False

    while i < n:
        if RE_START.match(lines[i]):
            start = i
            ours, theirs = [], []
            j = i + 1

            while j < n and not RE_BASE.match(lines[j]) and not RE_MID.match(lines[j]):
                ours.append(lines[j])
                j += 1

            if j < n and RE_BASE.match(lines[j]):
                j += 1
                while j < n and not RE_MID.match(lines[j]):
                    j += 1

            if j < n and RE_MID.match(lines[j]):
                j += 1
            else:
                out.extend(lines[start:])
                i = n
                break

            while j < n and not RE_END.match(lines[j]):
                theirs.append(lines[j])
                j += 1

            if j >= n:
                out.extend(lines[start:])
                i = n
                break

            end = j

            resolved_here, chosen = resolve_block(ours, theirs)
            if resolved_here:
                out.extend(chosen)
                resolved += 1
                changed = True
            else:
                out.extend(lines[start:end + 1])
                remaining += 1

            i = end + 1
        else:
            out.append(lines[i])
            i += 1

    if changed:
        with open(path, "w", encoding="utf-8", errors="surrogateescape", newline="") as f:
            f.writelines(out)

    return resolved, remaining


total_resolved = 0
total_remaining = 0
any_resolved = False
for path in conflicted_files():
    result = process_file(path)
    if result is None:
        print(f"  пропущено (не читается как текст): {path}")
        continue

    resolved, remaining = result
    if resolved == 0 and remaining == 0:
        continue

    total_resolved += resolved
    total_remaining += remaining
    if resolved:
        any_resolved = True

    status = "полностью разрешён" if remaining == 0 else f"осталось {remaining} конфликтов"
    print(f"  {path}: разрешено — {resolved}, {status}")

    if remaining == 0 and resolved:
        git_add_retry(path)

if not any_resolved:
    print("  локализационных конфликтов не найдено")

with open(os.path.join(GITDIR, ".dsresolve-count-locale"), "w") as f:
    f.write(f"{total_resolved} {total_remaining}\n")
PYEOF
  if [ -f "$gitdir/.dsresolve-count-locale" ]; then
    read -r DSC_LOCALE DSC_LOCALE_REMAINING < "$gitdir/.dsresolve-count-locale"
    rm -f "$gitdir/.dsresolve-count-locale"
  else
    DSC_LOCALE=0
    DSC_LOCALE_REMAINING=0
  fi
}

# Не резолвер, а диагностика: ищет дублирующиеся id прототипов (в пределах
# одного type — SS14 допускает совпадение id у РАЗНЫХ типов, поэтому
# сравнение идёт именно по паре type+id) и дублирующиеся ключи локализации
# (в пределах одного языка — совпадение ключа МЕЖДУ языками нормально).
# Полезно прогнать после того, как конфликты разрешены (в том числе
# руками), чтобы поймать частый побочный эффект автоматического
# разрешения — два прототипа с одним id или два перевода с одним ключом.
check_duplicates() {
  python3 - <<'PYEOF'
import re
import subprocess
from collections import Counter, defaultdict


def tracked_files(patterns):
    out = subprocess.check_output(
        ["git", "ls-files", "-z", "--"] + list(patterns),
        universal_newlines=True,
    )
    return [f for f in out.split("\x00") if f]


YAML_ENTRY_RE = re.compile(r"(?m)^-\s*")
TYPE_RE = re.compile(r"(?m)^\s{0,2}type:\s*(\S+)")
ID_RE = re.compile(r"(?m)^\s{0,2}id:\s*(\S+)")


def scan_yaml(path):
    try:
        with open(path, encoding="utf-8", errors="ignore") as f:
            text = f.read()
    except OSError:
        return []
    entries = []
    for block in YAML_ENTRY_RE.split(text)[1:]:
        m_type = TYPE_RE.search(block)
        m_id = ID_RE.search(block)
        if m_type and m_id:
            ptype = m_type.group(1).strip().strip("\"'")
            pid = m_id.group(1).strip().strip("\"'")
            entries.append((ptype, pid))
    return entries


FTL_KEY_RE = re.compile(r"^([A-Za-z0-9_.\-]+)\s*=")


def scan_ftl(path):
    try:
        with open(path, encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()
    except OSError:
        return []
    keys = []
    for line in lines:
        # Ключ сообщения в FTL стоит в самом начале строки (без отступа);
        # строки с отступом — это атрибуты/продолжение предыдущего ключа
        # (".desc = ..."), а не отдельные ключи, их пропускаем.
        if not line.strip() or line[0] in " \t#":
            continue
        m = FTL_KEY_RE.match(line)
        if m:
            keys.append(m.group(1))
    return keys


def locale_lang(path):
    parts = path.split("/")
    if "Locale" in parts:
        idx = parts.index("Locale")
        if idx + 1 < len(parts):
            return parts[idx + 1]
    return "?"


def report(title, files, by_key):
    dupes = {k: v for k, v in by_key.items() if len(v) > 1}
    print(f"{title}: просканировано файлов — {len(files)}")
    if not dupes:
        print("  дубликатов не найдено")
        return
    print(f"  найдено дублирующихся — {len(dupes)}")
    for key, paths in sorted(dupes.items()):
        counts = Counter(paths)
        detail = ", ".join(f"{p} (x{n})" if n > 1 else p for p, n in counts.items())
        label = key if isinstance(key, str) else " / ".join(key)
        print(f"    {label}: {detail}")


yaml_files = tracked_files(["Resources/Prototypes/**/*.yml"])
yaml_by_key = defaultdict(list)
for path in yaml_files:
    for ptype, pid in scan_yaml(path):
        yaml_by_key[(ptype, pid)].append(path)
report("YAML-прототипы (id внутри одного type)", yaml_files, yaml_by_key)

print()

ftl_files = tracked_files(["Resources/Locale/**/*.ftl"])
ftl_by_key = defaultdict(list)
for path in ftl_files:
    lang = locale_lang(path)
    for key in scan_ftl(path):
        ftl_by_key[(lang, key)].append(path)
report("FTL-локализация (ключ внутри одного языка)", ftl_files, ftl_by_key)
PYEOF
}

# Иногда git пытается смерджить контент по переименованию и путает между
# собой РАЗНЫЕ файлы — обычно с мелкими однотипными json/yml (как meta.json
# у .rsi), у которых похожая структура. Признак: в маркере конфликта после
# имени ветки указан путь через двоеточие (<<<<<<< HEAD:путь), и путь этот
# на "нашей" и "их" стороне — РАЗНЫЙ. Разбирать построчно такой конфликт
# бессмысленно — это мешанина из 2-3 разных файлов, а не правки одного.
# Если "наш" файл при этом лежит под _Soyuz — он точно наш, и мы просто
# восстанавливаем его настоящее содержимое напрямую из HEAD в обход мешанины.
resolve_rename_conflicts() {
  local f count=0 ours_path
  while IFS= read -r -d '' f; do
    # {7,} — маркеры длиннее 7 символов при вложенных мерджах (см. выше)
    ours_path=$(grep -m1 -E '^<{7,} [^:]+:' "$f" 2>/dev/null | sed -E 's/^<{7,} [^:]+://' || true)
    [ -z "$ours_path" ] && continue

    case "$ours_path" in
      *_Soyuz/*|*_Soyuz)
        if git show "HEAD:$ours_path" > "$f.dsresolve.tmp" 2>/dev/null; then
          mv "$f.dsresolve.tmp" "$f"
          if git_add_retry "$f"; then
            count=$((count + 1))
            echo "  переименование перепуталось (разные файлы смешались) — восстановил из HEAD как есть: $f"
          fi
        else
          rm -f "$f.dsresolve.tmp"
        fi
        ;;
    esac
  done < <(git diff --name-only --diff-filter=U -z)
  DSC_RENAMES=$count
  echo "  путаница с переименованиями (_Soyuz): исправлено — $count"
}

# Конфликты типа "добавлено/удалено" (AA/AU/UA/DU/UD/DD в git status) — это
# НЕ "обе стороны поменяли содержимое" (UU), а история вида "файл появился
# только на одной стороне" или "удалён на одной, изменён на другой". Часто
# это тот же побочный эффект путаницы с переименованиями похожих meta.json —
# разбирать содержимое смысла нет, просто принимаем то, что сейчас лежит в
# рабочем дереве (git add), а если файла там больше нет — снимаем с индекса.
resolve_deleted_conflicts() {
  local count=0 skipped=0 entry xy path
  while IFS= read -r -d '' entry; do
    xy="${entry:0:2}"
    path="${entry:3}"
    case "$xy" in
      UU) continue ;;   # обычный конфликт содержимого — не сюда, это для других resolve-*
      AA|AU|UA|DU|UD|DD)
        if [ ! -e "$path" ]; then
          git rm -q --cached -- "$path" >/dev/null 2>&1 && count=$((count + 1))
          continue
        fi
        # AA (add/add) — тоже текстовый файл, и git мог попытаться слить
        # содержимое построчно, оставив внутри настоящие маркеры конфликта.
        # Слепо такое принимать нельзя — та же логика, что и в resolve-assets.
        if grep -IqE '^<{7,}' "$path" 2>/dev/null; then
          echo "  пропущено (внутри остались маркеры конфликта, $xy): $path"
          skipped=$((skipped + 1))
          continue
        fi
        git_add_retry "$path" && count=$((count + 1))
        ;;
      *) continue ;;
    esac
  done < <(git status --porcelain=v1 -z)
  DSC_DELETED=$count
  DSC_DELETED_SKIPPED=$skipped
  echo "  добавлено/удалено (не both-modified) конфликтов: обработано — $count, пропущено с маркерами — $skipped"
}

# Карты (Resources/Maps) обычно проходят через отдельный семантический
# merge=mapping-merge-driver — разбирать текст файла после его работы
# смысла нет в любом случае. Наши собственные карты под _Soyuz апстрим не
# трогает вообще, значит любой конфликт там — просто берём нашу сторону.
# А карты в остальных папках (общие/чужие) — не наш контент, там при
# конфликте логичнее взять входящую версию, а не упрямо тащить свою.
# Берём напрямую из стадий индекса (:2: — наша, :3: — входящая), а не
# из HEAD/MERGE_HEAD — так работает одинаково что при merge, что при
# rebase/cherry-pick, и не зависит от переименований по пути.
resolve_map_conflicts() {
  local f count_ours=0 count_theirs=0
  while IFS= read -r -d '' f; do
    case "$f" in
      Resources/Maps/_Soyuz/*)
        if git show ":2:$f" > "$f.dsresolve.tmp" 2>/dev/null; then
          mv "$f.dsresolve.tmp" "$f"
          git_add_retry "$f" && count_ours=$((count_ours + 1))
        else
          rm -f "$f.dsresolve.tmp"
        fi
        ;;
      *)
        if git show ":3:$f" > "$f.dsresolve.tmp" 2>/dev/null; then
          mv "$f.dsresolve.tmp" "$f"
          git_add_retry "$f" && count_theirs=$((count_theirs + 1))
        else
          rm -f "$f.dsresolve.tmp"
        fi
        ;;
    esac
  done < <(git diff --name-only --diff-filter=U -z -- 'Resources/Maps/**')
  DSC_MAP_OURS=$count_ours
  DSC_MAP_THEIRS=$count_theirs
  echo "  карты: наша версия (_Soyuz) — $count_ours, входящая (остальные) — $count_theirs"
}

# Если предыдущий git-процесс (например, внутри train) был убит крашем,
# Ctrl+C-мимо-обработчика или чем угодно ещё, .git/index.lock может
# остаться висеть — и тогда ЛЮБАЯ следующая git-команда падает с
# "Unable to create '.../index.lock': File exists", пока файл не уберут
# руками. Если файлу больше 60 секунд — почти наверняка это именно
# брошенный лок, а не реально работающий сейчас git, и его можно смело
# убрать самим. Возвращает 0, если можно спокойно продолжать (лока нет
# или он был старым и его снесли), и 1, если лок свежий и его не трогали —
# решение, что делать дальше, остаётся за вызывающим кодом.
clear_stale_lock() {
  local lockfile="$1/index.lock"
  [ -f "$lockfile" ] || return 0

  local now mtime age
  now=$(date +%s)
  mtime=$(stat -c %Y "$lockfile" 2>/dev/null || stat -f %m "$lockfile" 2>/dev/null || echo "$now")
  age=$((now - mtime))

  if [ "$age" -gt 60 ]; then
    echo "Нашёл .git/index.lock старше $age сек. — похоже, это остаток от аварийно прерванного git. Удаляю." >&2
    rm -f "$lockfile"
    return 0
  fi
  return 1
}

# git add с повтором: если в момент вызова index.lock держит что-то ещё
# (антивирус, открытый редактор коммита, GUI-клиент git), не падаем сразу,
# а пробуем убрать лок (если он уже устарел) и повторяем с паузой — до 20
# попыток (~10 секунд), прежде чем сдаться с понятным сообщением.
git_add_retry() {
  local path="$1" attempt=0 out
  while true; do
    if out=$(git add -- "$path" 2>&1); then
      return 0
    fi
    if [[ "$out" == *"did not match any files"* ]]; then
      return 0   # нечего добавлять (например, .png-конфликтов нет вообще) — это не ошибка
    fi
    if [[ "$out" != *"index.lock"* ]]; then
      echo "$out" >&2
      return 1
    fi
    attempt=$((attempt + 1))
    clear_stale_lock "$gitdir" >/dev/null 2>&1 || true
    if [ "$attempt" -ge 20 ]; then
      echo "  не получилось добавить в индекс: $path — index.lock не отпускает уже $attempt попыток. Похоже, что-то реально держит репозиторий (открытый редактор коммита, GUI-клиент git, антивирус) — закрой это и попробуй снова." >&2
      return 1
    fi
    sleep 0.5
  done
}

gitdir=$(git rev-parse --git-dir 2>/dev/null) || { echo "Похоже, мы не внутри git-репозитория." >&2; exit 1; }

wait_attempt=0
while ! clear_stale_lock "$gitdir" && [ -f "$gitdir/index.lock" ]; do
  wait_attempt=$((wait_attempt + 1))
  if [ "$wait_attempt" -ge 20 ]; then
    echo "ВНИМАНИЕ: .git/index.lock существует и не отпускает уже ~$((wait_attempt / 2)) сек." >&2
    echo "Похоже, git реально работает прямо сейчас (в другом окне, открытый редактор сообщения коммита, GUI-клиент) — не трогаю файл. Закрой это и попробуй снова." >&2
    exit 1
  fi
  sleep 0.5
done

cmd="${1:-}"

case "$cmd" in
  setup)
    git config rerere.enabled true
    git config rerere.autoupdate true
    git config merge.conflictstyle zdiff3
    echo "rerere включён. Конфликты теперь будут показывать общего предка (стиль zdiff3) — удобнее понимать, что поменялось с обеих сторон."
    ;;

  train)
    range="${2:?Укажи диапазон коммитов, например: fobos/master}"

    branch=$(git symbolic-ref -q --short HEAD || true)
    orig_head=$(git rev-parse HEAD)
    mkdir -p "$gitdir/rr-cache"

    # Чекпоинт: уже обработанные коммиты (не переобучаемся на них повторно).
    # Общий для всех диапазонов — коммит, once проверенный, второй раз не нужен.
    donefile="$gitdir/rerere-train-done"
    touch "$donefile"

    cleanup() {
      git merge --abort >/dev/null 2>&1 || true
      git reset -q --hard >/dev/null 2>&1 || true
      if [ -n "$branch" ]; then
        git checkout -q "$branch" >/dev/null 2>&1 || true
      else
        git checkout -q "$orig_head" >/dev/null 2>&1 || true
      fi
    }
    trap 'echo; echo "Прервано. Всё изученное до сих пор уже в rr-cache и в $donefile — просто запусти train с тем же диапазоном ещё раз, чтобы продолжить с этого места. Возвращаюсь на исходную ветку..."; cleanup; exit 130' INT TERM

    total=$(git rev-list --merges --count "$range")
    echo "Мердж-коммитов в диапазоне: $total."
    echo "Можно прервать в любой момент (Ctrl+C) — уже изученное не потеряется, повторный запуск продолжит с места остановки."

    n=0
    skipped=0
    while read -r commit parent1 other_parents; do
      n=$((n + 1))

      if grep -Fxq "$commit" "$donefile"; then
        skipped=$((skipped + 1))
        continue
      fi

      git checkout -q "${parent1}^0"

      if git merge --no-gpg-sign $other_parents >/dev/null 2>&1; then
        :   # смерджилось само, учить нечему
      elif [ -s "$gitdir/MERGE_RR" ]; then
        printf '  [%d/%d] учусь на: %s\n' "$n" "$total" "$(git show -s --format='%h %s' "$commit")"
        git rerere                        # запомнить конфликт как есть
        git checkout -q "$commit" -- .    # подставить реально готовое решение
        git rerere                        # запомнить это решение
      fi

      git reset -q --hard   # вернуть рабочую копию в чистое состояние
      echo "$commit" >> "$donefile"
    done < <(git rev-list --parents --merges "$range")

    trap - INT TERM
    cleanup

    [ "$skipped" -gt 0 ] && echo "Пропущено уже изученных ранее: $skipped."
    echo "Готово. База знаний rerere пополнена — дальше при таких же конфликтах git будет разрешать их сам."
    echo "Чтобы начать обучение с нуля (забыть прогресс): rm '$donefile'"
    ;;

  sync)
    remote="${2:-fobos}"
    branch="${3:-master}"

    echo "Фетчу $remote/$branch..."
    git fetch "$remote" "$branch"

    echo "Мерджу..."
    if git merge "$remote/$branch" -X patience -X find-renames=90; then
      echo "Смерджилось без конфликтов."
    else
      echo
      echo "Есть конфликты. Файлы от простых к сложным (по числу конфликтующих кусков):"
      triage
    fi
    ;;

  resolve-assets)
    resolve_asset_conflicts
    ;;

  resolve-renames)
    resolve_rename_conflicts
    ;;

  resolve-deleted)
    resolve_deleted_conflicts
    ;;

  resolve-map)
    resolve_map_conflicts
    ;;

  resolve-marked)
    shift || true
    resolve_marked_blocks "$@"
    ;;

  resolve-locale)
    resolve_locale_conflicts
    ;;

  check-duplicates)
    check_duplicates
    ;;

  resolve-conflicts)
    shift || true
    DSC_RENAMES=0 DSC_DELETED=0 DSC_DELETED_SKIPPED=0
    DSC_MAP_OURS=0 DSC_MAP_THEIRS=0
    DSC_PNG=0 DSC_OGG=0 DSC_SVGYML=0 DSC_SVGYML_SKIPPED=0
    DSC_MARKED=0 DSC_MARKED_REMAINING=0
    DSC_LOCALE=0 DSC_LOCALE_REMAINING=0

    echo "== переименования: чиним перепутанные git rename для _Soyuz =="
    resolve_rename_conflicts
    echo
    echo "== добавлено/удалено: конфликты не-both-modified =="
    resolve_deleted_conflicts
    echo
    echo "== карты: наша версия для _Soyuz, входящая — для остальных =="
    resolve_map_conflicts
    echo
    echo "== ассеты: png/ogg сразу, svg/png.yml/svg.yml — с проверкой =="
    resolve_asset_conflicts
    echo
    echo "== Помеченные (#DS14-Soyuz / -start.../-end) конфликты =="
    resolve_marked_blocks "$@"
    echo
    echo "== Локализация (.ftl) =="
    resolve_locale_conflicts
    echo

    remaining_files=0
    f_tmp=""
    while IFS= read -r f_tmp; do
      remaining_files=$((remaining_files + 1))
    done < <(git diff --name-only --diff-filter=U 2>/dev/null)

    total=$((DSC_RENAMES + DSC_DELETED + DSC_MAP_OURS + DSC_MAP_THEIRS + DSC_PNG + DSC_OGG + DSC_SVGYML + DSC_MARKED + DSC_LOCALE))

    echo "===== Итого разрешено конфликтов: $total ====="
    echo "  переименования (_Soyuz):        $DSC_RENAMES"
    echo "  добавлено/удалено:              $DSC_DELETED  (пропущено с маркерами: $DSC_DELETED_SKIPPED)"
    echo "  карты — наша версия (_Soyuz):    $DSC_MAP_OURS"
    echo "  карты — входящая (остальные):    $DSC_MAP_THEIRS"
    echo "  png:                             $DSC_PNG"
    echo "  ogg:                             $DSC_OGG"
    echo "  svg/png.yml/svg.yml:             $DSC_SVGYML  (пропущено с маркерами: $DSC_SVGYML_SKIPPED)"
    echo "  помеченные блоки (#DS14-Soyuz):  $DSC_MARKED  (не подошло под правило: $DSC_MARKED_REMAINING)"
    echo "  локализация (.ftl):              $DSC_LOCALE  (не подошло под правило: $DSC_LOCALE_REMAINING)"
    echo "  ------------------------------------------"
    echo "  осталось конфликтующих файлов:   $remaining_files"
    if [ "$remaining_files" -gt 0 ]; then
      echo
      echo "Осталось разобрать вручную:"
      triage
    fi
    ;;

  status)
    triage
    ;;

  *)
    echo "Использование: $0 {setup|train <диапазон>|sync [remote] [branch]|resolve-assets|resolve-renames|resolve-deleted|resolve-map|resolve-marked [доп. маркеры...]|resolve-locale|resolve-conflicts [доп. маркеры...]|check-duplicates|status}"
    exit 1
    ;;
esac