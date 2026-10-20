# SPDX-FileCopyrightText: 2026 Kofeecheks
# SPDX-License-Identifier: LicenseRef-Kofeecheks
"""Normalize Soyuz integration markers in tracked text files.

Run without arguments to preview, or pass --write to apply the replacements.
The replacement operates on bytes so existing encodings and line endings remain intact.
"""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path


OLD_MARKERS = (b"DS-14" + b" Soyuz", b"DS-14" + b" soyuz")
NEW_MARKER = b"DS14-Soyuz"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="apply the replacements")
    args = parser.parse_args()

    root = Path(
        subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip()
    )
    search = subprocess.run(
        ["git", "grep", "-Ilz", "-e", OLD_MARKERS[0].decode(), "-e", OLD_MARKERS[1].decode()],
        cwd=root,
        capture_output=True,
        check=False,
    )
    if search.returncode not in (0, 1):
        raise RuntimeError(search.stderr.decode("utf-8", errors="replace"))
    paths = search.stdout.split(b"\0")

    changed_files = 0
    changed_markers = 0
    for raw_path in filter(None, paths):
        relative_path = raw_path.decode("utf-8", errors="surrogateescape")
        path = root / relative_path
        if not path.is_file() or path.is_symlink():
            raise RuntimeError(f"Expected tracked regular file: {relative_path}")

        original = path.read_bytes()
        count = sum(original.count(marker) for marker in OLD_MARKERS)
        if count == 0:
            continue

        updated = original
        for marker in OLD_MARKERS:
            updated = updated.replace(marker, NEW_MARKER)
        if args.write:
            path.write_bytes(updated)

        changed_files += 1
        changed_markers += count
        print(f"{relative_path}: {count}")

    action = "Updated" if args.write else "Would update"
    print(f"{action} {changed_markers} markers in {changed_files} files")


if __name__ == "__main__":
    main()
