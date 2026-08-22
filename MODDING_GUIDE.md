# Building similar BepInEx 6 IL2CPP translation mods

This guide records the useful lessons from building DisfigureTranslationMod. It is intended for runtime translation mods that do not modify game assets, save files, or the original executable.

## 1. Start from a working IL2CPP setup

Do not replace a working BepInEx installation to start a new plugin. Reuse its target framework, package versions, references, and loading pattern.

For Disfigure, the proven baseline is:

- BepInEx 6 IL2CPP
- .NET 6
- `BepInEx.Unity.IL2CPP.BasePlugin`
- `[BepInPlugin]` for plugin metadata
- `AddComponent<T>()` for the runtime `MonoBehaviour`
- Harmony only for game methods that scanning cannot cover promptly

Run the game once before building so BepInEx generates `BepInEx\interop`. Reference BepInEx libraries from `BepInEx\core` and game/Unity interop assemblies from `BepInEx\interop`. The current reference list is in `DisfigureTranslationMod.csproj`.

A minimal entry point looks like this:

```csharp
[BepInPlugin("author.game.mod", "ExampleMod", "1.0.0")]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        new Harmony("author.game.mod").PatchAll(typeof(Plugin).Assembly);
        AddComponent<RuntimeBehaviour>();
    }
}
```

Copying a normal Mono Unity plugin without checking its APIs is unreliable. Use the generated IL2CPP interop types and confirm constructors, arrays, events, and overloads against the installed assemblies.

## 2. Inspect before patching

Use the existing plugins and generated assemblies as the first documentation. Search the decompiled game wrappers for:

```text
TMP_Text
TextMeshProUGUI
UnityEngine.UI.Text
.text
localization / localisation
language / locale
tooltip / description / title
OnEnable / Start / Update / OnPointerEnter
```

Interop wrappers reveal class names, fields, properties, and method signatures. Their method bodies still live in `GameAssembly.dll`, so runtime logs and focused tests remain necessary.

Disfigure-specific findings:

- The game primarily uses legacy `UnityEngine.UI.Text`; scanning only TMP misses most UI.
- `TMP_Text` scanning already covers both `TextMeshProUGUI` and world-space `TextMeshPro` subclasses.
- No game localization manager, language enum, string table, or localization ScriptableObject was found.
- Text comes from serialized scene/prefab fields and ordinary game objects such as upgrades and tooltips.
- Some UI objects are created dynamically, so one startup scan is insufficient.

For another game, look for a built-in localization system first. Feeding translated tables into an existing system is usually safer than replacing rendered components.

## 3. Build discovery before targeted patches

The smallest useful first version is a controlled scanner:

1. Enumerate `Resources.FindObjectsOfTypeAll<TMP_Text>()`.
2. Enumerate `Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>()`.
3. Reject null, destroyed, empty, and whitespace-only objects safely.
4. Record unique source strings in a `HashSet<string>`.
5. Replace exact matches only when the component is not already translated.
6. Scan at startup, immediately on scene change, shortly after scene change, and on a slow interval.

`FindObjectsOfTypeAll` includes inactive objects, which lets menus be translated before they become visible. It is more expensive than active-only enumeration, so do not run it every frame.

IL2CPP Unity objects can be destroyed between enumeration and access. Wrap component reads and writes, and log each failing component identity only once.

Polling `SceneManager.GetActiveScene().name` from `Update` proved dependable here and avoided assuming normal Mono scene-event behavior. A delayed scene scan catches menus created after the scene becomes active.

## 4. Keep source identity stable

Translation dictionaries use the untouched English string as the key. Preserve line endings, punctuation, capitalization, placeholders, and rich-text tags.

The manager needs three separate concepts:

- Canonical English source strings
- Exact translated output strings
- Placeholder patterns such as `Level {0}`

Track translated outputs so the scanner does not export its own translation as a new source string. For live language switching, build a reverse index from every installed language back to the canonical English key; remembering only the previous language fails after multiple switches.

For dynamically assembled tooltips, try a full exact match first, then translate each non-empty line while preserving blank lines and tags. Do not strip formatting to make matching easier—the rendered string must remain valid.

Avoid cataloging values that are only counters, timers, coordinates, or percentages. Use placeholders when a meaningful label contains a changing value.

## 5. Write JSON defensively

Missing or malformed translation files should disable that file, not crash the game. Create missing directories and empty defaults.

When updating detected strings:

1. Load and merge the existing dictionary.
2. Serialize to a temporary file in the same directory.
3. Replace or move it over the destination only after serialization succeeds.

This prevents a partial write from destroying the catalog if the game exits during saving. Never erase existing detected keys merely because they were not observed in the current session.

## 6. Use Harmony only where timing requires it

Periodic scanning works for discovery but visibly lags when the game writes text immediately after a click or hover. Fix that at the game method that owns the UI.

Useful patch patterns:

