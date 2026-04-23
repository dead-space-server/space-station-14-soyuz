#!/usr/bin/env python3

from __future__ import annotations

import argparse
import base64
import json
import re
from collections import defaultdict
from pathlib import Path

HEADER_RE = re.compile(r"(?mi)^\s*(?::cl:|🆑)\s*$")
COMMENT_RE = re.compile(r"<!--.*?-->", re.DOTALL)

SECTION_LABELS = {
    "добавлено": "Add",
    "удалено": "Remove",
    "изменено": "Tweak",
    "исправлено": "Fix",
    "add": "Add",
    "remove": "Remove",
    "removed": "Remove",
    "tweak": "Tweak",
    "changed": "Tweak",
    "change": "Tweak",
    "fix": "Fix",
    "fixed": "Fix",
}

SECTION_TITLES = {
    "Add": "🆕 Добавлено",
    "Remove": "❌ Удалено",
    "Tweak": "🛠️ Изменено",
    "Fix": "🐛 Исправлено",
}

SECTION_ORDER = ("Add", "Remove", "Tweak", "Fix")
DISCORD_FIELD_LIMIT = 1024


def load_body(args: argparse.Namespace) -> str:
    if args.body_base64 is not None:
        return base64.b64decode(args.body_base64).decode("utf-8", errors="replace")

    if args.body_file is not None:
        raw = Path(args.body_file).read_bytes()
        for encoding in ("utf-8", "utf-8-sig", "cp1251"):
            try:
                return raw.decode(encoding)
            except UnicodeDecodeError:
                continue

        return raw.decode("utf-8", errors="replace")

    raise ValueError("Either --body-base64 or --body-file must be provided.")


def clean_body(body: str) -> str:
    return COMMENT_RE.sub("", body).replace("\r\n", "\n").replace("\r", "\n")


def normalize_section(label: str) -> str | None:
    key = re.sub(r"\s+", " ", label.strip().lower())
    return SECTION_LABELS.get(key)


def parse_changes(body: str) -> list[dict[str, str]]:
    clean = clean_body(body)
    lines = clean.split("\n")

    marker_index = next(
        (index for index, line in enumerate(lines) if HEADER_RE.match(line)),
        None,
    )

    if marker_index is None:
        return []

    changes: list[dict[str, str]] = []
    current_type: str | None = None

    for raw_line in lines[marker_index + 1 :]:
        stripped = raw_line.strip()
        if not stripped:
            current_type = None
            continue

        line = re.sub(r"^\s*[-*]\s*", "", raw_line).strip()
        match = re.match(r"^(?P<label>[^:]+):\s*(?P<message>.*)$", line)
        if match:
            current_type = normalize_section(match.group("label"))
            if current_type is None:
                continue

            message = re.sub(r"\s+", " ", match.group("message")).strip()
            if message:
                changes.append({"type": current_type, "message": message})
            continue

        if current_type is None:
            continue

        message = re.sub(r"^\s*[-*]\s*", "", raw_line).strip()
        message = re.sub(r"\s+", " ", message)
        if message:
            changes.append({"type": current_type, "message": message})

    return changes


def group_changes(changes: list[dict[str, str]]) -> dict[str, list[str]]:
    grouped: dict[str, list[str]] = defaultdict(list)
    for change in changes:
        grouped[change["type"]].append(change["message"])
    return grouped


def update_yaml(args: argparse.Namespace) -> int:
    import yaml

    changes = parse_changes(load_body(args))
    if not changes:
        print("No changelog entries found in PR body.")
        return 0

    changelog_path = Path(args.changelog_file)
    if changelog_path.exists() and changelog_path.stat().st_size > 0:
        data = yaml.safe_load(changelog_path.read_text(encoding="utf-8")) or {}
    else:
        data = {}

    entries = data.get("Entries", [])
    last_id = max((entry.get("id", 0) for entry in entries), default=0)
    entries.append(
        {
            "id": last_id + 1,
            "author": args.author,
            "time": args.time,
            "pr": args.pr,
            "changes": changes,
        }
    )
    data["Entries"] = entries

    changelog_path.write_text(
        yaml.safe_dump(data, allow_unicode=True, sort_keys=False, indent=2),
        encoding="utf-8",
    )
    print(f"Updated changelog: {changelog_path}")
    return 0


def build_embed_fields(changes: list[dict[str, str]]) -> list[dict[str, object]]:
    grouped = group_changes(changes)
    fields: list[dict[str, object]] = []

    for change_type in SECTION_ORDER:
        messages = grouped.get(change_type)
        if not messages:
            continue

        chunk_lines: list[str] = []
        current_length = 0

        for message in messages:
            line = f"• {message}"
            additional = len(line) + (1 if chunk_lines else 0)

            if chunk_lines and current_length + additional > DISCORD_FIELD_LIMIT:
                fields.append(
                    {
                        "name": SECTION_TITLES[change_type],
                        "value": "\n".join(chunk_lines),
                        "inline": False,
                    }
                )
                chunk_lines = [line]
                current_length = len(line)
                continue

            chunk_lines.append(line)
            current_length += additional

        if chunk_lines:
            fields.append(
                {
                    "name": SECTION_TITLES[change_type],
                    "value": "\n".join(chunk_lines),
                    "inline": False,
                }
            )

    return fields


def render_discord(args: argparse.Namespace) -> int:
    changes = parse_changes(load_body(args))
    output_path = Path(args.output_file)

    if not changes:
        print("No changelog entries found in PR body.")
        if output_path.exists():
            output_path.unlink()
        return 0

    title = args.pr_title.strip() if args.pr_title else f"Изменение (PR #{args.pr})"
    description = f"Список изменений из [PR #{args.pr}](https://github.com/{args.repo}/pull/{args.pr})"

    payload = {
        "username": args.username,
        "allowed_mentions": {"parse": []},
        "embeds": [
            {
                "title": title,
                "url": f"https://github.com/{args.repo}/pull/{args.pr}",
                "description": description,
                "fields": build_embed_fields(changes),
                "color": 5763719,
                "footer": {
                    "text": f"Автор: {args.author} • PR #{args.pr}",
                },
                "timestamp": args.time,
            }
        ],
    }

    output_path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(f"Rendered Discord payload: {output_path}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--body-base64")
    common.add_argument("--body-file")

    update_parser = subparsers.add_parser("update-yaml", parents=[common])
    update_parser.add_argument("--author", required=True)
    update_parser.add_argument("--pr", required=True, type=int)
    update_parser.add_argument("--time", required=True)
    update_parser.add_argument("--changelog-file", required=True)
    update_parser.set_defaults(func=update_yaml)

    discord_parser = subparsers.add_parser("render-discord", parents=[common])
    discord_parser.add_argument("--author", required=True)
    discord_parser.add_argument("--pr", required=True, type=int)
    discord_parser.add_argument("--repo", required=True)
    discord_parser.add_argument("--pr-title")
    discord_parser.add_argument("--time", required=True)
    discord_parser.add_argument("--username", default="Союз-1")
    discord_parser.add_argument("--output-file", required=True)
    discord_parser.set_defaults(func=render_discord)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
