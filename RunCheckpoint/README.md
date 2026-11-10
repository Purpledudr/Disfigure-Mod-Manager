# Run Save & Quit

A one-slot checkpoint mod for solo runs on the Disfigure multiplayer beta with BepInEx 6 Unity Mono.

1. Pause an active solo run and select **Save & Quit**. The game saves the run and returns to the main menu.
2. Exit and relaunch Disfigure, then select **Resume Run**.
3. Press a key or button when the game shows **Press any button to resume**. The checkpoint is consumed when gameplay resumes.

Save & Quit replaces the slot only after capture and the file write succeed. A failed resume keeps the checkpoint for another attempt. Saving is unavailable during upgrades and boss encounters. The mod is disabled in multiplayer lobbies and co-op runs. Solo leaderboard submissions are suppressed while the mod is installed. Checkpoints made in Sandbox test mode resume in test mode.

Checkpoint files are editable, so restored scores are untrusted for ranking. Normal runs still use the game's profile records and score-derived credits.

The checkpoint stores the loadout, upgrades and runtime buffs, player state, elapsed time, score, enemies, dropped XP, and supported pooled battlefield objects. Format version 5 is retained. The original 0.8.4 checkpoint format omitted valid dropped XP data, so those checkpoints resume without dropped XP.

Build from the workspace root with `dotnet build .\RunCheckpoint\RunCheckpoint.csproj -c Release`. If your game is installed elsewhere, pass `-p:DisfigureDir="<game folder>"`.

Copy `RunCheckpoint/bin/Release/netstandard2.1/RunCheckpoint.dll` to `Disfigure/BepInExMono/plugins/RunCheckpoint/`. The checkpoint is stored in `Disfigure/BepInExMono/config/DisfigureRunCheckpoint.json`.
