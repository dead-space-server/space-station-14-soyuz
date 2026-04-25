#!/usr/bin/env python3

from __future__ import annotations

import argparse
import io
import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont


@dataclass(frozen=True)
class PreviewEntry:
    label: str
    prototype_path: str
    entity_id: str
    before_asset: str
    after_asset: str | None = None


@dataclass(frozen=True)
class RenderEntry:
    label: str
    before_name: str
    after_name: str
    before_asset: str
    after_asset: str
    entity_id: str


ENTRIES: tuple[PreviewEntry, ...] = (
    PreviewEntry("Bag Of Holding", "Resources/Prototypes/Entities/Clothing/Back/backpacks.yml", "ClothingBackpackHolding", "Resources/Textures/Clothing/Back/Backpacks/holding.rsi/holding.png"),
    PreviewEntry("Duffel Of Holding", "Resources/Prototypes/Entities/Clothing/Back/duffel.yml", "ClothingBackpackDuffelHolding", "Resources/Textures/Clothing/Back/Duffels/holding.rsi/icon.png"),
    PreviewEntry("Satchel Of Holding", "Resources/Prototypes/Entities/Clothing/Back/satchel.yml", "ClothingBackpackSatchelHolding", "Resources/Textures/Clothing/Back/Satchels/holding.rsi/icon.png"),
    PreviewEntry("Silicon Storage", "Resources/Prototypes/Entities/Clothing/Back/specific.yml", "XenoborgMaterialBag", "Resources/Textures/Objects/Specific/Robotics/silicon_storage_cube.rsi/xenoborg.png"),
    PreviewEntry("Portal", "Resources/Prototypes/Entities/Effects/portal.yml", "BasePortal", "Resources/Textures/Effects/portal.rsi/portal-blue.png", "Resources/Textures/Effects/portal.rsi/portal-red.png"),
    PreviewEntry("Hand Teleporter", "Resources/Prototypes/Entities/Objects/Devices/hand_teleporter.yml", "HandTeleporter", "Resources/Textures/Objects/Devices/hand_teleporter.rsi/icon.png"),
    PreviewEntry("Wrap", "Resources/Prototypes/Entities/Objects/Misc/parcel_wrap.yml", "ParcelWrapAdmeme", "Resources/Textures/Objects/Misc/ParcelWrap/parcel_wrap.rsi/brown.png"),
    PreviewEntry("Storage Implant", "Resources/Prototypes/Entities/Objects/Misc/subdermal_implants.yml", "StorageImplant", "Resources/Textures/Objects/Specific/Medical/implanter.rsi/implanter1.png"),
    PreviewEntry("Beaker", "Resources/Prototypes/Entities/Objects/Specific/Chemistry/chemistry.yml", "BluespaceBeaker", "Resources/Textures/Objects/Specific/Chemistry/beaker_bluespace.rsi/beakerbluespace.png"),
    PreviewEntry("Syringe", "Resources/Prototypes/Entities/Objects/Specific/Chemistry/chemistry.yml", "SyringeBluespace", "Resources/Textures/Objects/Specific/Chemistry/syringe.rsi/bluespace_base0.png"),
    PreviewEntry("Omega Soap", "Resources/Prototypes/Entities/Objects/Specific/Janitorial/soap.yml", "SoapOmega", "Resources/Textures/Objects/Specific/Janitorial/soap.rsi/omega-4.png"),
    PreviewEntry("Admin Hypo", "Resources/Prototypes/Entities/Objects/Specific/Medical/hypospray.yml", "AdminHypo", "Resources/Textures/Objects/Specific/Medical/syndihypo.rsi/hypo.png"),
    PreviewEntry("CentComm Flippo", "Resources/Prototypes/Entities/Objects/Tools/lighters.yml", "CentCommFlippo", "Resources/Textures/Objects/Tools/Lighters/centcomm.rsi/icon.png"),
    PreviewEntry("RCD Ammo", "Resources/Prototypes/Entities/Objects/Tools/tools.yml", "RCDAmmo", "Resources/Textures/Objects/Tools/rcd.rsi/ammo.png"),
    PreviewEntry("Nukie Delivery", "Resources/Prototypes/Entities/Structures/Machines/Computers/computers.yml", "ComputerNukieDelivery", "Resources/Textures/Structures/Machines/computers.rsi/request-syndie.png"),
    PreviewEntry("Fax Machine", "Resources/Prototypes/Entities/Structures/Machines/fax_machine.yml", "FaxMachineBase", "Resources/Textures/Structures/Machines/fax_machine.rsi/icon.png"),
    PreviewEntry("Holopad", "Resources/Prototypes/Entities/Structures/Machines/holopad.yml", "HolopadBluespace", "Resources/Textures/Structures/Machines/holopad.rsi/base.png"),
    PreviewEntry("Material Silo", "Resources/Prototypes/Entities/Structures/Machines/silo.yml", "MachineMaterialSilo", "Resources/Textures/Structures/Machines/silo.rsi/silo.png"),
    PreviewEntry("Space Heater", "Resources/Prototypes/Entities/Structures/Piping/Atmospherics/portable.yml", "SpaceHeater", "Resources/Textures/Structures/Piping/Atmospherics/Portable/portable_sheater.rsi/sheaterOff.png"),
    PreviewEntry("Anomaly", "Resources/Prototypes/Entities/Structures/Specific/Anomaly/anomalies.yml", "AnomalyBluespace", "Resources/Textures/Structures/Specific/anomaly.rsi/anom4.png"),
    PreviewEntry("Anomaly Trap", "Resources/Prototypes/Entities/Structures/Specific/Anomaly/anomaly_injectors.yml", "AnomalyTrapBluespace", "Resources/Textures/Structures/Specific/Anomalies/inner_anom_layer.rsi/bluespace.png"),
    PreviewEntry("Anomaly Core", "Resources/Prototypes/Entities/Structures/Specific/Anomaly/cores.yml", "AnomalyCoreBluespace", "Resources/Textures/Structures/Specific/Anomalies/Cores/bluespace_core.rsi/core.png"),
    PreviewEntry("Inert Core", "Resources/Prototypes/Entities/Structures/Specific/Anomaly/cores.yml", "AnomalyCoreBluespaceInert", "Resources/Textures/Structures/Specific/Anomalies/Cores/bluespace_core.rsi/core.png"),
    PreviewEntry("Locker", "Resources/Prototypes/Entities/Structures/Storage/Closets/Lockers/lockers.yml", "LockerBluespaceStation", "Resources/Textures/Structures/Storage/wall_locker.rsi/syndicate_closed.png"),
    PreviewEntry("Closet", "Resources/Prototypes/Entities/Structures/Storage/Closets/closets.yml", "ClosetBluespace", "Resources/Textures/Structures/Storage/closet.rsi/generic.png"),
    PreviewEntry("Unstable Closet", "Resources/Prototypes/Entities/Structures/Storage/Closets/closets.yml", "ClosetBluespaceUnstable", "Resources/Textures/Structures/Storage/closet.rsi/generic.png"),
    PreviewEntry("Artifact Portal", "Resources/Prototypes/XenoArch/effects.yml", "XenoArtifactPortal", "Resources/Textures/Effects/portal.rsi/portal-blue.png", "Resources/Textures/Effects/portal.rsi/portal-red.png"),
    PreviewEntry("VIB", "Resources/Prototypes/_DeadSpace/Entities/Objects/Fun/vib.yml", "VIB", "Resources/Textures/_DeadSpace/Objects/Fun/vib.rsi/icon.png"),
    PreviewEntry("Vial", "Resources/Prototypes/_DeadSpace/Entities/Objects/Specific/chemical-vials.yml", "BluespaceVial", "Resources/Textures/_DeadSpace/Objects/Specific/Chemistry/vial_bluespace.rsi/vial.png"),
    PreviewEntry("Artillery", "Resources/Prototypes/_DeadSpace/Entities/Structures/Machines/bluespaceartillery.yml", "BluespaceArtillery", "Resources/Textures/_DeadSpace/Structures/Machines/bluespaceartillery.rsi/icon.png"),
    PreviewEntry("Toner Cartridge", "Resources/Prototypes/_DeadSpace/Entities/Structures/Machines/photocopier.yml", "PhotocopierTonerCartridge", "Resources/Textures/_DeadSpace/Objects/Misc/tonercartridge.rsi/icon.png"),
    PreviewEntry("Array Asset", "Resources/Prototypes/Entities/Objects/Tools/tools.yml", "RCDAmmo", "Resources/Textures/Objects/Misc/module.rsi/bluespacearray.png"),
    PreviewEntry("Electrolite Asset", "Resources/Prototypes/Entities/Objects/Tools/tools.yml", "RCDAmmo", "Resources/Textures/Objects/Misc/stock_parts.rsi/bluespace_electrolite.png"),
    PreviewEntry("Matter Bin Asset", "Resources/Prototypes/Entities/Objects/Tools/tools.yml", "RCDAmmo", "Resources/Textures/Objects/Misc/stock_parts.rsi/bluespace_matter_bin.png"),
    PreviewEntry("Projectile Asset", "Resources/Prototypes/Entities/Effects/portal.yml", "BasePortal", "Resources/Textures/Objects/Weapons/Guns/Projectiles/magic.rsi/bluespace.png"),
    PreviewEntry("Hot Drink Asset", "Resources/Prototypes/_DeadSpace/Entities/Objects/Specific/chemical-vials.yml", "BluespaceVial", "Resources/Textures/_DeadSpace/Objects/Consumable/Drinks/bluespacehotglass.rsi/icon.png"),
)


