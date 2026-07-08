#!/usr/bin/env python3

"""
Sends DS14 changelog updates to a Discord webhook after a successful Publish run.

The script compares the current changelog with the changelog from the previous
successful Publish workflow run, then posts all new entries in Discord-sized
messages.
"""

import os
import re
import time
from pathlib import Path
from typing import Any, Iterable, Optional
from datetime import datetime, timedelta, timezone

import requests
import yaml

DEBUG = False
DEBUG_CHANGELOG_FILE_OLD = Path("Resources/Changelog/Old.yml")
DEBUG_LAST_PUBLISH_DATE = "01.01.1984 12:00 (UTC+3)"
GITHUB_API_URL = os.environ.get("GITHUB_API_URL", "https://api.github.com")

# https://discord.com/developers/docs/resources/webhook
DISCORD_SPLIT_LIMIT = 2000
DISCORD_WEBHOOK_URL = os.environ.get("DISCORD_WEBHOOK_URL")
DISCORD_WEBHOOK_URL_DEADSPACE = os.environ.get("CHANGELOG_DISCORD_WEBHOOK_DEADSPACE")
DISCORD_USERNAME = "Последние изменения Союз-1 ☭"
DISCORD_AVATAR_URL = "https://github.com/PERed5/soyuz/blob/main/soyuz.png?raw=true"

CHANGELOG_FILE = "Resources/Changelog/ChangelogDS14Soyuz.yml"
MOSCOW_TZ = timezone(timedelta(hours=3))

TYPE_ORDER = ("Add", "Remove", "Tweak", "Fix")
TYPE_EMOJIS = {
    "Add": "🆕",
    "Remove": "❌",
    "Tweak": "⚒️",
    "Fix": "🐛",
}
TYPE_LABELS_RU = {
    "Add": "Добавлено",
    "Remove": "Удалено",
    "Tweak": "Изменено",
    "Fix": "Исправлено",
}
SECTION_TITLE_TO_TYPE = {
    "Добавлено": "Add",
    "Удалено": "Remove",
    "Изменено": "Tweak",
    "Исправлено": "Fix",
}
UNKNOWN_TYPE_LABEL = "❓ Прочее"

ChangelogEntry = dict[str, Any]
GroupedChanges = dict[str, dict[str, list[tuple[str, str]]]]  # author -> type -> list of (message, pr_url)


def main():
    if not DISCORD_WEBHOOK_URL:
        print("No discord webhook URL found, skipping discord send")
        return

    if DEBUG:
        last_changelog_stream = DEBUG_CHANGELOG_FILE_OLD.read_text(encoding="utf-8")
        last_publish_date = DEBUG_LAST_PUBLISH_DATE
    else:
        last_changelog = get_last_changelog()
        if last_changelog is None:
            print("No previous successful publish run found, skipping discord send")
            return

        last_changelog_stream, last_publish_date = last_changelog

    last_changelog_data = yaml.safe_load(last_changelog_stream) or {}
    with open(CHANGELOG_FILE, "r", encoding="utf-8") as f:
        cur_changelog = yaml.safe_load(f) or {}

    diff = list(diff_changelog(last_changelog_data, cur_changelog))
    if not diff:
        print("No changelog changes found since the last publish")
        return

    messages = changelog_entries_to_messages(diff)
    send_messages(messages, last_publish_date)


def get_most_recent_workflow(
    sess: requests.Session, github_repository: str, github_run: str
) -> Optional[Any]:
    workflow_run = get_current_run(sess, github_repository, github_run)
    past_runs = get_past_runs(sess, workflow_run)
    for run in past_runs["workflow_runs"]:
        if run["id"] == workflow_run["id"]:
            continue
        return run
    return None


def get_current_run(
    sess: requests.Session, github_repository: str, github_run: str
) -> Any:
    resp = sess.get(
        f"{GITHUB_API_URL}/repos/{github_repository}/actions/runs/{github_run}"
    )
    resp.raise_for_status()
    return resp.json()


def get_past_runs(sess: requests.Session, current_run: Any) -> Any:
    params = {"status": "success", "created": f"<={current_run['created_at']}"}
    resp = sess.get(f"{current_run['workflow_url']}/runs", params=params)
    resp.raise_for_status()
    return resp.json()


