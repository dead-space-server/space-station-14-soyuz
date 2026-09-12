#!/usr/bin/env bash
#
# Помощник для мерджа апстрима в форке.
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
#       Пример: ./upstream-sync.sh train origin/master
#       ВАЖНО: коммить/стэшни все локальные изменения перед запуском —
#       скрипт временно переключает HEAD между коммитами и делает hard reset.
#
#   ./upstream-sync.sh sync [remote] [branch]
#       git fetch + git merge с апстримом (remote по умолчанию "fobos",
#       branch по умолчанию "master"), с -X patience для чуть менее кривых
#       мерджей. После неудачного мерджа печатает список конфликтующих
#       файлов от простых к сложным.
#
#   ./upstream-sync.sh resolve-single-line [доп. маркер ...]
#       Ищет однострочные конфликты (ровно 1 строка с нашей стороны и
#       ровно 1 строка со стороны входящих изменений). Если в строках
#       конфликта встречается "# DS14-Soyuz" или "#DS14-Soyuz" — берёт
#       НАШУ версию, иначе — берёт ВХОДЯЩУЮ. Многострочные конфликты не
#       трогает вообще. Файл, где после этого конфликтов не осталось,
#       автоматически добавляется в индекс (git add). Можно передать
#       дополнительные маркеры через пробел — они добавятся к списку.
#       Пример: ./upstream-sync.sh resolve-single-line
#
#   ./upstream-sync.sh status
#       Просто показать текущие незакрытые конфликты, отсортированные
#       по числу конфликтующих кусков (по возрастанию сложности).

set -euo pipefail

triage() {
  git diff --name-only --diff-filter=U 2>/dev/null | while IFS= read -r f; do
    n=$(grep -c '^<<<<<<< ' "$f" 2>/dev/null || true)
    n=${n:-0}
    printf '%4d  %s\n' "$n" "$f"
  done | sort -n
}

cmd="${1:-}"

case "$cmd" in
  setup)
    git config rerere.enabled true
    git config rerere.autoupdate true
    git config merge.conflictstyle zdiff3
    echo "rerere включён. Конфликты теперь будут показывать общего предка (стиль zdiff3) — удобнее понимать, что поменялось с обеих сторон."
    ;;

  train)
    range="${2:?Укажи диапазон коммитов, например: origin/master}"

    branch=$(git symbolic-ref -q --short HEAD || true)
    orig_head=$(git rev-parse HEAD)
    gitdir=$(git rev-parse --git-dir)
    mkdir -p "$gitdir/rr-cache"

    echo "Прохожу по истории мерджей в диапазоне: $range"
    git rev-list --parents "$range" | while read -r commit parent1 other_parents; do
      [ -z "$other_parents" ] && continue   # не мердж-коммит — пропускаем

      git checkout -q "${parent1}^0"

      if git merge --no-gpg-sign $other_parents >/dev/null 2>&1; then
        continue   # смерджилось само, учить нечему
      fi

      if [ -s "$gitdir/MERGE_RR" ]; then
        echo "  учусь на: $(git show -s --format='%h %s' "$commit")"
        git rerere                        # запомнить конфликт как есть
        git checkout -q "$commit" -- .    # подставить реально готовое решение
        git rerere                        # запомнить это решение
      fi

      git reset -q --hard   # вернуть рабочую копию в чистое состояние
    done

    if [ -n "$branch" ]; then
      git checkout -q "$branch"
    else
      git checkout -q "$orig_head"
    fi
    echo "Готово. База знаний rerere пополнена — дальше при таких же конфликтах git будет разрешать их сам."
    ;;

  sync)
    remote="${2:-fobos}"
    branch="${3:-master}"

    echo "Фетчу $remote/$branch..."
    git fetch "$remote" "$branch"

    echo "Мерджу..."
    if git merge "$remote/$branch" -X patience; then
      echo "Смерджилось без конфликтов."
    else
      echo
      echo "Есть конфликты. Файлы от простых к сложным (по числу конфликтующих кусков):"
      triage
    fi
    ;;

  resolve-single-line)
    shift || true
    python3 - "$@" <<'PYEOF'
import subprocess
import sys

# Маркеры, которые означают "эту строку трогать нельзя, это наша (Союз) правка".
# Можно дописать свои через аргументы командной строки скрипта.
MARKERS = ["# DS14-Soyuz", "#DS14-Soyuz"] + sys.argv[1:]


def conflicted_files():
    out = subprocess.check_output(
        ["git", "diff", "--name-only", "--diff-filter=U", "-z"],
        universal_newlines=True,
    )
    return [f for f in out.split("\x00") if f]


def process_file(path):
    # newline='' — чтобы не трогать CRLF/LF, читаем и пишем байт-в-байт как есть
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
        if lines[i].startswith("<<<<<<<"):
            start = i
            ours, theirs = [], []
            j = i + 1

            while j < n and not lines[j].startswith("|||||||") and not lines[j].startswith("======="):
                ours.append(lines[j])
                j += 1

            if j < n and lines[j].startswith("|||||||"):
                j += 1
                while j < n and not lines[j].startswith("======="):
                    j += 1  # содержимое общего предка (zdiff3) для решения не нужно

            if j < n and lines[j].startswith("======="):
                j += 1
            else:
                out.extend(lines[start:])
                i = n
                break

            while j < n and not lines[j].startswith(">>>>>>>"):
                theirs.append(lines[j])
                j += 1

            if j >= n:
                out.extend(lines[start:])
                i = n
                break

            end = j  # индекс строки ">>>>>>>"

            if len(ours) == 1 and len(theirs) == 1:
                text = ours[0] + theirs[0]
                keep_ours = any(m in text for m in MARKERS)
                out.append(ours[0] if keep_ours else theirs[0])
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

    if resolved:
        any_resolved = True

    status = "полностью разрешён" if remaining == 0 else f"осталось {remaining} многострочных конфликтов"
    print(f"  {path}: однострочных разрешено — {resolved}, {status}")

    if remaining == 0 and resolved:
        subprocess.run(["git", "add", path], check=True)

if not any_resolved:
    print("Однострочных конфликтов не найдено — либо их нет, либо все уже сложнее одной строки.")
PYEOF
    ;;

  status)
    triage
    ;;

  *)
    echo "Использование: $0 {setup|train <диапазон>|sync [remote] [branch]|resolve-single-line [доп. маркеры...]|status}"
    exit 1
    ;;
esac