def run_git_show(rev: str, path: str, *, binary: bool) -> bytes | str:
    proc = subprocess.run(
        ["git", "show", f"{rev}:{path}"],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if binary:
        return proc.stdout
    return proc.stdout.decode("utf-8", errors="replace")


def run_git_lines(*args: str) -> list[str]:
    proc = subprocess.run(
        ["git", *args],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    return [line for line in proc.stdout.splitlines() if line]


def try_run_git_show(rev: str, path: str, *, binary: bool) -> bytes | str | None:
    proc = subprocess.run(
        ["git", "show", f"{rev}:{path}"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if proc.returncode != 0:
        return None
    if binary:
        return proc.stdout
    return proc.stdout.decode("utf-8", errors="replace")


def run_git_grep(rev: str, pattern: str, pathspec: str) -> list[str]:
    proc = subprocess.run(
        ["git", "grep", "-l", pattern, rev, "--", pathspec],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    if proc.returncode not in (0, 1):
        raise subprocess.CalledProcessError(proc.returncode, proc.args, output=proc.stdout, stderr=proc.stderr)
    prefix = f"{rev}:"
    return [line.removeprefix(prefix) for line in proc.stdout.splitlines() if line]


def extract_entity_block(text: str, entity_id: str) -> list[str]:
    current: list[str] = []
    for line in text.splitlines():
        normalized = line.lstrip("\ufeff")
        if normalized.startswith("- type: entity"):
            if current and any(re.match(rf"^\s*id:\s*{re.escape(entity_id)}\s*$", entry_line) for entry_line in current):
                return current
            current = [normalized]
            continue

        if current:
            current.append(normalized)

    if current and any(re.match(rf"^\s*id:\s*{re.escape(entity_id)}\s*$", entry_line) for entry_line in current):
        return current

    raise ValueError(f"Could not find entity block for {entity_id}")


def extract_field(block: list[str], field_name: str) -> str | None:
    pattern = re.compile(rf"^\s*{re.escape(field_name)}:\s*(.+?)\s*$")
    for line in block:
        match = pattern.match(line)
        if match:
            value = match.group(1).strip()
            if value.startswith('"') and value.endswith('"'):
                return value[1:-1]
            return value
    return None


def extract_entity_blocks(text: str) -> list[list[str]]:
    blocks: list[list[str]] = []
    current: list[str] = []
    for line in text.splitlines():
        normalized = line.lstrip("\ufeff")
        if normalized.startswith("- type: entity"):
            if current:
                blocks.append(current)
            current = [normalized]
            continue
        if current:
            current.append(normalized)

    if current:
        blocks.append(current)

    return blocks


def extract_sprite_paths(block: list[str]) -> set[str]:
    pattern = re.compile(r"^\s*sprite:\s*(.+?)\s*$")
    sprites: set[str] = set()
    for line in block:
        match = pattern.match(line)
        if not match:
            continue
        value = match.group(1).strip().strip('"')
        if value.startswith("/"):
            value = value[1:]
        sprites.add(value)
    return sprites


def entity_name_for_rev(rev: str, entry: PreviewEntry) -> str:
    text = run_git_show(rev, entry.prototype_path, binary=False)
    block = extract_entity_block(text, entry.entity_id)
    name = extract_field(block, "name")
    suffix = extract_field(block, "suffix")
    if name and suffix:
        return f"{name} [{suffix}]"
    if name:
        return name
    if suffix:
        return f"{entry.label} [{suffix}]"
    return entry.label


def try_entity_name_for_rev(rev: str, entry: PreviewEntry) -> str:
    try:
        return entity_name_for_rev(rev, entry)
    except (subprocess.CalledProcessError, ValueError):
        return entry.label


def load_font(candidates: list[str], size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in candidates:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def fit_sprite(raw_bytes: bytes | None, thumb_size: int) -> Image.Image:
    box = Image.new("RGBA", (thumb_size, thumb_size), (245, 247, 250, 255))
    if raw_bytes is None:
        return box

    sprite = Image.open(io.BytesIO(raw_bytes)).convert("RGBA")
    bbox = sprite.getchannel("A").getbbox()
    if bbox:
        sprite = sprite.crop(bbox)
    max_side = max(sprite.width, sprite.height, 1)
    max_scaled = thumb_size - 12
    scale = max(1, min(8, max_scaled // max_side if max_side else 8))
    sprite = sprite.resize((sprite.width * scale, sprite.height * scale), Image.Resampling.NEAREST)
    box.alpha_composite(sprite, ((thumb_size - sprite.width) // 2, (thumb_size - sprite.height) // 2))
    return box


def elide(text: str, limit: int = 30) -> str:
    if len(text) <= limit:
        return text
    return text[: limit - 1] + "..."


def wrap_text(
    draw: ImageDraw.ImageDraw,
    text: str,
    font: ImageFont.ImageFont,
    max_width: int,
    *,
    max_lines: int | None = None,
) -> list[str]:
    words = text.split()
    if not words:
        return [text]

    lines: list[str] = []
    current = words[0]
    for word in words[1:]:
        candidate = f"{current} {word}"
        bbox = draw.textbbox((0, 0), candidate, font=font)
        if bbox[2] - bbox[0] <= max_width:
            current = candidate
            continue
        lines.append(current)
        current = word

    lines.append(current)

    if max_lines is None or len(lines) <= max_lines:
        return lines

    trimmed = lines[:max_lines]
    last = trimmed[-1]
    while last:
        candidate = f"{last}..."
        bbox = draw.textbbox((0, 0), candidate, font=font)
        if bbox[2] - bbox[0] <= max_width:
            trimmed[-1] = candidate
            return trimmed
        if " " in last:
            last = last.rsplit(" ", 1)[0]
        else:
            last = last[:-1]

    trimmed[-1] = "..."
    return trimmed


def draw_centered_lines(
    draw: ImageDraw.ImageDraw,
    lines: list[str],
    *,
    center_x: int,
    top_y: int,
    font: ImageFont.ImageFont,
    fill: tuple[int, int, int, int],
    line_gap: int = 2,
) -> None:
    current_y = top_y
    for line in lines:
        bbox = draw.textbbox((0, 0), line, font=font)
        width = bbox[2] - bbox[0]
        height = bbox[3] - bbox[1]
        draw.text((center_x - width / 2, current_y), line, fill=fill, font=font)
        current_y += height + line_gap


def measure_lines_height(
    draw: ImageDraw.ImageDraw,
    lines: list[str],
    *,
    font: ImageFont.ImageFont,
    line_gap: int = 2,
) -> int:
    total = 0
    for index, line in enumerate(lines):
        bbox = draw.textbbox((0, 0), line, font=font)
        total += bbox[3] - bbox[1]
        if index < len(lines) - 1:
            total += line_gap
    return total


def tracked_paths_for_entry(entry: PreviewEntry) -> set[str]:
    paths = {
        entry.prototype_path,
        entry.before_asset,
        str(Path(entry.before_asset).parent),
    }
    after_asset_path = entry.after_asset or entry.before_asset
    paths.add(after_asset_path)
    paths.add(str(Path(after_asset_path).parent))
    return paths


def pick_asset_for_rsi(rev: str, rsi_dir: str, changed_paths: list[str]) -> str | None:
    preferred = ["icon.png", "base.png"]
    for filename in preferred:
        asset_path = f"{rsi_dir}/{filename}"
        if try_run_git_show(rev, asset_path, binary=True) is not None:
            return asset_path

    for changed_path in changed_paths:
        if changed_path.startswith(f"{rsi_dir}/") and try_run_git_show(rev, changed_path, binary=True) is not None:
            return changed_path

    return None


def infer_entry_from_rsi_dir(head_sha: str, rsi_dir: str, changed_paths: list[str]) -> PreviewEntry | None:
    if not rsi_dir.startswith("Resources/Textures/"):
        return None

    sprite_path = rsi_dir.removeprefix("Resources/Textures/")
    prototype_files = run_git_grep(head_sha, sprite_path, "Resources/Prototypes/**/*.yml")
    for prototype_path in prototype_files:
        text = run_git_show(head_sha, prototype_path, binary=False)
        for block in extract_entity_blocks(text):
            if sprite_path not in extract_sprite_paths(block):
                continue

            entity_id = extract_field(block, "id")
            if not entity_id:
                continue

            label = extract_field(block, "name") or entity_id
            asset_path = pick_asset_for_rsi(head_sha, rsi_dir, changed_paths)
            if asset_path is None:
                continue

            return PreviewEntry(
                label=label,
                prototype_path=prototype_path,
                entity_id=entity_id,
                before_asset=asset_path,
            )

    return None


def build_render_entries(base_sha: str, head_sha: str, changed_paths: list[str]) -> list[RenderEntry]:
    render_entries: list[RenderEntry] = []
    seen_assets: set[str] = set()

    for entry in ENTRIES:
        tracked_paths = tracked_paths_for_entry(entry)
        if not any(
            changed_path == tracked_path or changed_path.startswith(f"{tracked_path}/")
            for changed_path in changed_paths
            for tracked_path in tracked_paths
        ):
            continue

        before_name = try_entity_name_for_rev(base_sha, entry)
        after_name = try_entity_name_for_rev(head_sha, entry)
        after_asset_path = entry.after_asset or entry.before_asset
        render_entries.append(
            RenderEntry(
                label=entry.label,
                before_name=before_name,
                after_name=after_name,
                before_asset=entry.before_asset,
                after_asset=after_asset_path,
                entity_id=entry.entity_id,
            )
        )
        seen_assets.add(entry.before_asset)
        seen_assets.add(after_asset_path)

    changed_pngs = [path for path in changed_paths if path.endswith(".png")]
    prototype_cache: dict[str, PreviewEntry | None] = {}
    for changed_path in changed_pngs:
        if changed_path in seen_assets:
            continue

        rsi_dir = str(Path(changed_path).parent).replace("\\", "/")
        if ".rsi/" not in f"{rsi_dir}/":
            continue

        if rsi_dir not in prototype_cache:
            prototype_cache[rsi_dir] = infer_entry_from_rsi_dir(head_sha, rsi_dir, changed_paths)
        prototype_entry = prototype_cache[rsi_dir]

        label = Path(changed_path).stem
        before_name = label
        after_name = label
        entity_id = Path(changed_path).stem
        if prototype_entry is not None:
            entity_name = try_entity_name_for_rev(head_sha, prototype_entry)
            before_name = f"{entity_name} / {label}"
            after_name = f"{entity_name} / {label}"
            entity_id = prototype_entry.entity_id

        render_entries.append(
            RenderEntry(
                label=label,
                before_name=before_name,
                after_name=after_name,
                before_asset=changed_path,
                after_asset=changed_path,
                entity_id=entity_id,
            )
        )

    return render_entries


def changed_entries(base_sha: str, head_sha: str) -> tuple[list[RenderEntry], list[str]]:
    changed_paths = run_git_lines("diff", "--name-only", f"{base_sha}..{head_sha}")
    if not changed_paths:
        return [], []
    return build_render_entries(base_sha, head_sha, changed_paths), changed_paths


def build_preview(base_sha: str, head_sha: str, output_path: Path, metadata_path: Path | None) -> dict[str, Any]:
    selected_entries, changed_paths = changed_entries(base_sha, head_sha)

    thumb = 88
    cell_width = 330
    cell_height = 220
    margin = 18
    gap = 14
    header_height = 88
    cols = 4
    rows = max(1, (len(selected_entries) + cols - 1) // cols)
    canvas_width = margin * 2 + cols * cell_width + (cols - 1) * gap
    canvas_height = header_height + margin * 2 + rows * cell_height + (rows - 1) * gap

    bg = (245, 247, 250, 255)
    panel = (255, 255, 255, 255)
    border = (210, 215, 223, 255)
    text = (25, 31, 40, 255)
    muted = (92, 100, 112, 255)
    before_color = (51, 102, 204, 255)
    after_color = (200, 55, 55, 255)

    image = Image.new("RGBA", (canvas_width, canvas_height), bg)
    draw = ImageDraw.Draw(image)
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
        13,
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

    draw.text((20, 16), "PR Before/After Comparison", fill=text, font=title_font)
    draw.text((22, 48), f"Compared {base_sha[:7]} -> {head_sha[:7]}", fill=muted, font=subtitle_font)

    rendered: list[dict[str, str]] = []
    start_y = header_height + margin
    if not selected_entries:
        message = "No tracked before/after entries changed in this PR."
        detail = "The workflow only renders entries touched by prototype changes or files inside their RSI folders."
        draw.rounded_rectangle(
            (margin, start_y, canvas_width - margin, start_y + cell_height),
            radius=8,
            fill=panel,
            outline=border,
            width=2,
        )
        draw.text((margin + 18, start_y + 24), message, fill=text, font=label_font)
        draw.text((margin + 18, start_y + 54), detail, fill=muted, font=subtitle_font)
    else:
        for index, entry in enumerate(selected_entries):
            col = index % cols
            row = index // cols
            x = margin + col * (cell_width + gap)
            y = start_y + row * (cell_height + gap)

            draw.rounded_rectangle((x, y, x + cell_width, y + cell_height), radius=8, fill=panel, outline=border, width=2)
            title_lines = wrap_text(draw, entry.label, label_font, cell_width - 24, max_lines=2)
            title_height = measure_lines_height(draw, title_lines, font=label_font)
            draw_centered_lines(
                draw,
                title_lines,
                center_x=x + cell_width // 2,
                top_y=y + 10,
                font=label_font,
                fill=text,
            )

            before_asset = try_run_git_show(base_sha, entry.before_asset, binary=True)
            after_asset = try_run_git_show(head_sha, entry.after_asset, binary=True)
            pair_width = thumb * 2 + 68
            pair_start_x = x + (cell_width - pair_width) // 2
            left_x = pair_start_x
            right_x = pair_start_x + thumb + 68
            top_y = y + 20 + title_height
            draw.rectangle((left_x, top_y, left_x + thumb, top_y + thumb), fill=bg, outline=border, width=1)
            draw.rectangle((right_x, top_y, right_x + thumb, top_y + thumb), fill=bg, outline=border, width=1)

            image.alpha_composite(fit_sprite(before_asset, thumb), (left_x, top_y))
            image.alpha_composite(fit_sprite(after_asset, thumb), (right_x, top_y))

            left_center_x = left_x + thumb // 2
            right_center_x = right_x + thumb // 2
            draw_centered_lines(
                draw,
                ["BEFORE"],
                center_x=left_center_x,
                top_y=top_y + thumb + 8,
                font=tag_font,
                fill=before_color,
                line_gap=0,
            )
            draw_centered_lines(
                draw,
                ["AFTER"],
                center_x=right_center_x,
                top_y=top_y + thumb + 8,
                font=tag_font,
                fill=after_color,
                line_gap=0,
            )
            before_lines = wrap_text(draw, entry.before_name, small_font, thumb + 40, max_lines=3)
            after_lines = wrap_text(draw, entry.after_name, small_font, thumb + 40, max_lines=3)
            tag_height = measure_lines_height(draw, ["BEFORE"], font=tag_font, line_gap=0)
            draw_centered_lines(
                draw,
                before_lines,
                center_x=left_center_x,
                top_y=top_y + thumb + 12 + tag_height,
                font=small_font,
                fill=muted,
            )
            draw_centered_lines(
                draw,
                after_lines,
                center_x=right_center_x,
                top_y=top_y + thumb + 12 + tag_height,
                font=small_font,
                fill=muted,
            )

            rendered.append(
                {
                    "label": entry.label,
                    "entity_id": entry.entity_id,
                    "before_name": entry.before_name,
                    "after_name": entry.after_name,
                    "before_asset": entry.before_asset,
                    "after_asset": entry.after_asset,
                }
            )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path)

    metadata = {
        "base_sha": base_sha,
        "head_sha": head_sha,
        "image": str(output_path),
        "changed_paths": changed_paths,
        "entries": rendered,
    }

    if metadata_path is not None:
        metadata_path.parent.mkdir(parents=True, exist_ok=True)
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")

    return metadata


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate a PR before/after sheet.")
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

    print(json.dumps({"image": metadata["image"], "entry_count": len(metadata["entries"])}, ensure_ascii=False))


if __name__ == "__main__":
    main()
