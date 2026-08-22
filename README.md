# Disfigure Mod Manager

A simple desktop manager for Disfigure BepInEx plugins. It detects the game folder, installs BepInEx when needed, and lets players install, update, enable, disable, and uninstall plugins from a public catalog without a GitHub account or token.

## Download

[Download Disfigure Mod Manager for Windows](https://github.com/Purpledudr/DisfigureModManager/releases/download/manager-v1.0.0/DisfigureModManager-win-x64.zip)

Extract the ZIP and run `DisfigureModManager.exe`. The manager automatically looks for Disfigure in Steam libraries; use **Browse** if the game is elsewhere.

Linux and Steam Deck players running Disfigure through Proton should select a Windows x64 IL2CPP BepInEx package in the manager. A native Linux BepInEx package is not compatible with the Windows game under Proton.

The manager prevents plugin changes while Disfigure is running. If the game is open when a change is requested, close it first; the restart reminder only appears when the game is actually running.

## Available plugins

- **Disfigure Translation Mod 0.7.9:** Adds nine community language translations and supports Disfigure 1.0.
- **Sandbox Mode 0.3.70:** Opens an in-run panel for upgrades, timer controls, and enemy spawning.

The manager reads [plugins.json](plugins.json), so new plugins and releases can be added without publishing a new manager build.

## How to use it

1. Open the manager and confirm the detected Disfigure folder.
2. Install a compatible BepInEx version if prompted.
3. Use **Available Plugins** to install plugins from the catalog.
4. Use **Installed Plugins** to update, enable, disable, or uninstall them.

## Translation Mod controls

- **F8:** Open the language menu
- **F5:** Reload translations
- **F4:** Force a text rescan

Supported languages are English, Español, Français, Русский, Deutsch, Português (Brasil), 简体中文, 日本語, and Polski. Translations are machine-assisted and may contain mistakes.

## Repository layout

- [`DisfigureModManager`](DisfigureModManager): Windows desktop manager source
- [`SandboxMode`](SandboxMode): Sandbox Mode plugin source
- Repository root: Translation Mod source and technical documentation
- [`plugins.json`](plugins.json): Public application and plugin catalog

Build instructions and implementation details for the Translation Mod are in [TECHNICAL.md](TECHNICAL.md).

