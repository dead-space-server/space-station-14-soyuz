#!/usr/bin/env python3

from __future__ import annotations

import argparse
import io
import json
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont


MAX_ENTRIES = 32
TEXTURE_ROOT = "Resources/Textures"


@dataclass(frozen=True)
class TextureChange:
    status: str
    before_path: str | None
    after_path: str | None

    @property
    def display_path(self) -> str:
        return self.after_path or self.before_path or "<unknown>"

    @property
    def label(self) -> str:
        return Path(self.display_path).name


def run_git(args: list[str], *, check: bool = True) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["git", *args],
        check=check,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


def run_git_show(rev: str, path: str) -> bytes | None:
    proc = run_git(["show", f"{rev}:{path}"], check=False)
    if proc.returncode != 0:
        return None
    return proc.stdout


def list_changed_textures(base_sha: str, head_sha: str) -> tuple[list[TextureChange], int]:
    # DS14-start: build preview entries from actual PR texture diff instead of a fixed list.
    proc = run_git(
        [
            "diff",
            "--name-status",
            "--find-renames",
            "--diff-filter=ACDMR",
            base_sha,
            head_sha,
            "--",
            TEXTURE_ROOT,
        ]
    )

    changes: list[TextureChange] = []
    for raw_line in proc.stdout.decode("utf-8", errors="replace").splitlines():
        parts = raw_line.split("\t")
        if not parts:
            continue

        code = parts[0]
        kind = code[:1]

        def is_png(path: str) -> bool:
            return path.lower().endswith(".png")

        if kind == "A" and len(parts) >= 2 and is_png(parts[1]):
            changes.append(TextureChange("added", None, parts[1]))
        elif kind == "D" and len(parts) >= 2 and is_png(parts[1]):
            changes.append(TextureChange("removed", parts[1], None))
        elif kind == "M" and len(parts) >= 2 and is_png(parts[1]):
            changes.append(TextureChange("modified", parts[1], parts[1]))
        elif kind == "R" and len(parts) >= 3 and (is_png(parts[1]) or is_png(parts[2])):
            before_path = parts[1] if is_png(parts[1]) else None
            after_path = parts[2] if is_png(parts[2]) else None
            changes.append(TextureChange("renamed", before_path, after_path))

    omitted = max(0, len(changes) - MAX_ENTRIES)
    return changes[:MAX_ENTRIES], omitted
    # DS14-end


