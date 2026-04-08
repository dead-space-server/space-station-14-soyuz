#!/usr/bin/env python3

from __future__ import annotations

import argparse
import base64
import re
from pathlib import Path

HEADER_RE = re.compile(r"(?mi)^\s*(?::cl:|🆑)\s*$")
SECTION_RE = re.compile(
    r"^\s*[-*]?\s*(Добавлено|Удалено|Изменено|Исправлено)\s*:\s*(.*)$"
)
COMMENT_RE = re.compile(r"<!--.*?-->", re.DOTALL)

SECTION_TYPES = {
    "Добавлено": "Add",
    "Удалено": "Remove",
    "Изменено": "Tweak",
    "Исправлено": "Fix",
}

TYPE_EMOJIS = {
    "Add": "🆕",
    "Remove": "❌",
    "Tweak": "⚒️",
    "Fix": "🐛",
}


def load_body(args: argparse.Namespace) -> str:
    if args.body_base64 is not None:
        return base64.b64decode(args.body_base64).decode("utf-8", errors="replace")

    if args.body_file is not None:
        return Path(args.body_file).read_text(encoding="utf-8")

    raise ValueError("Either --body-base64 or --body-file must be provided.")


def parse_changes(body: str) -> list[dict[str, str]]:
    clean_body = COMMENT_RE.sub("", body)
    lines = clean_body.splitlines()

    marker_index = next(
        (index for index, line in enumerate(lines) if HEADER_RE.match(line)),
        None,
    )

    if marker_index is None:
        return []

    changes: list[dict[str, str]] = []
    current_type: str | None = None
    current_parts: list[str] = []

    def flush_current() -> None:
        nonlocal current_type, current_parts

        if current_type is None:
            return

        message = " ".join(part for part in current_parts if part).strip()
        message = re.sub(r"\s+", " ", message)

        if message:
            changes.append({"type": current_type, "message": message})

        current_type = None
        current_parts = []

    for raw_line in lines[marker_index + 1 :]:
        if not raw_line.strip():
            continue

        match = SECTION_RE.match(raw_line)
        if match is not None:
            flush_current()
            current_type = SECTION_TYPES[match.group(1)]
            first_part = match.group(2).strip()
            current_parts = [first_part] if first_part else []
            continue

        if current_type is None:
            continue

        continuation = re.sub(r"^\s*[-*]?\s*", "", raw_line).strip()
        if continuation:
            current_parts.append(continuation)

    flush_current()
    return changes


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


def render_discord(args: argparse.Namespace) -> int:
    changes = parse_changes(load_body(args))
    output_path = Path(args.output_file)

    if not changes:
        print("No changelog entries found in PR body.")
        if output_path.exists():
            output_path.unlink()
        return 0

    pr_link = f"[#{args.pr}](<https://github.com/{args.repo}/pull/{args.pr}>)"
    lines = [f"**{args.author}** updated:"]

    for change in changes:
        emoji = TYPE_EMOJIS.get(change["type"], "✅")
        lines.append(f"{emoji} - {change['message']} ({pr_link})")

    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Rendered Discord message: {output_path}")
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
    discord_parser.add_argument("--output-file", required=True)
    discord_parser.set_defaults(func=render_discord)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
