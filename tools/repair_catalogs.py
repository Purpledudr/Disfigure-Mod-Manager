#!/usr/bin/env python3
"""Repair language catalogs from canonical runtime text using free Google Translate."""

import argparse
import json
import re
import time
from collections import Counter
from pathlib import Path

from deep_translator import GoogleTranslator

PROTECTED = re.compile(r"<[^>]+>|\{\d+\}")
PLACEHOLDER = re.compile(r"\{\d+\}")
FOREIGN_SOURCE = re.compile(
    r"[áéíóúñ¿¡]|[\u0400-\u052f\u3040-\u30ff\u3400-\u9fff]|"
    r"\b(?:Cada uno|Cada vez|Daño de|Daños por|Las|Los|Mientras|Minibosses desove|"
    r"Por todos|Se vuelve|Su salud|Tarifa de|Tasa de|Tamaño de|Todos los|"
    r"Velocidad de|Corazón)\b",
    re.IGNORECASE,
)
ARTIFACT = re.compile(r"\{\\(?:fn|fs|bord|shad)|\\[34]aH|阿基姆波语Name")
GOOGLE_CODES = {"zh": "zh-CN"}
EXTRA_TERMS = {
    "es": {
        "Circle y Cone Vision": "Las visiones circular y cónica",
        "Circle Vision": "Visión circular", "Cone Vision": "Visión cónica",
        "Vision Circles": "círculos de visión", "Vision": "Visión",
        "Wind Up Strike": "Golpe cargado", "Sentry": "Centinela",
        "Akimbo SMGs": "subfusiles dobles", "Akimbo": "armas dobles",
        "Akimbo SMG": "subfusil doble",
        "atravesarán <color=white></color> a través de": "<color=white>atravesarán</color>",
        "están activos simultáneamente": "están activas simultáneamente",
        "RMB": "botón derecho", "LMB": "botón izquierdo",
    },
    "fr": {
        "Circle et Cone Vision": "Les visions circulaire et conique",
        "Circle Vision": "Vision circulaire", "Cone Vision": "Vision conique",
        "Vision Circles": "cercles de vision", "Vision": "Vision",
        "Wind Up Strike": "Frappe chargée", "Sentry": "Tourelle",
        "Akimbo SMGs": "PM en akimbo", "SMG Akimbo": "PM en akimbo",
        "sont actifs simultanément": "sont actives simultanément",
        "RMB": "clic droit", "LMB": "clic gauche",
    },
    "ru": {
        "Circle Vision": "Круговой обзор", "Cone Vision": "Конусный обзор",
        "Vision Circles": "Круги обзора", "Vision": "Обзор",
        "Wind Up Strike": "Заряженный удар", "Sentry": "Турель",
        "Akimbo SMGs": "парные пистолеты-пулемёты",
        "пистолетами-пулеметами Akimbo": "парными пистолетами-пулемётами",
        "Akimbo": "парное оружие",
        "RMB": "ПКМ", "LMB": "ЛКМ",
    },
    "de": {
        "Circle und Cone Vision": "Kreis- und Kegelsicht",
        "Circle Vision": "Kreissicht", "Cone Vision": "Kegelsicht",
        "Vision Circles": "Sichtkreise", "Vision": "Sicht",
        "Wind Up Strike": "Aufgeladener Schlag", "Sentry": "Geschützturm",
        "RMB": "rechte Maustaste", "LMB": "linke Maustaste",
    },
    "pt": {
        "Circle e Cone Vision": "As visões circular e cônica",
        "Circle Vision": "Visão circular", "Cone Vision": "Visão cônica",
        "Vision Circles": "círculos de visão", "Vision": "Visão",
        "Wind Up Strike": "Golpe carregado", "Sentry": "Torreta",
        "Akimbo SMGs": "submetralhadoras duplas", "SMGs Akimbo": "submetralhadoras duplas",
        "estão ativos simultaneamente": "estão ativas simultaneamente",
        "Akimbo": "armas duplas",
        "RMB": "botão direito", "LMB": "botão esquerdo",
    },
    "zh": {
        "Circle 和 Cone Vision": "圆形与锥形视野", "圆形视觉": "圆形视野",
        "Circle Vision": "圆形视野", "Cone Vision": "锥形视野",
        "Vision Circles": "视野圆环", "Vision": "视野",
        "Wind Up Strike": "蓄力斩", "Psych Trail": "心灵轨迹",
        "Sentry": "哨兵", "Blaze": "烈焰", "Akimbo SMGs": "双持冲锋枪",
        "Akimbo SMG": "双持冲锋枪", "Akimbo": "双持",
        "人民币": "鼠标右键", "RMB": "鼠标右键", "LMB": "鼠标左键",
    },
    "ja": {
        "Circle Vision": "円形視界", "Cone Vision": "扇形視界",
        "Vision Circles": "視界サークル", "Vision": "視界",
        "Wind Up Strike": "溜め斬り", "Psych Trail": "サイコトレイル",
        "Sentry": "セントリー", "Blaze": "炎", "Akimbo SMGs": "二丁持ちSMG",
        "Akimbo": "二丁持ち", "ビジョン": "視界",
        "RMB": "マウス右ボタン", "LMB": "マウス左ボタン",
    },
}

