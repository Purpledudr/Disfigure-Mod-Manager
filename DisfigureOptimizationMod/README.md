# Disfigure Optimization Mod

A lightweight BepInEx plugin that reduces rendering work without changing Disfigure's gameplay. During normal play the game should look effectively unchanged: enemies, bullets, animations, colors, attacks, and effects remain recognizable.

## What it disables

- URP post-processing and scene volumes.
- Shadow casting, shadow receiving, and motion vectors on enemy and projectile renderers.
- Projectile trail renderers.

It does **not** hide enemies, freeze animations, change hitboxes, alter spawns, or modify weapons, damage, movement, and timing. Experimental animation and hidden-renderer options remain off by default.

## Expected performance

Our controlled tests measured roughly **20–35% higher FPS**, with about **30%** being a reasonable expectation. The exact gain depends on hardware, resolution, enemy count, projectile count, and whether the game is limited by the CPU or GPU.

## Install

Install **Disfigure Optimization Mod** from [Disfigure Mod Manager](https://github.com/Purpledudr/Disfigure-Mod-Manager), or place `DisfigureOptimizationMod.dll` in `BepInEx/plugins` manually.

There is no menu or setup. The optimizations start automatically when the mod is installed; disable or uninstall it through Disfigure Mod Manager.

## Build

```powershell
dotnet build .\DisfigureOptimizationMod.csproj -c Release
```

Override `DisfigureDir` when the game is installed somewhere else:

```powershell
dotnet build .\DisfigureOptimizationMod.csproj -c Release -p:DisfigureDir="C:\path\to\Disfigure"
```
