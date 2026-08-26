#!/usr/bin/env python3

import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "translations" / "community_translation_template.json"
COMMUNITY = ROOT / "community-translations"
NUMBER_ONLY = re.compile(r"^[+-]?\d+(?:\.\d+)?(?:%|x|s)?$")


def load(path):
    with path.open(encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise ValueError("top level must be a JSON object")
    return value


def main():
    template = load(TEMPLATE)
    expected_keys = list(template)
    failures = []

    for path in sorted(COMMUNITY.glob("*.json")):
        try:
            catalog = load(path)
            if list(catalog) != expected_keys:
                failures.append(f"{path.name}: source keys were changed, removed, added, or reordered")
                continue
            if any(not isinstance(value, str) for value in catalog.values()):
                failures.append(f"{path.name}: every translation value must be a string")
            if any(
                value
                for key, value in catalog.items()
                if key.startswith(("__SECTION_", "__COMMENT_")) or NUMBER_ONLY.fullmatch(key)
            ):
                failures.append(f"{path.name}: section, comment, and number-only rows must stay blank")
        except (OSError, ValueError, json.JSONDecodeError) as error:
            failures.append(f"{path.name}: {error}")

    if failures:
        raise SystemExit("\n".join(failures))
    print(f"Validated {len(list(COMMUNITY.glob('*.json')))} community translation files.")


if __name__ == "__main__":
    main()
