# DisfigureTranslationMod

BepInEx 6 IL2CPP runtime discovery and replacement for TextMeshPro and legacy Unity UI text in Disfigure. Loaded active and inactive text is pretranslated before menus are opened. Disfigure's two upgrade-popup builders are patched directly so completed popup titles and bodies are translated before the next rendered frame; periodic scans remain for discovery and fallback.

## Build

Prerequisites:

- .NET 6 SDK or newer
- The existing repository files
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

## Install

### All-in-one Windows x64 ZIP

1. Extract the ZIP into the Disfigure game directory beside `Disfigure.exe`.
2. Launch Disfigure. The first launch can take longer while BepInEx generates files for the installed game version.
3. Press F8 to choose a language.

This package includes the unmodified official BepInEx 6.0.0-be.785 Unity IL2CPP Windows x64 distribution. BepInEx is licensed under LGPL-2.1; its license and source/build links are included in the ZIP.

### Mod-only ZIP

1. Install BepInEx 6 Unity IL2CPP for Windows x64 and launch the game once.
2. Extract the mod-only ZIP into the Disfigure game directory, preserving its `BepInEx` folder structure.
3. Launch Disfigure and press F8.

The plugin creates its config and editable discovery files on first launch:

```text
BepInEx\config\DisfigureTranslationMod.cfg
BepInEx\translations\DisfigureTranslationMod\detected_strings.json
BepInEx\translations\DisfigureTranslationMod\en.json
BepInEx\translations\DisfigureTranslationMod\translated.json
```

The release ZIP supplies the maintained language catalogs. Existing config, detected strings, and custom translations are not overwritten.

## Translation format

Exact and placeholder translations share the simple JSON dictionary. They are separated internally when loaded:

```json
{
  "Play": "Translated Play",
  "Damage: {0}": "Translated Damage: {0}",
  "Level {0}": "Translated Level {0}"
}
```

`{0}`, `{1}`, and later numbered placeholders match signed integers, decimals, and percentages. For example, `Damage: {0}` matches `Damage: 25`. A timer can use two placeholders: `Time: {0}:{1}`. Rich-text tags and line breaks remain untouched.

The project sample is at `sample\translated.json`. The runtime-created file is intentionally empty so installing the plugin does not change English UI by default.

The maintained catalogs are under `translations`: `en.json` is the canonical detected-English index, with Spanish, French, Russian, German, Brazilian Portuguese, Simplified Chinese, Japanese, and Polish catalogs beside it. Each translated catalog currently covers the same 2,373 source keys. Machine-assisted descriptions still benefit from native-speaker review.

## Configure

Defaults:

- Plugin enabled
- Detection enabled
- Replacement enabled
- One scan per second
- Newly detected string logging disabled
- Dynamic patterns enabled
- `en.json` loaded
- F5 reloads translations
- F4 forces a rescan
- F8 opens the language picker
- Popup audit logging disabled

All values and hotkeys are configurable in `BepInEx\config\DisfigureTranslationMod.cfg`. Restart the game after changing `TranslationFilename`; F5 reloads the contents of the file selected at startup.

Press F8 in game to switch among installed language files. The selected filename is saved to the BepInEx config and used on the next launch.

## Update language catalogs

`tools\translate_catalog.py` merges new runtime detections without overwriting existing translations. It uses the supplied website data files only to identify gameplay names and descriptions; exact replacement keys always come from `detected_strings.json`. Argos Translate is optional and is used only to draft missing entries offline. Reviewed short terms live in `translations\glossary.json`.

`tools\repair_catalogs.py` is the stricter all-language repair path used for version 0.7.3. It derives a stable canonical source set from the installed baseline and live detector, translates complete lines through the free Google Translate web endpoint, checkpoints progress, preserves TMP tags/placeholders, retries malformed markup, removes known translated/debug contamination, and audits every result before accepting it.

## Test in game

1. Launch Disfigure and verify the BepInEx log contains `DisfigureTranslationMod loaded`, the translation path, and a startup scan count.
2. Open the main menu and settings, wait at least one scan interval, then inspect `detected_strings.json`.
3. Add a detected exact entry such as `"Play": "Translated Play"` to `translated.json`.
4. Press F5. Verify the log reports the exact/pattern counts and a `translation reload` scan with at least one replacement; verify the visible label changes.
5. Change scenes or revisit the menu and verify the translation is reapplied if the game restores `Play`.
6. Add `"Level {0}": "Translated Level {0}"`, reload, and open a screen containing a changing level value.
7. Temporarily put malformed JSON in `translated.json`, press F5, and verify a parsing error is logged while the game keeps running. Restore valid JSON afterward.
8. Test target-language characters. A missing-glyph warning includes the active font name and affected translated text.

The v0.7.8 all-in-one package has passed a clean-install launch test on Windows x64. Compilation and that smoke test do not prove every screen and language combination; report missed or overlapping text with a screenshot and the active language.

## Best screens for collecting strings

- Main menu, weapon selection, profile/statistics, compendium, and every settings tab
- Switch between installed language catalogs with F8; already translated visible text is converted immediately through the canonical English key
- Short single-line labels that exceed their original UI rectangle use Unity best-fit sizing to avoid row overlaps
- Tutorial prompts and any Steam/reset-data tooltip
- Gameplay HUD, pause menu, and in-game settings
- Level-up choices, upgrade paths, mutation choices, delete-upgrade UI, and all hover tooltips
- Weapon unlocks, achievements, boss UI, death screen, and end-of-run totals

## Cases that may later need targeted patches

- Text created and destroyed between scans, especially very short tooltips or notifications
- Popups built by a game method other than the two currently targeted hooks
- Text rendered through IMGUI, sprites, custom meshes, or another non-component renderer
- Source strings assembled in game logic where a useful placeholder pattern cannot describe the result

If runtime testing finds one of these, patch the narrow game method that owns that UI.
