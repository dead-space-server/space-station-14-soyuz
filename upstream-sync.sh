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

  status)
    triage
    ;;

  *)
    echo "Использование: $0 {setup|train <диапазон>|sync [remote] [branch]|status}"
    exit 1
    ;;
esac
