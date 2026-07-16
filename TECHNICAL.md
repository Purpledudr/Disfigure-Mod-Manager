# DisfigureTranslationMod technical information

DisfigureTranslationMod is a BepInEx 6 IL2CPP plugin that discovers and replaces TextMeshPro and legacy Unity UI text at runtime. Loaded active and inactive text is pretranslated before menus open. Targeted game-method patches translate upgrade and tooltip content immediately; periodic scans remain for discovery and fallback.

## Build

Prerequisites:

- .NET 6 SDK or newer
- A BepInEx 6 IL2CPP Disfigure installation containing generated interop assemblies

From the repository root:

```powershell
dotnet build .\DisfigureTranslationMod.csproj -c Release
```

The project defaults to `D:\SteamLibrary\steamapps\common\Disfigure`. Override another installation without editing the project:

```powershell
dotnet build .\DisfigureTranslationMod.csproj -c Release -p:DisfigureDir="C:\path\to\Disfigure"
```

Expected DLL:

```text
bin\Release\net6.0\DisfigureTranslationMod.dll
```

## Packages

The all-in-one Windows x64 package includes the unmodified official BepInEx 6.0.0-be.785 Unity IL2CPP distribution. BepInEx is licensed under LGPL-2.1; its license and source/build links are included in the ZIP.

The mod-only package requires BepInEx 6 Unity IL2CPP to be installed first.

The plugin creates these editable files on first launch:

```text
BepInEx\config\DisfigureTranslationMod.cfg
BepInEx\translations\DisfigureTranslationMod\detected_strings.json
BepInEx\translations\DisfigureTranslationMod\en.json
BepInEx\translations\DisfigureTranslationMod\translated.json
```

Existing configuration, detected strings, and custom translations are not overwritten.

## Translation format

Exact and placeholder translations share a JSON dictionary:

```json
{
  "Play": "Translated Play",
  "Damage: {0}": "Translated Damage: {0}",
  "Level {0}": "Translated Level {0}"
}
```

Numbered placeholders match signed integers, decimals, and percentages. Rich-text tags and line breaks remain untouched. Exact and placeholder entries are separated internally when loaded.

The maintained catalogs are in `translations`. `en.json` is the canonical English index. Each translated catalog covers the same 2,373 source keys.

## Configuration

Defaults:

- Plugin, detection, replacement, and dynamic patterns enabled
- One scan per second
- Newly detected string and popup-audit logging disabled
- `en.json` loaded
- F5 reloads translations
- F4 forces a rescan
- F8 opens the language picker

All values and hotkeys are configurable in `BepInEx\config\DisfigureTranslationMod.cfg`. The selected language file is saved in that configuration.

## Catalog maintenance

`tools\translate_catalog.py` merges runtime detections without overwriting existing translations. `tools\repair_catalogs.py` performs the stricter all-language repair and audit pass. Both preserve placeholders and rich-text tags. Reviewed short terms live in `translations\glossary.json`.

## Testing

1. Launch Disfigure and confirm the BepInEx log contains `DisfigureTranslationMod loaded`, the translation path, and a startup scan count.
2. Visit the main menu, settings, weapon selection, upgrades, perks, mutations, gameplay HUD, pause screen, tooltips, and end screen.
3. Press F8 and verify visible text changes immediately when switching languages.
4. Press F5 and verify the active catalog reloads.
5. Check target-language characters, long labels, wrapping, and overlaps.
6. Inspect `detected_strings.json` for newly exposed English text.

The v0.7.8 all-in-one package passed a clean-install launch test on Windows x64. Compilation and that smoke test do not prove every screen and language combination.

## Cases that may need targeted patches

- Text created and destroyed between scans
- Popups built outside the currently patched game methods
- Text rendered through IMGUI, sprites, custom meshes, or another non-component renderer
- Dynamically assembled source strings that cannot be represented by a useful placeholder pattern

When one is found, the intended fix is a narrow patch to the game method that owns that UI.