PROGRESSION_LABELS = {
    "de": {"kills": "{weapon} ABSCHÜSSE {roman}", "mod": "{weapon} MOD {roman}", "kit": "{weapon}-SET", "win": "{weapon}-SIEG", "expert": "{weapon}-EXPERTE", "unlocked": "{weapon} FREIGESCHALTET", "Golden": "GOLD", "Ruby": "RUBIN", "Diamond": "DIAMANT"},
    "es": {"kills": "BAJAS CON {weapon} {roman}", "mod": "MOD DE {weapon} {roman}", "kit": "KIT DE {weapon}", "win": "VICTORIA CON {weapon}", "expert": "EXPERTO EN {weapon}", "unlocked": "{weapon} DESBLOQUEADO", "Golden": "DORADO", "Ruby": "RUBÍ", "Diamond": "DIAMANTE"},
    "fr": {"kills": "ÉLIMINATIONS {weapon} {roman}", "mod": "MOD {weapon} {roman}", "kit": "KIT {weapon}", "win": "VICTOIRE {weapon}", "expert": "EXPERT {weapon}", "unlocked": "{weapon} DÉBLOQUÉ", "Golden": "OR", "Ruby": "RUBIS", "Diamond": "DIAMANT"},
    "ru": {"kills": "{weapon}: УБИЙСТВА {roman}", "mod": "МОД {weapon} {roman}", "kit": "КОМПЛЕКТ {weapon}", "win": "ПОБЕДА С {weapon}", "expert": "ЭКСПЕРТ {weapon}", "unlocked": "{weapon} РАЗБЛОКИРОВАНО", "Golden": "ЗОЛОТО", "Ruby": "РУБИН", "Diamond": "АЛМАЗ"},
    "pt": {"kills": "ABATES COM {weapon} {roman}", "mod": "MOD DE {weapon} {roman}", "kit": "KIT DE {weapon}", "win": "VITÓRIA COM {weapon}", "expert": "ESPECIALISTA EM {weapon}", "unlocked": "{weapon} DESBLOQUEADO", "Golden": "DOURADO", "Ruby": "RUBI", "Diamond": "DIAMANTE"},
    "zh": {"kills": "{weapon} 击杀 {roman}", "mod": "{weapon} 模组 {roman}", "kit": "{weapon} 套装", "win": "{weapon} 胜利", "expert": "{weapon} 专家", "unlocked": "{weapon} 已解锁", "Golden": "黄金", "Ruby": "红宝石", "Diamond": "钻石"},
    "ja": {"kills": "{weapon} キル {roman}", "mod": "{weapon} MOD {roman}", "kit": "{weapon} キット", "win": "{weapon} 勝利", "expert": "{weapon} エキスパート", "unlocked": "{weapon} アンロック済み", "Golden": "ゴールド", "Ruby": "ルビー", "Diamond": "ダイヤモンド"},
    "pl": {"kills": "ZABÓJSTWA {weapon} {roman}", "mod": "MOD {weapon} {roman}", "kit": "ZESTAW {weapon}", "win": "ZWYCIĘSTWO {weapon}", "expert": "EKSPERT {weapon}", "unlocked": "{weapon} ODBLOKOWANO", "Golden": "ZŁOTY", "Ruby": "RUBINOWY", "Diamond": "DIAMENTOWY"},
}
WEAPON_NAMES = (
    "AKIMBO SMGS", "BATTLE AXE", "BOOMERANG SCYTHE", "BURST RIFLE", "FLAMETHROWER",
    "GAUSS KATANA", "GREATSWORD", "GRENADE LAUNCHER", "HANDCANNON", "KNIFE",
    "LASER CATALYST", "LEVER-ACTION RIFLE", "MINIGUN", "PISTOL", "PULSE RIFLE",
    "RAILGUN", "SAW LAUNCHER", "SHOTGUN", "SNIPER", "TWIN KATANAS",
)
WEAPON_NAME_OVERRIDES = {
    "es": {"AKIMBO SMGS": "SUBFUSILES DOBLES", "FLAMETHROWER": "LANZALLAMAS"},
    "ru": {"FLAMETHROWER": "ОГНЕМЁТ"},
    "pt": {"AKIMBO SMGS": "SUBMETRALHADORAS DUPLAS", "FLAMETHROWER": "LANÇA-CHAMAS"},
    "zh": {"AKIMBO SMGS": "双持冲锋枪", "FLAMETHROWER": "火焰喷射器"},
    "ja": {"AKIMBO SMGS": "二丁持ちSMG", "FLAMETHROWER": "火炎放射器"},
    "pl": {"AKIMBO SMGS": "PODWÓJNE PISTOLETY MASZYNOWE", "FLAMETHROWER": "MIOTACZ OGNIA"},
}
IDENTITY_TEXT = {
    "Aliaskey Vasilieu", "ArisTheMage", "Arlensoul", "Arvid Eapen", "Be Happy™",
    "Bera Gedikli", "Biggest Boss (knockout)", "Billbro Baggins", "Bull", "cheese",
    "Cipher", "Darkhellthepro", "DevilAnjel", "Ezgi Akhan", "Ho Kim Quang", "Isorn!",
    "jw", "Kevin Kindheart", "kira anão", "Mechanism Y", "Schtroumph perdant", "senpai",
    "Shaunak Sawant", "sweaty sleeves bro", "that guy", "The Ghost", "The Mauler",
    "The Variable Man", "THEANATOLIEN", "Vikram Ramesh", "Zoe “Squinch” Allen",
    "☢ Kotzkreis der Duftige",
}


