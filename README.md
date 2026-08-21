# DisfigureTranslationMod

A fan translation mod for the Windows version of Disfigure.

## Download

[Download the latest release](https://github.com/Purpledudr/DisfigureTranslationMod/releases/latest)

Choose the **all-in-one Windows x64 ZIP** unless you already have BepInEx 6 IL2CPP installed.

The repository also contains the [Disfigure Mod Manager](DisfigureModManager), which can install BepInEx, install this translation package, and manage future catalog plugins. Its Windows build is published automatically as a separate `manager-v*` release and is listed in [plugins.json](plugins.json) as an application, never as a BepInEx plugin.

## Install

1. Download the ZIP file
2. Open the Disfigure game directory containing `Disfigure.exe`.
3. Extract the ZIP directly into that directory.
4. Launch the game and press **F8** to choose a language.

The game directory usually looks like `C:\Users\{username}\SteamLibrary\steamapps\common\Disfigure` or `C:\Program Files (x86)\Steam\steamapps\common\Disfigure`.

The first launch may take longer while BepInEx creates its required files. Your selected language is remembered between launches.

## Supported languages

- English
- Español
- Français
- Русский
- Deutsch
- Português (Brasil)
- 简体中文
- 日本語
- Polski

Translations are machine-assisted and may still contain mistakes. Screenshots and corrections are welcome.

## Controls

- **F8:** Open the language menu
- **F5:** Reload translations
- **F4:** Force a text rescan

## More information

Build instructions, configuration, translation-file details, testing information, and implementation notes are in [TECHNICAL.md](TECHNICAL.md).