def load_font(candidates: list[str], size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in candidates:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def fit_sprite(raw_bytes: bytes, thumb_size: int) -> Image.Image:
    sprite = Image.open(io.BytesIO(raw_bytes)).convert("RGBA")
    bbox = sprite.getchannel("A").getbbox()
    if bbox:
        sprite = sprite.crop(bbox)
    max_side = max(sprite.width, sprite.height, 1)
    max_scaled = thumb_size - 12
    scale = max(1, min(8, max_scaled // max_side if max_side else 8))
    sprite = sprite.resize((sprite.width * scale, sprite.height * scale), Image.Resampling.NEAREST)
    box = Image.new("RGBA", (thumb_size, thumb_size), (245, 247, 250, 255))
    box.alpha_composite(sprite, ((thumb_size - sprite.width) // 2, (thumb_size - sprite.height) // 2))
    return box


def make_placeholder(thumb_size: int, label: str, color: tuple[int, int, int, int], font: ImageFont.ImageFont) -> Image.Image:
    box = Image.new("RGBA", (thumb_size, thumb_size), (245, 247, 250, 255))
    draw = ImageDraw.Draw(box)
    draw.rounded_rectangle((6, 6, thumb_size - 6, thumb_size - 6), radius=10, outline=color, width=3)
    text_bbox = draw.multiline_textbbox((0, 0), label, font=font, align="center", spacing=2)
    text_width = text_bbox[2] - text_bbox[0]
    text_height = text_bbox[3] - text_bbox[1]
    draw.multiline_text(
        ((thumb_size - text_width) / 2, (thumb_size - text_height) / 2),
        label,
        fill=color,
        font=font,
        align="center",
        spacing=2,
    )
    return box


def elide(text: str, limit: int = 36) -> str:
    if len(text) <= limit:
        return text
    return text[: limit - 1] + "..."


def status_label(status: str) -> str:
    return status.upper()


def build_preview(base_sha: str, head_sha: str, output_path: Path, metadata_path: Path | None) -> dict[str, Any]:
    thumb = 88
    cell_width = 330
    cell_height = 188
    margin = 18
    gap = 14
    header_height = 96
    cols = 4

    bg = (245, 247, 250, 255)
    panel = (255, 255, 255, 255)
    border = (210, 215, 223, 255)
    text = (25, 31, 40, 255)
    muted = (92, 100, 112, 255)
    before_color = (51, 102, 204, 255)
    after_color = (200, 55, 55, 255)
    status_colors: dict[str, tuple[int, int, int, int]] = {
        "added": (18, 127, 66, 255),
        "modified": (178, 111, 0, 255),
        "removed": (174, 28, 40, 255),
        "renamed": (108, 60, 170, 255),
    }

    title_font = load_font(
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "C:/Windows/Fonts/segoeuib.ttf",
        ],
        20,
    )
    subtitle_font = load_font(
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "C:/Windows/Fonts/segoeui.ttf",
        ],
        11,
    )
    label_font = load_font(
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "C:/Windows/Fonts/segoeuib.ttf",
        ],
        12,
    )
    small_font = load_font(
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "C:/Windows/Fonts/segoeui.ttf",
        ],
        10,
    )
    tag_font = load_font(
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "C:/Windows/Fonts/segoeuib.ttf",
        ],
        10,
    )

    changes, omitted = list_changed_textures(base_sha, head_sha)

    if not changes:
        image = Image.new("RGBA", (1000, 220), bg)
        draw = ImageDraw.Draw(image)
        draw.text((20, 18), "PR Texture Before / After", fill=text, font=title_font)
        draw.text((22, 50), f"Compared {base_sha[:7]} -> {head_sha[:7]}", fill=muted, font=subtitle_font)
        draw.rounded_rectangle((18, 88, 982, 192), radius=10, fill=panel, outline=border, width=2)
        draw.text((40, 116), "No changed PNG textures found in Resources/Textures.", fill=text, font=label_font)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        image.save(output_path)
        metadata = {
            "base_sha": base_sha,
            "head_sha": head_sha,
            "image": str(output_path),
            "entry_count": 0,
            "omitted_count": 0,
            "entries": [],
        }
        if metadata_path is not None:
            metadata_path.parent.mkdir(parents=True, exist_ok=True)
            metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
        return metadata

    rows = (len(changes) + cols - 1) // cols
    canvas_width = margin * 2 + cols * cell_width + (cols - 1) * gap
    canvas_height = header_height + margin * 2 + rows * cell_height + (rows - 1) * gap
    image = Image.new("RGBA", (canvas_width, canvas_height), bg)
    draw = ImageDraw.Draw(image)

    draw.text((20, 16), "PR Texture Before / After", fill=text, font=title_font)
    summary = f"Compared {base_sha[:7]} -> {head_sha[:7]} | {len(changes)} texture(s)"
    if omitted:
        summary += f" | +{omitted} omitted"
    draw.text((22, 48), summary, fill=muted, font=subtitle_font)

    rendered: list[dict[str, str]] = []
    start_y = header_height + margin
    for index, change in enumerate(changes):
        before_bytes = run_git_show(base_sha, change.before_path) if change.before_path else None
        after_bytes = run_git_show(head_sha, change.after_path) if change.after_path else None

        col = index % cols
        row = index // cols
        x = margin + col * (cell_width + gap)
        y = start_y + row * (cell_height + gap)

        draw.rounded_rectangle((x, y, x + cell_width, y + cell_height), radius=8, fill=panel, outline=border, width=2)
        draw.text((x + 12, y + 10), elide(change.label, 28), fill=text, font=label_font)

        status_color = status_colors.get(change.status, muted)
        draw.rounded_rectangle((x + 238, y + 8, x + 316, y + 28), radius=10, fill=status_color)
        draw.text((x + 255, y + 13), status_label(change.status), fill=(255, 255, 255, 255), font=tag_font)

        left_x = x + 18
        right_x = x + 174
        top_y = y + 38
        draw.rectangle((left_x, top_y, left_x + thumb, top_y + thumb), fill=bg, outline=border, width=1)
        draw.rectangle((right_x, top_y, right_x + thumb, top_y + thumb), fill=bg, outline=border, width=1)

        before_box = fit_sprite(before_bytes, thumb) if before_bytes else make_placeholder(thumb, "NO\nFILE", before_color, tag_font)
        after_box = fit_sprite(after_bytes, thumb) if after_bytes else make_placeholder(thumb, "NO\nFILE", after_color, tag_font)
        image.alpha_composite(before_box, (left_x, top_y))
        image.alpha_composite(after_box, (right_x, top_y))

        draw.text((left_x + 20, top_y + thumb + 6), "BEFORE", fill=before_color, font=tag_font)
        draw.text((right_x + 25, top_y + thumb + 6), "AFTER", fill=after_color, font=tag_font)
        draw.text((x + 12, y + 138), elide(change.before_path or "<new file>", 46), fill=muted, font=small_font)
        draw.text((x + 12, y + 154), elide(change.after_path or "<deleted file>", 46), fill=muted, font=small_font)

        rendered.append(
            {
                "status": change.status,
                "before_path": change.before_path or "",
                "after_path": change.after_path or "",
            }
        )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path)

    metadata = {
        "base_sha": base_sha,
        "head_sha": head_sha,
        "image": str(output_path),
        "entry_count": len(rendered),
        "omitted_count": omitted,
        "entries": rendered,
    }

    if metadata_path is not None:
        metadata_path.parent.mkdir(parents=True, exist_ok=True)
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")

    return metadata


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate a before/after texture preview sheet for a PR.")
    parser.add_argument("--base-sha", required=True)
    parser.add_argument("--head-sha", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--metadata")
    args = parser.parse_args()

    metadata = build_preview(
        base_sha=args.base_sha,
        head_sha=args.head_sha,
        output_path=Path(args.output),
        metadata_path=Path(args.metadata) if args.metadata else None,
    )

    print(
        json.dumps(
            {
                "image": metadata["image"],
                "entry_count": metadata["entry_count"],
                "omitted_count": metadata["omitted_count"],
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