- Prefix: translate string arguments before the game builds the visible UI.
- Postfix: translate the final component fields after the game finishes assigning them.
- Getter postfix: translate returned display metadata when callers consistently use the getter.
- Targeted `Update`/`LateUpdate` postfix: last resort when that specific game component rewrites its text continuously.

Patching generic Unity text setters did not intercept every native IL2CPP assignment in Disfigure. Patching `weaponupgradetooltip.setText`, `UpgradeDescriptionTooltip.Show`, pointer-enter handlers, settings methods, and the end-screen builder did.

Do not mutate serialized source fields unless they are proven to be display-only. An early attempt translated more than a thousand upgrade backing fields and crashed because some names were also game identifiers. Translate method arguments, return values, or visible component fields instead.

Keep scanning as a fallback even after adding hooks. It still discovers unknown text and covers screens without a targeted method.

## 7. Treat settings and popups as separate timing problems

Some settings store reusable `on` and `off` strings, then copy them to a label after every click. Translating only the visible label causes English to flash or remain until the next scan. Translate the backing display strings before the click handler runs, then translate the final label in a postfix.

Hover popups often combine a title, description, rich-text effects, and generated stat lines. Patch the popup builder, not only the object being hovered. Log translation hits and misses by context while diagnosing, but deduplicate them to avoid flooding BepInEx logs.

## 8. Fonts and layout are part of localization

A correct translation can still render as boxes, question marks, or overlapping rows.

- Check every translated character against the active font.
- Ignore whitespace and remove rich-text tags only for the temporary glyph check.
- Log the font name, missing code point, and affected text once.
- Use a system-font fallback only when the current font lacks required glyphs.
- Expect CJK, Cyrillic, and Arabic to need different coverage and layout testing.

Disfigure's legacy text can use a Windows dynamic-font fallback. TMP generally needs an appropriate TMP font asset or fallback asset; do not assume a normal `Font` can safely replace it.

Use best-fit/auto-sizing only for short label blocks that exceed their rectangle. Applying it to prose descriptions makes text inconsistent and can hide layout problems. Some labels, such as the gameplay level, need horizontal overflow so a longer translated word does not push the number onto a clipped second line.

## 9. Protect text encoding

Keep catalogs and translation scripts in UTF-8. Cyrillic was once converted to literal question marks because translated text was embedded in a PowerShell-piped script before JSON serialization.

Prefer running a saved UTF-8 script file over passing non-ASCII source code through multiple shells. Before accepting a generated catalog, audit for:

- Repeated question marks
- Replacement characters
- Broken or unbalanced rich-text tags
- Missing or reordered placeholders
- Empty translations
- Unexpected unchanged English descriptions

## 10. Test the real runtime path

A successful build proves only API compatibility. It does not prove that hooks fire, fonts contain glyphs, or translated labels fit.

Minimum test pass:

1. Start from a clean BepInEx install and confirm the plugin loads.
2. Open the main menu and every settings page.
3. Enter gameplay and open pause/settings UI.
4. Hover every weapon, perk, mutation, upgrade path, and tooltip type.
5. Trigger level-up choices, unlocks, boss UI, death, victory, and end-score screens.
6. Switch languages repeatedly, including switching back to English.
7. Check immediate click/hover updates without waiting for the periodic scan.
8. Check non-Latin glyphs, wrapping, alignment, and overlap.
9. Inspect the detector and popup-audit logs for misses.
10. Verify malformed or missing JSON does not stop the plugin.

Windows locks a loaded plugin DLL. Close the game before installing a rebuilt DLL. Keep compile success, static catalog validation, and in-game verification clearly separated in notes and releases.

## 11. Package without modifying the game

Provide two packages when practical:

- Mod only: plugin DLL and maintained translation catalogs under the correct `BepInEx` paths.
- All in one: the mod plus an unmodified official BepInEx distribution for the correct platform and Unity backend.

Do not package generated interop assemblies, caches, logs, user config, detected strings, save data, decompiled game code, or original game assets. Include the BepInEx license and notice when redistributing it.

Use deterministic folder paths, test extraction into a clean game directory, publish SHA-256 checksums, and preserve existing user configuration during updates.

## 12. Reusable workflow

For a similar game:

1. Install the correct BepInEx 6 build and prove a minimal plugin loads.
2. Inspect existing mods, logs, interop assemblies, and decompiled signatures.
3. Look for a built-in localization system.
4. Identify every text-rendering component actually used by the game.
5. Build exact discovery and replacement with safe JSON persistence.
6. Add placeholder patterns for meaningful changing values.
7. Runtime-test menus and dynamic UI to find delayed assignments.
8. Add the narrowest game-method patches needed for immediate translation.
9. Add glyph checks and targeted layout handling.
10. Audit catalogs, test clean installation, and package only redistributable files.

Start with scanning and exact matches. Add patches only when a recorded runtime failure proves they are necessary.
