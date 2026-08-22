#!/usr/bin/env python3
"""Add useful newly detected English UI strings to the canonical catalog."""

import argparse
import json
import re
from pathlib import Path


NON_ENGLISH = re.compile(r"[\u0400-\u052f\u3040-\u30ff\u3400-\u9fff]")
NUMBER_ONLY = re.compile(
    r"^[\s+\-<>/.:,%$€£¥₽₩₹0-9xXMKmksh]+$|"
    r"^\d+(?:\.\d+)?[MK]?$|^\d+\s+remaining\b|^Loading\.\.\.\s*\d+%$",
    re.IGNORECASE,
)
SINGLE_USERNAME = re.compile(r"^\.?[A-Za-z][A-Za-z0-9_.-]{2,31}$")
ARTIFACT = re.compile(r"\{\\(?:fn|fs|bord|shad)|\\[34]aH|阿基姆波语Name|[A-Za-z]{40}")
FOREIGN_TEXT = re.compile(
    r"\b(?:EINSTELLUNGEN|FÜR|KARTE|MENGEN|Nahkampf(?:schaden|größe|reichweite)|"
    r"Projektile|SPIELEN|VOLLBILDSCHIRM)\b",
    re.IGNORECASE,
)
ALLOW_SINGLE_WORD = {
    "Bull",
    "Cipher",
    "CLEAR",
    "FLAMETHROWER",
}


def read(path: Path) -> dict[str, str]:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}


def write(path: Path, values: dict[str, str]) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(dict(sorted(values.items())), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary.replace(path)


def useful(source: str, translated_outputs: set[str]) -> bool:
    text = source.strip()
    undecorated = text[1:].strip() if text.startswith(">") else text
    if not text or text in translated_outputs or undecorated in translated_outputs:
        return False
    if not re.search(r"[A-Za-z]", text) or NON_ENGLISH.search(text):
        return False
    if NUMBER_ONLY.fullmatch(text) or ARTIFACT.search(text) or FOREIGN_TEXT.search(text):
        return False
    if "�" in text or text.startswith(".") or text.startswith("http://") or text.startswith("https://"):
        return False
    if SINGLE_USERNAME.fullmatch(text) and text not in ALLOW_SINGLE_WORD and not text.isupper():
        return False
    return True


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--detected", type=Path, required=True)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    detected = read(args.detected)
    english_path = args.directory / "en.json"
    english = read(english_path)
    outputs = {
        value
        for path in args.directory.glob("??.json")
        if path.stem != "en"
        for value in read(path).values()
    }
    additions = {
        source: source
        for source in detected
        if source not in english and useful(source, outputs)
    }

    print(f"{len(additions)} useful strings selected from {len(detected) - len(english.keys() & detected.keys())} misses")
    if args.dry_run:
        for source in sorted(additions):
            print(source.replace("\n", "\\n"))
        return

    english.update(additions)
    write(english_path, english)
    print(f"{len(english)} canonical English strings -> {english_path}")


if __name__ == "__main__":
    main()
