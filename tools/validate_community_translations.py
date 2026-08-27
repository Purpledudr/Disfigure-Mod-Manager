#!/usr/bin/env python3

import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "translations" / "community_translation_template.json"
COMMUNITY = ROOT / "community-translations"
BLANK = COMMUNITY / "Blank-Translations"
CURRENT = COMMUNITY / "Current-Translations"
ADDITIONAL = COMMUNITY / "Additional-Blank-Translations"
NUMBER_ONLY = re.compile(r"^[+-]?\d+(?:\.\d+)?(?:%|x|s)?$")
ADDITIONAL_TEMPLATES = {
    "Documentation": ROOT / "translations" / "community_documentation_template.json",
    "Mod-Manager": ROOT / "translations" / "community_mod_manager_template.json",
    "Sandbox-Mod": ROOT / "translations" / "community_sandbox_mod_template.json",
}


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

    blank_paths = sorted(BLANK.glob("*.json"))
    current_paths = sorted(CURRENT.glob("*.json"))
    if {path.name for path in blank_paths} != {path.name for path in current_paths}:
        failures.append("current and blank folders must contain the same full language filenames")

    for path in blank_paths:
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

    for path in current_paths:
        try:
            catalog = load(path)
            if any(not isinstance(value, str) for value in catalog.values()):
                failures.append(f"{path.name}: every current translation value must be a string")
        except (OSError, ValueError, json.JSONDecodeError) as error:
            failures.append(f"{path.name}: {error}")

    language_names = {path.name for path in blank_paths}
    additional_count = 0
    for folder, template_path in ADDITIONAL_TEMPLATES.items():
        template_keys = list(load(template_path))
        paths = sorted((ADDITIONAL / folder).glob("*.json"))
        additional_count += len(paths)
        if {path.name for path in paths} != language_names:
            failures.append(f"{folder}: must contain the same full language filenames as Blank-Translations")
        for path in paths:
            try:
                catalog = load(path)
                if list(catalog) != template_keys:
                    failures.append(f"{folder}/{path.name}: source keys were changed, removed, added, or reordered")
                    continue
                if any(not isinstance(value, str) for value in catalog.values()):
                    failures.append(f"{folder}/{path.name}: every translation value must be a string")
                if any(
                    value
                    for key, value in catalog.items()
                    if key.startswith(("__SECTION_", "__COMMENT_"))
                ):
                    failures.append(f"{folder}/{path.name}: section and comment rows must stay blank")
            except (OSError, ValueError, json.JSONDecodeError) as error:
                failures.append(f"{folder}/{path.name}: {error}")

    if failures:
        raise SystemExit("\n".join(failures))
    print(
        f"Validated {len(blank_paths)} blank game files, {len(current_paths)} current game files, "
        f"and {additional_count} additional blank files."
    )


if __name__ == "__main__":
    main()
