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


def elide(text: str, limit: int = 30) -> str:
    if len(text) <= limit:
        return text
    return text[: limit - 1] + "..."


def build_preview(base_sha: str, head_sha: str, output_path: Path, metadata_path: Path | None) -> dict[str, Any]:
    thumb = 88
    cell_width = 330
    cell_height = 180
    margin = 18
    gap = 14
    header_height = 88
    cols = 4
    rows = (len(ENTRIES) + cols - 1) // cols
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

    draw.text((20, 16), "PR Bluespace -> Redspace Comparison", fill=text, font=title_font)
    draw.text((22, 48), f"Compared {base_sha[:7]} -> {head_sha[:7]}", fill=muted, font=subtitle_font)

    rendered: list[dict[str, str]] = []
    start_y = header_height + margin
    for index, entry in enumerate(ENTRIES):
        before_name = entity_name_for_rev(base_sha, entry)
        after_name = entity_name_for_rev(head_sha, entry)
        before_asset = run_git_show(base_sha, entry.before_asset, binary=True)
        after_asset_path = entry.after_asset or entry.before_asset
        after_asset = run_git_show(head_sha, after_asset_path, binary=True)

        col = index % cols
        row = index // cols
        x = margin + col * (cell_width + gap)
        y = start_y + row * (cell_height + gap)

        draw.rounded_rectangle((x, y, x + cell_width, y + cell_height), radius=8, fill=panel, outline=border, width=2)
        draw.text((x + 12, y + 10), entry.label, fill=text, font=label_font)

        left_x = x + 18
        right_x = x + 174
        top_y = y + 34
        draw.rectangle((left_x, top_y, left_x + thumb, top_y + thumb), fill=bg, outline=border, width=1)
        draw.rectangle((right_x, top_y, right_x + thumb, top_y + thumb), fill=bg, outline=border, width=1)

        image.alpha_composite(fit_sprite(before_asset, thumb), (left_x, top_y))
        image.alpha_composite(fit_sprite(after_asset, thumb), (right_x, top_y))

        draw.text((left_x + 20, top_y + thumb + 6), "BEFORE", fill=before_color, font=tag_font)
        draw.text((right_x + 25, top_y + thumb + 6), "AFTER", fill=after_color, font=tag_font)
        draw.text((x + 12, y + 138), elide(before_name), fill=muted, font=small_font)
        draw.text((x + 170, y + 138), elide(after_name), fill=muted, font=small_font)

        rendered.append(
            {
                "label": entry.label,
                "entity_id": entry.entity_id,
                "before_name": before_name,
                "after_name": after_name,
                "before_asset": entry.before_asset,
                "after_asset": after_asset_path,
            }
        )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path)

    metadata = {
        "base_sha": base_sha,
        "head_sha": head_sha,
        "image": str(output_path),
        "entries": rendered,
    }

    if metadata_path is not None:
        metadata_path.parent.mkdir(parents=True, exist_ok=True)
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")

    return metadata


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate a before/after bluespace-to-redspace preview sheet.")
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
