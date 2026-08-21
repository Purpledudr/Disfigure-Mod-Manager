# Disfigure Mod Manager

A small Windows desktop manager for Disfigure BepInEx plugin DLLs. It finds Steam libraries automatically, scans enabled and disabled plugin folders, blocks changes while the game is running, and installs updates from a public JSON catalog without GitHub authentication.

If BepInEx is missing, the manager shows an **Install BepInEx** button. It offers compatible Windows x64 IL2CPP versions, downloads the selected official package, validates it, and installs it into the game folder. Choose `6.0.0-be.785` for the mods in this workspace unless a plugin specifically requires an older release.

Disfigure only ships a Windows build. Linux and Steam Deck players running it through Proton should also choose the Windows x64 package—the native Linux BepInEx package is not compatible with a Windows game running under Proton.

## Run it

```powershell
dotnet run --project .\DisfigureModManager\DisfigureModManager.csproj
```

The first time it opens, confirm the detected Disfigure folder (or use **Browse**). The public catalog is already configured as:

```text
https://raw.githubusercontent.com/Purpledudr/DisfigureTranslationMod/main/plugins.json
```

The app remembers both values in `%LOCALAPPDATA%\DisfigureModManager\settings.json`.

## Publish a standalone Windows app

```powershell
dotnet publish .\DisfigureModManager\DisfigureModManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Before publishing, you can put the raw catalog URL in `appsettings.json` so users do not need to enter it themselves.

## Maintain the plugin catalog

`plugins.json` lives at the repository root. Plugin downloads can be a direct DLL or a ZIP containing files under `BepInEx/plugins` and `BepInEx/translations`. Set `available` to `false` for a coming-soon entry. Applications such as the manager belong in the separate top-level `applications` array and are never installed into BepInEx.

```json
{
  "plugins": [
    {
      "id": "run-history",
      "name": "Run History",
      "description": "Keeps a history of completed runs.",
      "version": "1.2.0",
      "downloadUrl": "https://github.com/OWNER/REPOSITORY/releases/download/v1.2.0/DisfigureRunHistory.dll",
      "dllFilename": "DisfigureRunHistory.dll",
      "packageType": "dll",
      "available": true
    }
  ]
}
```

Versions are compared numerically. The installed version is read from the DLL's product or assembly version, so set that version in each plugin project when releasing it.

## Quick check

```powershell
dotnet run --project .\DisfigureModManager\DisfigureModManager.csproj -- --self-test
```
