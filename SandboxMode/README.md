# Disfigure Sandbox Mode

An in-run sandbox panel for Disfigure. Press **F5** during a run to open it; **F5** or **Escape** closes it. Press **F8** to export the currently loaded perk, mutation, and enemy artwork as PNG files in `DisfigureArtExport` beside the game executable.

Version 0.3.70 extracts the enemy number after the map name so every map sorts 0–11 correctly. Enemy previews use the game's initialized pool models, keep all death-effect, attack-manager, fractured-model, and outline trees inactive, apply the game's black enemy color to normal visible meshes, and advance Disfigure's mesh animators while the sandbox is paused. The centipede preview creates its native indexed body segments, keeps one front/head mesh, and hides the duplicate head mesh on the remaining body segments. Live animated enemies use the stable overlay camera with a fully opaque light 3D backplate behind every preview. The camera, backplates, and clones are destroyed as soon as the sandbox closes.

Controls:

- Enable or disable loaded upgrades, weapon perks, and mutations.
- Select every upgrade in a tree with one button.
- Deselect Tank to remove its two max hearts, Pact to remove one max heart, a Max Health -1 upgrade to regain one filled heart, or Last Stand to heal fully.
- Browse perks grouped by weapon in the beta catalog's canonical 1–9 order, and upgrades grouped by their native upgrade trees (six complete groups per page).
- See native artwork and descriptions by hovering over an upgrade, perk, or mutation.
- Set the run timer from 0:00 to 60:00.
- Spawn regular and elite enemies exposed by the current map.

The panel keeps the run paused while open. Changes use the game's own upgrade removal, upgrade application, timer, and enemy spawning methods. Test mode is enabled only during an active run and restored on return to the main menu. Run score and score-derived credits are continuously forced to zero so sandbox use cannot affect leaderboards.

Known limits:

- In-game weapon replacement is not included. The selected weapon initializes several weapon-specific objects when the run starts, and changing only `WeaponManager.weaponName` would leave a broken mixed weapon state.
- The mutation tab can only manipulate mutation components loaded by the current gameplay scene. All mutations are available for choosing before a run, but the game may still load only the five equipped mutations into a run.
- Boss spawning is excluded because bosses require arena and encounter state in addition to creating the enemy object.

Build with:

```powershell
dotnet build .\SandboxMode\SandboxMode.csproj -c Release
```

Copy `SandboxMode.dll` from `SandboxMode\bin\Release\net6.0` into `BepInEx\plugins`.

