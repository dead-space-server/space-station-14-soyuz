#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass, field
from pathlib import Path

TEST_MARKER_PATTERN = re.compile(r"\[(?:Test|TestCase|TestCaseSource)\b")
UNIT_NAME_SPLIT_PATTERN = re.compile(r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")

DEFAULT_TESTS_ROOT = Path("Content.IntegrationTests") / "Tests"
DEFAULT_NAMESPACE_PREFIX = "Content.IntegrationTests.Tests"
DEFAULT_SHARD_COUNT = 10
MAX_SHARD_NAME_LENGTH = 96


@dataclass(frozen=True)
class TestUnit:
    name: str
    weight: int
    filter_expression: str


@dataclass
class Shard:
    index: int
    units: list[TestUnit] = field(default_factory=list)
    weight: int = 0

    def add(self, unit: TestUnit) -> None:
        self.units.append(unit)
        self.weight += unit.weight

    @property
    def id(self) -> str:
        return f"shard-{self.index + 1:02d}"

    def to_matrix_entry(self) -> dict[str, object]:
        sorted_units = sorted(self.units, key=lambda unit: unit.name)
        return {
            "id": self.id,
            "name": build_shard_name(self.units, self.weight),
            "tests": self.weight,
            "unit_count": len(sorted_units),
            "units": ", ".join(unit.name for unit in sorted_units),
            "filter": "|".join(unit.filter_expression for unit in sorted_units),
        }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate balanced GitHub Actions shards for Content.IntegrationTests.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--shards", type=int, default=DEFAULT_SHARD_COUNT)
    parser.add_argument("--tests-root", type=Path, default=DEFAULT_TESTS_ROOT)
    parser.add_argument("--namespace-prefix", default=DEFAULT_NAMESPACE_PREFIX)
    parser.add_argument("--pretty", action="store_true")
    args = parser.parse_args()

    units = discover_units(args.tests_root, args.namespace_prefix)
    if not units:
        raise SystemExit("No integration test units discovered.")

    shards = balance_units(units, args.shards)
    matrix = {"include": [shard.to_matrix_entry() for shard in shards if shard.units]}
    if args.pretty:
        for shard in matrix["include"]:
            print(
                f"{shard['id']}: {shard['name']}, "
                f"{shard['unit_count']} units -> {shard['units']}"
            )
    else:
        print(json.dumps(matrix, separators=(",", ":")))
    return 0


def discover_units(tests_root: Path, namespace_prefix: str) -> list[TestUnit]:
    if not tests_root.exists():
        raise SystemExit(f"Tests root does not exist: {tests_root}")

    units: list[TestUnit] = []

    for file_path in sorted(tests_root.glob("*.cs")):
        weight = count_test_markers(file_path)
        if weight == 0:
            continue

        stem = file_path.stem
        units.append(
            TestUnit(
                name=stem,
                weight=weight,
                filter_expression=f"FullyQualifiedName~{namespace_prefix}.{stem}",
            )
        )

    for directory in sorted(path for path in tests_root.iterdir() if path.is_dir()):
        weight = sum(count_test_markers(file_path) for file_path in directory.rglob("*.cs"))
        if weight == 0:
            continue

        units.append(
            TestUnit(
                name=directory.name,
                weight=weight,
                filter_expression=f"FullyQualifiedName~{namespace_prefix}.{directory.name}.",
            )
        )

    return sorted(units, key=lambda unit: (-unit.weight, unit.name))


def count_test_markers(file_path: Path) -> int:
    text = file_path.read_text(encoding="utf-8")
    return len(TEST_MARKER_PATTERN.findall(text))


def build_shard_name(units: list[TestUnit], weight: int) -> str:
    display_units = [humanize_unit_name(unit.name) for unit in sort_units_for_display(units)]
    suffix = f" ({weight} markers)"
    summary = summarize_display_units(display_units, MAX_SHARD_NAME_LENGTH - len(suffix))
    return f"{summary}{suffix}"


def sort_units_for_display(units: list[TestUnit]) -> list[TestUnit]:
    return sorted(units, key=lambda unit: (-unit.weight, unit.name))


def humanize_unit_name(unit_name: str) -> str:
    for suffix in ("Tests", "Test"):
        if unit_name.endswith(suffix):
            unit_name = unit_name[: -len(suffix)]
            break

    return UNIT_NAME_SPLIT_PATTERN.sub(" ", unit_name).strip()


def summarize_display_units(display_units: list[str], max_length: int) -> str:
    if not display_units:
        return "Unknown"

    selected: list[str] = []

    for name in display_units:
        candidate_units = selected + [name]
        remaining = len(display_units) - len(candidate_units)
        candidate = ", ".join(candidate_units)
        if remaining > 0:
            candidate = f"{candidate} +{remaining} more"

        if selected and len(candidate) > max_length:
            break

        selected.append(name)

    remaining = len(display_units) - len(selected)
    summary = ", ".join(selected)
    if remaining > 0:
        summary = f"{summary} +{remaining} more"

    return summary


def balance_units(units: list[TestUnit], shard_count: int) -> list[Shard]:
    if shard_count < 1:
        raise SystemExit("--shards must be at least 1.")

    shard_count = min(shard_count, len(units))
    shards = [Shard(index=index) for index in range(shard_count)]

    for unit in units:
        shard = min(shards, key=lambda candidate: (candidate.weight, len(candidate.units), candidate.index))
        shard.add(unit)

    return shards


if __name__ == "__main__":
    raise SystemExit(main())
