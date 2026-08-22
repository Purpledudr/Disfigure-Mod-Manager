#!/usr/bin/env python3
"""Write strings detected at runtime that have no exact translation key."""

import argparse
import json
from pathlib import Path


def read(path: Path) -> dict[str, str]:
    return json.loads(path.read_text(encoding="utf-8"))


def missing(detected: dict[str, str], translated: dict[str, str]) -> dict[str, str]:
    return {key: key for key in sorted(detected) if key not in translated}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("detected", type=Path)
    parser.add_argument("translated", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    result = missing(read(args.detected), read(args.translated))
    output = args.output or args.detected.with_name("missing_runtime_strings.json")
    temporary = output.with_suffix(output.suffix + ".tmp")
    temporary.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(output)
    print(f"{len(result)} exact translation misses -> {output}")


if __name__ == "__main__":
    assert missing({"Play": "Play", "Quit": "Quit"}, {"Play": "Jugar"}) == {"Quit": "Quit"}
    main()