def repair_progression_labels(catalog: dict[str, str], language: str) -> None:
    labels = PROGRESSION_LABELS.get(language)
    if not labels:
        return
    names = WEAPON_NAME_OVERRIDES.get(language, {})
    for source_weapon in WEAPON_NAMES:
        weapon = names.get(source_weapon, catalog.get(source_weapon, source_weapon)).upper()
        if source_weapon in names:
            catalog[source_weapon] = weapon
        for roman in ("I", "II", "III"):
            for suffix in ("kills", "mod"):
                key = f"{source_weapon} {suffix.upper()} {roman}"
                if key in catalog:
                    catalog[key] = labels[suffix].format(weapon=weapon, roman=roman)
        for suffix in ("kit", "win", "expert", "unlocked"):
            key = f"{source_weapon} {suffix.upper()}"
            if key in catalog:
                catalog[key] = labels[suffix].format(weapon=weapon)
        for tier in ("Golden", "Ruby", "Diamond"):
            key = f"{tier.upper()} {source_weapon}"
            if key in catalog:
                catalog[key] = f"{labels[tier]} {weapon}"


def read_json(path: Path) -> dict[str, str]:
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}


def write_json(path: Path, values: dict[str, str]) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(dict(sorted(values.items())), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary.replace(path)


def protect(text: str) -> tuple[str, dict[str, str]]:
    replacements: dict[str, str] = {}

    def replace(match: re.Match[str]) -> str:
        original = match.group(0)
        marker = f"[ZXQP{len(replacements)}]"
        replacements[marker] = original
        return marker

    return PLACEHOLDER.sub(replace, text), replacements


def protect_all(text: str) -> tuple[str, dict[str, str]]:
    replacements: dict[str, str] = {}

    def replace(match: re.Match[str]) -> str:
        marker = f"[ZXQ{len(replacements)}]"
        replacements[marker] = match.group(0)
        return marker

    return PROTECTED.sub(replace, text), replacements


def restore(source: str, translated: str, replacements: dict[str, str]) -> str:
    for marker, original in replacements.items():
        if translated.count(marker) != 1:
            raise ValueError(f"Translation changed protected marker {marker} in {source!r}")
        translated = translated.replace(marker, original)
    if "[ZXQ" in translated:
        raise ValueError(f"Translation left an unknown protected marker in {source!r}")
    return translated


def repair_closing_tags(source: str, translated: str) -> str:
    source_named_colors = re.findall(r"<color=([^#][^>]*)>", source, re.IGNORECASE)
    if source_named_colors and all(color.lower() == "white" for color in source_named_colors):
        translated = re.sub(
            r"<color=(?!#)[^>]+>", "<color=white>", translated, flags=re.IGNORECASE
        )
    source_tags = Counter(tag.lower() for tag in PROTECTED.findall(source) if tag.startswith("<"))
    translated_tags = Counter(tag.lower() for tag in PROTECTED.findall(translated) if tag.startswith("<"))
    missing = source_tags - translated_tags
    extra = translated_tags - source_tags
    if not extra and missing and all(tag.startswith("</") for tag in missing):
        trailing = translated[len(translated.rstrip()) :]
        translated = translated.rstrip() + "".join(
            tag * count for tag, count in missing.items()
        ) + trailing
    return translated


def markup_matches(source: str, translated: str) -> bool:
    return sorted(tag.lower() for tag in PROTECTED.findall(source)) == sorted(
        tag.lower() for tag in PROTECTED.findall(translated)
    )


def batches(items: list[str], limit: int = 4200):
    batch: list[str] = []
    length = 0
    for item in items:
        added = len(item) + 32
        if batch and length + added > limit:
            yield batch
            batch, length = [], 0
        batch.append(item)
        length += added
    if batch:
        yield batch


def request(translator: GoogleTranslator, text: str) -> str:
    for attempt in range(5):
        try:
            result = translator.translate(text)
            if result:
                return result
        except Exception:
            if attempt == 4:
                raise
            time.sleep(2 ** attempt)
    raise RuntimeError("Translation returned no text.")


def translate_lines(lines: list[str], language: str, cache_path: Path) -> dict[str, str]:
    translator = GoogleTranslator(source="en", target=GOOGLE_CODES.get(language, language))
    cache = read_json(cache_path)
    cache = {
        source: repaired
        for source, translated in cache.items()
        if markup_matches(source, repaired := repair_closing_tags(source, translated))
    }
    pending = [line for line in lines if line not in cache]

    for number, batch in enumerate(batches(pending), 1):
        protected: list[tuple[str, dict[str, str]]] = [protect(line) for line in batch]
        boundaries = [f"<zxqboundary{i}/>" for i in range(len(batch) - 1)]
        joined = "\n".join(
            part
            for index, (part, _) in enumerate(protected)
            for part in ([part, boundaries[index]] if index < len(boundaries) else [part])
        )
        output = request(translator, joined)
        translated_parts: list[str] = []
        position = 0
        for boundary in boundaries:
            found = output.find(boundary, position)
            if found < 0:
                translated_parts = []
                break
            translated_parts.append(output[position:found].strip())
            position = found + len(boundary)
        if translated_parts or len(batch) == 1:
            translated_parts.append(output[position:].strip())

        if len(translated_parts) != len(batch):
            translated_parts = [request(translator, item).strip() for item, _ in protected]

        for source, translated, (_, replacements) in zip(batch, translated_parts, protected):
            try:
                value = repair_closing_tags(source, restore(source, translated, replacements))
            except ValueError:
                single = request(translator, protect(source)[0]).strip()
                value = repair_closing_tags(source, restore(source, single, replacements))
            if not markup_matches(source, value):
                protected_all, all_replacements = protect_all(source)
                value = restore(source, request(translator, protected_all).strip(), all_replacements)
            if not markup_matches(source, value):
                raise ValueError(f"Could not preserve rich-text markup in {source!r}")
            cache[source] = value

        write_json(cache_path, cache)
        print(f"[{language}] batch {number}: {len(cache)}/{len(lines)} unique lines")

    return cache


def translate_source(source: str, line_translations: dict[str, str]) -> str:
    translated: list[str] = []
    for line in source.splitlines(keepends=True):
        ending = "\r\n" if line.endswith("\r\n") else "\n" if line.endswith("\n") else ""
        content = line[: -len(ending)] if ending else line
        leading = content[: len(content) - len(content.lstrip())]
        trailing = content[len(content.rstrip()) :]
        core = content.strip()
        value = repair_closing_tags(core, line_translations[core]) if core else ""
        translated.append(leading + value + trailing + ending)
    return "".join(translated)


def canonical_sources(directory: Path, detected_path: Path) -> list[str]:
    indexed = read_json(directory / "en.json")
    if indexed:
        return sorted(indexed)

    baseline = detected_path.parent
    catalogs = [read_json(path) for path in baseline.glob("??.json") if path.stem != "en"]
    outputs = {value for catalog in catalogs for value in catalog.values()}
    spanish_sources = set(read_json(baseline / "es.json"))
    detected_sources = set(read_json(detected_path))
    candidates = spanish_sources | {source for source in detected_sources if source not in outputs}
    return sorted(
        source
        for source in candidates
        if source.strip()
        and not FOREIGN_SOURCE.search(source)
        and not ARTIFACT.search(source)
        and not re.search(r"[A-Za-z]{40}", source)
    )


def audit(source_keys: list[str], catalog: dict[str, str], language: str) -> None:
    missing = [source for source in source_keys if not catalog.get(source)]
    markup_errors = [
        source
        for source in source_keys
        if sorted(tag.lower() for tag in PROTECTED.findall(source))
        != sorted(tag.lower() for tag in PROTECTED.findall(catalog.get(source, "")))
    ]
    artifacts = [
        source for source in source_keys if ARTIFACT.search(catalog[source]) and not ARTIFACT.search(source)
    ]
    if missing or markup_errors or artifacts:
        raise ValueError(
            f"{language} audit failed: {len(missing)} missing, "
            f"{len(markup_errors)} markup errors, {len(artifacts)} artifacts"
        )
    identities = sum(
        1 for source in source_keys if len(source) >= 20 and catalog[source] == source
    )
    print(f"{language}: {len(catalog)} entries; {identities} unchanged long English strings")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--detected", type=Path, required=True)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--language", action="append", required=True)
    parser.add_argument("--refresh", action="append", default=[])
    args = parser.parse_args()

    sources = canonical_sources(args.directory, args.detected)
    write_json(args.directory / "en.json", {source: source for source in sources})
    overrides = read_json(args.directory / "catalog_overrides.json")
    glossary = read_json(args.directory / "glossary.json")
    cache_directory = args.directory / ".repair-cache"
    cache_directory.mkdir(exist_ok=True)

    for language in args.language:
        path = args.directory / f"{language}.json"
        old = read_json(path)
        catalog = {} if language in args.refresh else {
            source: old[source] for source in sources if source in old and old[source]
        }
        missing = [source for source in sources if source not in catalog]
        unique_lines = sorted(
            {
                line.strip()
                for source in missing
                for line in source.splitlines()
                if line.strip()
            }
        )
        translations = translate_lines(
            unique_lines, language, cache_directory / f"{language}.json"
        )
        for source in missing:
            catalog[source] = translate_source(source, translations)
        for source, languages in glossary.items():
            if source in catalog and language in languages:
                catalog[source] = languages[language]
        terms = dict(overrides.get(language, {}).get("terms", {}))
        terms.update(EXTRA_TERMS.get(language, {}))
        for source in catalog:
            for term in sorted(terms, key=len, reverse=True):
                pattern = re.escape(term)
                if re.search(r"[A-Za-z]", term):
                    pattern = rf"(?<![A-Za-z]){pattern}(?![A-Za-z])"
                catalog[source] = re.sub(
                    pattern, terms[term], catalog[source], flags=re.IGNORECASE
                )
        for source, translated in overrides.get(language, {}).get("exact", {}).items():
            if source in catalog:
                catalog[source] = translated
        if language == "ru":
            for source, translated in read_json(args.directory / "ru_overrides.json").items():
                if source in catalog:
                    catalog[source] = translated
        repair_progression_labels(catalog, language)
        for source in IDENTITY_TEXT:
            if source in catalog:
                catalog[source] = source
        write_json(path, catalog)
        audit(sources, catalog, language)


if __name__ == "__main__":
    main()