def get_last_changelog() -> Optional[tuple[str, str]]:
    github_repository = os.environ["GITHUB_REPOSITORY"]
    github_run = os.environ["GITHUB_RUN_ID"]
    github_token = os.environ["GITHUB_TOKEN"]

    session = requests.Session()
    session.headers["Authorization"] = f"Bearer {github_token}"
    session.headers["Accept"] = "application/vnd.github+json"
    session.headers["X-GitHub-Api-Version"] = "2022-11-28"

    most_recent = get_most_recent_workflow(session, github_repository, github_run)
    if most_recent is None:
        return None

    head_commit = most_recent.get("head_commit") or {}
    last_sha = most_recent.get("head_sha") or head_commit["id"]
    last_publish_date = format_publish_date(most_recent["created_at"])
    print(f"Last successful publish job was {most_recent['id']}: {last_sha}")
    last_changelog_stream = get_last_changelog_by_sha(
        session, last_sha, github_repository
    )

    return last_changelog_stream, last_publish_date


def format_publish_date(created_at: str) -> str:
    published_at = datetime.fromisoformat(created_at.replace("Z", "+00:00"))
    return published_at.astimezone(MOSCOW_TZ).strftime("%d.%m.%Y %H:%M (UTC+3)")


def get_last_changelog_by_sha(
    sess: requests.Session, sha: str, github_repository: str
) -> str:
    params = {"ref": sha}
    headers = {"Accept": "application/vnd.github.raw"}
    resp = sess.get(
        f"{GITHUB_API_URL}/repos/{github_repository}/contents/{CHANGELOG_FILE}",
        headers=headers,
        params=params,
    )
    resp.raise_for_status()
    return resp.text


def diff_changelog(
    old: dict[str, Any], cur: dict[str, Any]
) -> Iterable[ChangelogEntry]:
    old_entry_ids = {entry.get("id") for entry in old.get("Entries", [])}
    for entry in cur.get("Entries", []):
        if entry.get("id") not in old_entry_ids:
            yield entry


def extract_pr_url_from_entry(entry: ChangelogEntry) -> Optional[str]:
    """Extract PR URL from changelog entry if available."""
    # Try to get from metadata
    metadata = entry.get("metadata", {})
    if "pr_url" in metadata:
        return metadata["pr_url"]
    
    # Try to extract from changelog text
    changes = entry.get("changes", [])
    for change in changes:
        message = change.get("message", "")
        # Look for PR pattern like (#343) or (PR #343)
        pr_match = re.search(r'\((?:\w+\s+)?#?(\d+)\)', message)
        if pr_match:
            pr_num = pr_match.group(1)
            repo = os.environ.get("GITHUB_REPOSITORY", "PERed5/soyuz")
            return f"https://github.com/{repo}/pull/{pr_num}"
    
    return None


def changelog_entries_to_messages(entries: Iterable[ChangelogEntry]) -> list[str]:
    """Convert changelog entries to Discord messages in PR-style format."""
    grouped_changes = group_changes_by_author(entries)
    messages: list[str] = []
    current_message = ""
    
    for author, changes in grouped_changes.items():
        # Build message for this author
        author_message = render_author_message(author, changes)
        
        if not author_message:
            continue
        
        # Check if we need to split
        if len(author_message) > DISCORD_SPLIT_LIMIT:
            if current_message:
                messages.append(current_message)
                current_message = ""
            
            # Split long author message
            for split_part in split_long_author_message(author, changes):
                messages.append(split_part)
            continue
        
        # Try to combine with previous message
        if current_message and len(f"{current_message}\n\n{author_message}") <= DISCORD_SPLIT_LIMIT:
            current_message = f"{current_message}\n\n{author_message}"
        else:
            if current_message:
                messages.append(current_message)
            current_message = author_message
    
    if current_message:
        messages.append(current_message)
    
    return messages


def group_changes_by_author(entries: Iterable[ChangelogEntry]) -> GroupedChanges:
    """Group changes by author and type, preserving PR URLs."""
    grouped: GroupedChanges = {}
    
    for entry in entries:
        author = str(entry.get("author") or "unknown")
        pr_url = extract_pr_url_from_entry(entry)
        
        if author not in grouped:
            grouped[author] = {type_key: [] for type_key in TYPE_ORDER}
        
        for change in entry.get("changes", []):
            type_key = str(change.get("type") or "")
            message = normalize_change_message(change.get("message", ""))
            sanitized = sanitize_change(type_key, message)
            if sanitized is None:
                continue
            
            type_key, message = sanitized
            # Store as tuple (message, pr_url)
            if (message, pr_url) not in grouped[author].setdefault(type_key, []):
                grouped[author].setdefault(type_key, []).append((message, pr_url))
    
    return grouped


