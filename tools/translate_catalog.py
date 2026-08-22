#!/usr/bin/env python3
"""Build language JSON files from runtime-detected text and website references."""

import argparse
import json
import re
from pathlib import Path

JS_FIELD = re.compile(r'\b(?P<field>name|effect|text|description)\s*:\s*(?P<value>"(?:\\.|[^"\\])*")')
JS_OBJECT_NAME = re.compile(r'^\s*(?P<value>"(?:\\.|[^"\\])*")\s*:\s*\{', re.MULTILINE)
PROTECTED = re.compile(r'(<[^>]+>|\{\d+\}|\n)')
WORDS = re.compile(r'[^a-z0-9]+')
NUMBER_ONLY = re.compile(r'^\s*[-+]?\d+(?:[.,]\d+)?[xMm]?\s*$')


def read_json(path: Path) -> dict[str, str]:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}


def write_json(path: Path, values: dict[str, str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(dict(sorted(values.items())), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def normalize(text: str) -> str:
    text = re.sub(r"<[^>]+>", " ", text).replace("'''", " ").lower()
    text = re.sub(r"^\s*\d+\.\s*", "", text)
    return WORDS.sub(" ", text).strip()


def references(paths: list[Path]) -> tuple[set[str], set[str], set[str], set[str]]:
    names, descriptions, source_names, source_descriptions = set(), set(), set(), set()
    for path in paths:
        contents = path.read_text(encoding="utf-8-sig")
        for match in JS_OBJECT_NAME.finditer(contents):
            value = json.loads(match.group("value"))
            names.add(normalize(value))
            source_names.add(value)
        for match in JS_FIELD.finditer(contents):
            value = json.loads(match.group("value"))
            if not value:
                continue
            if match.group("field") == "name":
                names.add(normalize(value))
                source_names.add(value)
            else:
                source_descriptions.add(value)
                if value != "N/A":
                    descriptions.add(normalize(value))
    return names, {value for value in descriptions if len(value) >= 12}, source_names, source_descriptions


def in_scope(source: str, existing: dict[str, str], names: set[str], descriptions: set[str]) -> bool:
    if source in existing or "<color=" in source.lower():
        return True
    value = normalize(source)
    if value in names:
        return True
    return len(value) >= 12 and any(value.startswith(item) or item.startswith(value) for item in descriptions)


def translate_preserving_markup(source: str, translator) -> str:
    def translate_fragment(part: str) -> str:
        if PROTECTED.fullmatch(part) or not any(character.isalpha() for character in part):
            return part
        leading = part[:len(part) - len(part.lstrip())]
        trailing = part[len(part.rstrip()):]
        return leading + translator(part.strip()).strip() + trailing

    return "".join(translate_fragment(part) for part in PROTECTED.split(source))


def translate_name(source: str, translator) -> str:
    return translator(source.lower()).upper()


def translate_identity(source: str, translator) -> str:
    translated = translate_preserving_markup(source, lambda text: translator(text.lower()))
    visible_letters = "".join(character for character in PROTECTED.sub("", source) if character.isalpha())
    if visible_letters and visible_letters == visible_letters.upper():
        translated = "".join(
            part if PROTECTED.fullmatch(part) else part.upper()
            for part in PROTECTED.split(translated)
        )
    return translated


def validate(source: str, translated: str) -> None:
    source_markup = re.findall(r"<[^>]+>|\{\d+\}", source)
    translated_markup = re.findall(r"<[^>]+>|\{\d+\}", translated)
    if source_markup != translated_markup:
        raise ValueError(f"Markup changed while translating {source!r}")


def install_model(language: str) -> None:
    import argostranslate.package

    argostranslate.package.update_package_index()
    package = next((item for item in argostranslate.package.get_available_packages()
                    if item.from_code == "en" and item.to_code == language), None)
    if package is None:
        raise SystemExit(f"No Argos English -> {language} model is available.")
    argostranslate.package.install_from_path(package.download())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--detected", type=Path, required=True)
    parser.add_argument("--reference", type=Path, action="append", default=[])
    parser.add_argument("--seed", type=Path, action="append", default=[])
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--language", action="append", default=[])
    parser.add_argument("--glossary", type=Path)
    parser.add_argument("--install-models", action="store_true")
    parser.add_argument("--all-detected", action="store_true")
    parser.add_argument("--include-reference-names", action="store_true")
    parser.add_argument("--include-reference-descriptions", action="store_true")
    parser.add_argument("--retry-identity-names", action="store_true")
    parser.add_argument("--retry-identities", action="store_true")
    parser.add_argument("--index-only", action="store_true")
    args = parser.parse_args()

    detected = read_json(args.detected)
    seed_keys = {key for path in args.seed for key in read_json(path)}
    english = {key: key for key in set(detected) | seed_keys if any(character.isalpha() for character in key)}
    names, descriptions, source_names, source_descriptions = references(args.reference)
    if args.include_reference_names:
        english.update({name.upper(): name.upper() for name in source_names if any(character.isalpha() for character in name)})
    if args.include_reference_descriptions:
        english.update({description: description for description in source_descriptions})
    write_json(args.output_dir / "en.json", english)
    if args.index_only:
        print(f"English index: {len(english)} entries")
        return
    if not args.language:
        raise SystemExit("At least one --language is required unless --index-only is used.")
    glossary = read_json(args.glossary or args.output_dir / "glossary.json")

    for language in args.language:
        if args.install_models:
            install_model(language)
        output_path = args.output_dir / f"{language}.json"
        translated = read_json(output_path)
        selected = english if args.all_detected else {
            key: key for key in english if key in seed_keys or in_scope(key, translated, names, descriptions)
        }
        missing = [key for key in selected
                   if key not in translated and language not in glossary.get(key, {})]
        identity_names = [name.upper() for name in source_names
                          if name.upper() in selected and translated.get(name.upper()) == name.upper()]
        identity_values = [source for source in detected
                           if args.retry_identities
                           and source in selected
                           and translated.get(source) == source
                           and any(character.isalpha() for character in source)
                           and not NUMBER_ONLY.fullmatch(source)
                           and language not in glossary.get(source, {})]
        if missing or args.retry_identity_names and identity_names or identity_values:
            import argostranslate.translate
            engine = lambda text, code=language: argostranslate.translate.translate(text, "en", code)
        for index, source in enumerate(missing, 1):
            translated[source] = translate_preserving_markup(source, engine)
            validate(source, translated[source])
            print(f"[{language} {index}/{len(missing)}] {source[:70]!r}")
        if args.retry_identity_names:
            for index, source in enumerate(identity_names, 1):
                translated[source] = translate_name(source, engine)
                print(f"[{language} name {index}/{len(identity_names)}] {source!r}")
        for index, source in enumerate(identity_values, 1):
            translated[source] = translate_identity(source, engine)
            validate(source, translated[source])
            print(f"[{language} identity {index}/{len(identity_values)}] {source[:70]!r}")
        for source, languages in glossary.items():
            if source in selected and language in languages:
                translated[source] = languages[language]
        write_json(output_path, translated)
        print(f"{language}: {len(translated)} translations; {len(missing)} added")


def self_test() -> None:
    sample = "Damage <color=white>+{0}%</color>\nReady"
    translated = translate_preserving_markup(sample, lambda text: text.replace("Damage", "Daño").replace("Ready", "Listo"))
    assert translated == "Daño <color=white>+{0}%</color>\nListo"
    assert normalize("<b>Sluggish Rounds</b>") == "sluggish rounds"
    assert normalize("2. LOOSE CANNON") == "loose cannon"
    assert JS_OBJECT_NAME.search('  "Charged Reactor": {').group("value") == '"Charged Reactor"'
    assert translate_name("TEMPEST", lambda value: "tempestad") == "TEMPESTAD"
    assert translate_identity("HOLD <b>TO BUY</b>", lambda value: value.replace("hold", "mantener").replace("to buy", "para comprar")) == "MANTENER <b>PARA COMPRAR</b>"


if __name__ == "__main__":
    self_test()
    main()