def normalize_change_message(message: Any) -> str:
    return " ".join(str(message).split())


def sanitize_change(type_key: str, message: str) -> Optional[tuple[str, str]]:
    if not message:
        return None
    
    for section_title, section_type in SECTION_TITLE_TO_TYPE.items():
        prefix = f"{section_title}:"
        if message == prefix or message == section_title:
            return None
        
        if message.startswith(prefix):
            message = message[len(prefix):].strip()
            if not message:
                return None
            return section_type, message
    
    return type_key, message


def render_author_message(author: str, changes: dict[str, list[tuple[str, str]]]) -> str:
    """Render a single author's changes in PR-style format with clickable links."""
    lines = [f"👤 Автор: **{author}**", ""]
    
    for type_key in TYPE_ORDER:
        items = changes.get(type_key, [])
        if not items:
            continue
        
        emoji = TYPE_EMOJIS.get(type_key, "❓")
        label_ru = TYPE_LABELS_RU.get(type_key, "Прочее")
        
        for message, pr_url in items:
            pr_number = None
            if pr_url and "pull/" in pr_url:
                pr_number = pr_url.rstrip("/").split("/")[-1]
            
            if pr_number:
                # Clickable link in Discord Markdown format
                lines.append(f"{emoji} {label_ru} - {message} ([PR #{pr_number}]({pr_url}))")
            else:
                lines.append(f"{emoji} {label_ru} - {message}")
    
    if len(lines) == 2:  # Only header
        return ""
    
    return "\n".join(lines)


def split_long_author_message(author: str, changes: dict[str, list[tuple[str, str]]]) -> list[str]:
    """Split a long author message into multiple Discord messages."""
    messages = []
    current_lines = [f"👤 Автор: **{author}**", ""]
    
    for type_key in TYPE_ORDER:
        items = changes.get(type_key, [])
        if not items:
            continue
        
        emoji = TYPE_EMOJIS.get(type_key, "❓")
        label_ru = TYPE_LABELS_RU.get(type_key, "Прочее")
        
        for message, pr_url in items:
            pr_number = None
            if pr_url and "pull/" in pr_url:
                pr_number = pr_url.rstrip("/").split("/")[-1]
            
            if pr_number:
                line = f"{emoji} {label_ru} - {message} ([PR #{pr_number}]({pr_url}))"
            else:
                line = f"{emoji} {label_ru} - {message}"
            
            # Check if adding this line exceeds limit
            candidate = "\n".join([*current_lines, line])
            if len(candidate) > DISCORD_SPLIT_LIMIT:
                if len(current_lines) > 2:  # Have content
                    messages.append("\n".join(current_lines))
                current_lines = [f"👤 Автор: **{author}**", "", line]
            else:
                current_lines.append(line)
    
    if len(current_lines) > 2:
        messages.append("\n".join(current_lines))
    
    return messages


def send_discord_webhook(webhook_url: str, content: str):
    """Send a message to Discord webhook."""
    body = {
        "username": DISCORD_USERNAME,
        "avatar_url": DISCORD_AVATAR_URL,
        "content": content,
        "allowed_mentions": {"parse": []},
    }
    
    retry_attempt = 0
    try:
        response = requests.post(webhook_url, json=body, timeout=10)
        while response.status_code == 429:
            retry_attempt += 1
            if retry_attempt > 20:
                print(f"Too many retries... giving up for {webhook_url}")
                return
            retry_after = response.json().get("retry_after", 5)
            print(f"Rate limited, retrying after {retry_after} seconds")
            time.sleep(retry_after)
            response = requests.post(webhook_url, json=body, timeout=10)
        response.raise_for_status()
    except requests.exceptions.RequestException as e:
        print(f"Failed to send message to {webhook_url}: {e}")


def send_messages(messages: list[str], last_publish_date: str):
    """Send all messages to Discord webhooks."""
    webhooks = [DISCORD_WEBHOOK_URL]
    if DISCORD_WEBHOOK_URL_DEADSPACE:
        webhooks.append(DISCORD_WEBHOOK_URL_DEADSPACE)
    
    for webhook_url in webhooks:
        print(f"Sending changelog to {webhook_url}")
        for idx, message in enumerate(messages, 1):
            print(f"Sending message {idx}/{len(messages)} to discord")
            send_discord_webhook(webhook_url, message)


if __name__ == "__main__":
    main()