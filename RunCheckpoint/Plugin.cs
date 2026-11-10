using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RunCheckpoint;

[BepInPlugin("casto.disfigure.run-checkpoint", "Run Save & Quit", "0.9.1")]
public sealed class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        RunCheckpointController.Initialize(Logger, Path.Combine(Paths.ConfigPath, "DisfigureRunCheckpoint.json"));
        new Harmony("casto.disfigure.run-checkpoint").PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo("Run Checkpoint loaded for solo runs. Solo leaderboard score submission is disabled while installed.");
    }

    private void LateUpdate()
    {
        CheckpointUi.UpdateLobbyVisibility();
        if (RunCheckpointController.HasPendingRestore) RunCheckpointController.PollRestore();
        else if (RunCheckpointController.MultiplayerActive) RunCheckpointController.EndCheckpointTestMode();
    }
}

internal static class RunCheckpointController
{
    private const int SaveVersion = 5;
    private static readonly HashSet<string> SkippedGameManagerFields = new(StringComparer.Ordinal)
    {
        "enemiesOnScreen",
        "pauseEnemySpawn"
    };

    internal static ManualLogSource Logger { get; private set; }
    internal static string SavePath { get; private set; } = string.Empty;
    internal static string LastMessage { get; private set; } = string.Empty;
    internal static bool HasPendingRestore => pendingRestore != null || placementFramesRemaining > 0 || restoreCompleted || waitingForResumeInput;
    internal static bool MultiplayerActive => WavedashCoopSession.IsActive || WavedashCoopSession.Instance?.Busy == true || CoopAutoStart.IsLaunching || CoopLifecycle.InCoopRun;
    internal static bool ReturningFromCheckpoint { get; private set; }

    internal static bool HasCompatibleSave()
    {
        try
        {
            return IsCompatible(JsonConvert.DeserializeObject<RunSave>(File.ReadAllText(SavePath)));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCompatible(RunSave save) => save?.Version == SaveVersion
        && !string.IsNullOrWhiteSpace(save.WeaponId)
        && save.GameManagerState?.TryGetValue("timePassed", out var elapsed) == true
        && float.TryParse(elapsed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
        && seconds >= 0f && !float.IsInfinity(seconds);

    private static RunSave pendingRestore;
    private static string pendingCheckpointContents;
    private static float restoreNotBefore;
    private static bool launchPending;
    private static int launchStage;
    private static int launchReadyFrame;
    private static float launchStageDeadline;
    private static GameObject launchNextButton;
    private static readonly List<EnemyPlacement> pendingEnemyPlacements = new();
    private static Dictionary<int, int> capturedEnemyIndices = new();
    private static Transform pendingPlayerTransform;
    private static Rigidbody pendingPlayerBody;
    private static Vector3 pendingPlayerPosition;
    private static Quaternion pendingPlayerRotation;
    private static int placementFramesRemaining;
    private static RunSave pendingPlacementSave;
    private static bool restoreCompleted;
    private static bool waitingForResumeInput;
    private static float resumeInputNotBefore;
    private static float resumeTimeScale = 1f;
    private static bool checkpointEnabledTestMode;

    internal static void Initialize(ManualLogSource logger, string savePath)
    {
        Logger = logger;
        SavePath = savePath;
        var probe = new RunSave
        {
            Map = 2,
            WeaponId = "pistol",
            PlayerPositionX = 4f,
            PlayerStatsState = new() { ["isDoomRound"] = "True" },
            WeaponManagerState = new() { ["marksmanMultiplier"] = "1.12", ["currentSnareCount"] = "2" },
            ScoreTextState = new() { ["score"] = "9001", ["minibossKills"] = "2" },
            MusicManagerState = new() { ["currentSongIndex"] = "2" },
            GeneralAudioState = new() { ["fireTimer"] = "0.25" },
            SideUpgradeIconNames = new() { ["TEST PERK"] = "perk-ii" },
            MutationLoadout = new() { 0, 1, 2, 3, 4 },
            RunSessionStatsValid = true,
            RunSessionStats = new() { HitsTaken = 1, TotalDamageDealt = 4583, BulletsFired = 218, HighestDamageNumber = 37 },
            RandomState = new() { S0 = 1, S1 = 2, S2 = 3, S3 = 4 },
            EnemyRelationships = new() { ExecutionStrikeTargets = new() { 0 } },
            ExpOrbDataValid = true,
            ExpOrbs = new() { new ExpOrbSave { Position = new VectorSave { X = 3f }, Level = 2, Value = 5, Age = 1.5f } },
            AudioSources = new() { new AudioSourceSave { Owner = "music", ClipName = "track", TimeSamples = 123, WasPlaying = true } },
            PlayerAnimators = new() { new AnimatorSave { Owner = "player", Layers = new() { new AnimatorLayerSave { StateHash = 42, NormalizedTime = 0.5f } } } },
            Snares = new() { new SnareSave { Active = true, EnableTimer = 0.4f } },
            WorldObjects = new()
            {
                new WorldObjectSave
                {
                    Kind = "landmine",
                    PositionX = 8f,
                    Components = new() { new ComponentSave { TypeName = "Projectile", State = new() { ["mineDuration"] = "4.5", ["damage"] = "17", ["isSaw"] = "True" } } },
                    PierceTargets = new() { new EnemyObjectReferenceSave { EnemyIndex = 0, Path = "1/0" } }
                }
            },
            Enemies = new()
            {
                new EnemySave
                {
                    PoolId = "enemy0",
                    PositionX = 12.5f,
                    CurrentHealth = 7f,
                    ShockVisualWorldScale = new VectorSave { X = 1f, Y = 2f, Z = 3f },
                    State = new() { ["isFire"] = "True", ["comboStrikeCounter"] = "3" },
                    Vectors = new() { ["freezePos"] = new VectorSave { X = 1f, Y = 2f, Z = 3f } },
                    BulletNumbers = new() { 2, 4 }
                }
            }
        };
        var roundTrip = JsonConvert.DeserializeObject<RunSave>(JsonConvert.SerializeObject(probe));
        if (roundTrip?.Map != probe.Map || roundTrip.PlayerPositionX != 4f || roundTrip.Enemies.Count != 1
            || Math.Abs(DivideScale(1f, 20f) - 0.05f) > 0.0001f
            || roundTrip.PlayerStatsState["isDoomRound"] != "True"
             || roundTrip.WeaponManagerState["currentSnareCount"] != "2"
             || roundTrip.ScoreTextState["score"] != "9001"
             || roundTrip.MusicManagerState["currentSongIndex"] != "2"
             || roundTrip.GeneralAudioState["fireTimer"] != "0.25"
             || roundTrip.SideUpgradeIconNames["TEST PERK"] != "perk-ii"
             || roundTrip.MutationLoadout.Count != 5 || roundTrip.MutationLoadout[4] != 4
             || !roundTrip.RunSessionStatsValid || roundTrip.RunSessionStats.TotalDamageDealt != 4583
             || roundTrip.RandomState.S3 != 4
             || roundTrip.EnemyRelationships.ExecutionStrikeTargets[0] != 0
             || !roundTrip.ExpOrbDataValid
             || roundTrip.ExpOrbs[0].Value != 5
             || roundTrip.AudioSources[0].TimeSamples != 123
             || roundTrip.PlayerAnimators[0].Layers[0].StateHash != 42
            || roundTrip.Snares.Count != 1 || roundTrip.Snares[0].EnableTimer != 0.4f
            || roundTrip.Enemies[0].PositionX != probe.Enemies[0].PositionX
             || roundTrip.Enemies[0].ShockVisualWorldScale.Z != 3f
             || roundTrip.Enemies[0].State["comboStrikeCounter"] != "3"
             || roundTrip.Enemies[0].Vectors["freezePos"].Z != 3f
             || roundTrip.Enemies[0].BulletNumbers.Count != 2
             || roundTrip.WorldObjects.Count != 1
             || roundTrip.WorldObjects[0].Components[0].State["mineDuration"] != "4.5"
             || roundTrip.WorldObjects[0].Components[0].State["damage"] != "17"
             || roundTrip.WorldObjects[0].PierceTargets[0].Path != "1/0")
        {
            Logger?.LogError("Checkpoint JSON self-check failed.");
        }
    }

    internal static void PollRestore()
    {
        try
        {
            if (MultiplayerActive)
            {
                FailLaunch("A multiplayer lobby became active during solo resume.");
                return;
            }
            PollRestoreStep();
        }
        catch (Exception ex)
        {
            FailLaunch($"Checkpoint restore failed: {ex}", returnToMenu: GameManager.instance != null);
        }
    }

    private static void PollRestoreStep()
    {
        if (pendingRestore == null)
        {
            ApplyPendingPlacements();
            if (restoreCompleted && placementFramesRemaining <= 0)
            {
                restoreCompleted = false;
                waitingForResumeInput = true;
                resumeInputNotBefore = Time.unscaledTime + 0.35f;
                CheckpointUi.ShowReadyPrompt();
                Logger?.LogInfo("Checkpoint loading complete; waiting for player input.");
            }
            else if (waitingForResumeInput)
            {
                CheckpointUi.MaintainReadyPrompt();
                if (Time.unscaledTime >= resumeInputNotBefore && AnyResumeInputPressed())
                {
                    CheckpointUi.HideLoadingOverlay();
                    var gm = GameManager.instance;
                    if (gm == null) throw new InvalidOperationException("Gameplay exited before resume was confirmed.");
                    gm.pauseEnemySpawn = false;
                    Time.timeScale = resumeTimeScale;
                    waitingForResumeInput = false;
                    ConsumeCheckpoint();
                    Show($"Checkpoint resumed at {FormatTime(gm.timePassed)}.");
                    Logger?.LogInfo("Checkpoint released from the ready pause.");
                }
            }
            return;
        }

        if (launchPending)
        {
            var menu = FindActiveStartMenu();
            if (menu == null)
            {
                if (Time.unscaledTime >= launchStageDeadline)
                {
                    FailLaunch("Resume could not find the start menu.");
                }
                return;
            }

            try
            {
                if (Time.frameCount < launchReadyFrame) return;
                if (Time.unscaledTime >= launchStageDeadline)
                {
                    FailLaunch($"Resume timed out during menu stage {launchStage}.");
                    return;
                }

                switch (launchStage)
                {
                    case 1:
                        var weapon = FindSavedWeapon(pendingRestore.WeaponId);
                        if (weapon == null) return;
                        ApplyMutationLoadout(pendingRestore.MutationLoadout);
                        weapon.EnsureRefs();
                        weapon.selectWeapon();
                        launchNextButton = MonoFields.StartButton(weapon);
                        Logger?.LogInfo($"Selected saved weapon: {weapon.weaponname}.");
                        AdvanceLaunchStage(2);
                        break;
                    case 2:
                    case 3:
                        if (PressButton(launchNextButton, "NEXT")) AdvanceLaunchStage(launchStage + 1);
                        break;
                    case 4:
                        var mutations = FindActiveMutationMenu();
                        if (mutations == null) return;
                        ApplyMutationLoadout(pendingRestore.MutationLoadout);
                        mutations.RefreshAllMutationBackgroundSprites();
                        if (PressButton(launchNextButton, "NEXT")) AdvanceLaunchStage(5);
                        break;
                    case 5:
                        menu.ChooseMap(pendingRestore.Map);
                        Logger?.LogInfo($"Selected saved map: {pendingRestore.Map}.");
                        AdvanceLaunchStage(6);
                        break;
                    case 6:
                        if (!PressDifficultyButton(pendingRestore.Difficulty)) return;
                        launchPending = false;
                        launchStage = 0;
                        launchReadyFrame = 0;
                        launchStageDeadline = Time.unscaledTime + 90f;
                        launchNextButton = null;
                        restoreNotBefore = 0f;
                        Logger?.LogInfo($"Checkpoint launch started: map={pendingRestore.Map}, difficulty={pendingRestore.Difficulty}, weapon={pendingRestore.WeaponId}.");
                        break;
                }
            }
            catch (Exception ex)
            {
                FailLaunch($"Checkpoint launch failed: {ex}");
            }

            return;
        }

        var refs = Safe(() => GameRefs.Instance, null);
        if (Time.unscaledTime >= launchStageDeadline)
        {
            FailLaunch("The saved run's gameplay scene did not become ready.", returnToMenu: GameManager.instance != null);
            return;
        }
        if (GameManager.instance == null || refs?.playerStats == null || refs.weaponManager == null || SideUpgradesDisplayHandling.instance == null)
        {
            restoreNotBefore = 0f;
            return;
        }

        if (restoreNotBefore == 0f)
        {
            GameManager.instance.pauseEnemySpawn = true;
            restoreNotBefore = Time.unscaledTime + 1f;
        }
        else if (Time.unscaledTime >= restoreNotBefore)
        {
            Restore();
            pendingRestore = null;
            restoreNotBefore = 0f;
        }
    }

    internal static void Save()
    {
        if (MultiplayerActive || HasPendingRestore || SceneManagement.IsSceneTransitionInFlight) return;
        var refs = Safe(() => GameRefs.Instance, null);
        var gm = Safe(() => GameManager.instance, null);
        var wm = refs?.weaponManager;
        var ps = refs?.playerStats;
        var pm = refs?.playerMove;
        var score = refs?.scoreText;
        var scene = Safe(() => UnityEngine.Object.FindObjectOfType<SceneManagement>(), null);
        var player = (wm == null ? default : MonoFields.Player(wm))?.transform;

        if (gm == null || wm == null || ps == null || player == null || scene?.pausemenu?.activeInHierarchy != true)
        {
            Show("Pause an active run before saving.");
            return;
        }

        if (scene.upgradeScreen?.activeInHierarchy == true || scene.endScreen?.activeInHierarchy == true || gm.pauseEnemySpawn)
        {
            Show("Checkpoint unavailable during upgrades or a boss encounter.");
            return;
        }

        try
        {
            var captureFailures = new List<string>();
            T Capture<T>(string name, Func<T> capture, T fallback)
            {
                try
                {
                    return capture();
                }
                catch (Exception ex)
                {
                    captureFailures.Add(name);
                    Logger?.LogError($"Checkpoint capture chunk '{name}' failed: {ex}");
                    return fallback;
                }
            }

            var expManager = ExpManager.instance;
            Capture("in-flight EXP", () => { if (expManager != null) MonoFields.FlushPendingCollectionGrants(expManager); return true; }, false);
            var enemies = Capture("enemies", () => CaptureEnemies(gm), new List<EnemySave>());
            var enemyRelationships = Capture("enemy relationships", () => CaptureEnemyRelationships(ps, wm), new EnemyRelationshipsSave());
            var worldObjects = Capture("world objects", CaptureWorldObjects, new List<WorldObjectSave>());
            var audioSources = Capture("audio", () => CaptureRunAudio(refs), new List<AudioSourceSave>());
            var expOrbs = Capture("EXP orbs", () => CaptureExpOrbs(expManager), new List<ExpOrbSave>());
            var runSessionStats = Capture("run statistics", CaptureRunSessionStats, new RunSessionStatsSave());
            var randomState = UnityEngine.Random.state;
            var save = new RunSave
            {
                Version = SaveVersion,
                SavedAtUtc = DateTime.UtcNow,
                Map = gm.map,
                Difficulty = gm.difficulty,
                TestMode = cheatmanager.Instance.isTestMode,
                WeaponId = string.IsNullOrWhiteSpace(wm.weaponName) ? "pistol" : wm.weaponName,
                MutationLoadout = Capture("mutation loadout", CaptureMutationLoadout, new List<int>()),
                PlayerPositionX = player.position.x,
                PlayerPositionY = player.position.y,
                PlayerPositionZ = player.position.z,
                PlayerRotationX = player.rotation.x,
                PlayerRotationY = player.rotation.y,
                PlayerRotationZ = player.rotation.z,
                PlayerRotationW = player.rotation.w,
                Health = wm.getCurrentHealth(),
                Level = ps.level,
                MaxHealth = ps.maxHealth,
                Experience = wm.exp,
                LevelUpExperience = wm.levelupexp,
                SkillPoints = wm.getSkillPoints(),
                Kills = score == null ? 0 : MonoFields.Kills(score),
                CurrentMutationCount = ps.currentMutationCount,
                MutationOffersSkipped = ps.mutationOffersSkippedThisRun,
                GameManagerState = Capture("game manager", () => CaptureGameManagerState(gm), new Dictionary<string, string>()),
                ScoreTextState = score == null ? new() : Capture("score", () => CaptureRuntimeState(score, typeof(ScoreText)), new Dictionary<string, string>()),
                ScoreTextVectors = score == null ? new() : Capture("score vectors", () => CaptureRuntimeVectors(score, typeof(ScoreText)), new Dictionary<string, VectorSave>()),
                ChosenUpgrades = Copy(ps.chosenUpgrades),
                WeaponPerks = Copy(ps.unlockedWeaponPerksList),
                SideUpgradeIconNames = Capture("side upgrade icons", CaptureSideUpgradeIcons, new Dictionary<string, string>()),
                PlayerStatsState = Capture("player stats", () => CaptureRuntimeState(ps, typeof(PlayerStats)), new Dictionary<string, string>()),
                PlayerStatsVectors = Capture("player vectors", () => CaptureRuntimeVectors(ps, typeof(PlayerStats)), new Dictionary<string, VectorSave>()),
                WeaponManagerState = Capture("weapon manager", () => CaptureRuntimeState(wm, typeof(WeaponManager)), new Dictionary<string, string>()),
                WeaponManagerVectors = Capture("weapon vectors", () => CaptureRuntimeVectors(wm, typeof(WeaponManager)), new Dictionary<string, VectorSave>()),
                PlayerMoveState = pm == null ? new() : Capture("movement", () => CaptureRuntimeState(pm, typeof(PlayerMove)), new Dictionary<string, string>()),
                PlayerMoveVectors = pm == null ? new() : Capture("movement vectors", () => CaptureRuntimeVectors(pm, typeof(PlayerMove)), new Dictionary<string, VectorSave>()),
                ExpManagerState = expManager == null ? new() : Capture("EXP manager", () => CaptureRuntimeState(expManager, typeof(ExpManager)), new Dictionary<string, string>()),
                ExpManagerVectors = expManager == null ? new() : Capture("EXP vectors", () => CaptureRuntimeVectors(expManager, typeof(ExpManager)), new Dictionary<string, VectorSave>()),
                ExpOrbDataValid = !captureFailures.Contains("EXP orbs"),
                ExpOrbs = expOrbs,
                MusicManagerState = refs.musicManager == null ? new() : Capture("music", () => CaptureRuntimeState(refs.musicManager, typeof(MusicManager)), new Dictionary<string, string>()),
                MusicManagerVectors = refs.musicManager == null ? new() : Capture("music vectors", () => CaptureRuntimeVectors(refs.musicManager, typeof(MusicManager)), new Dictionary<string, VectorSave>()),
                GeneralAudioState = refs.generalAudioSource == null ? new() : Capture("audio manager", () => CaptureRuntimeState(refs.generalAudioSource, typeof(GeneralAudioSource)), new Dictionary<string, string>()),
                GeneralAudioVectors = refs.generalAudioSource == null ? new() : Capture("audio vectors", () => CaptureRuntimeVectors(refs.generalAudioSource, typeof(GeneralAudioSource)), new Dictionary<string, VectorSave>()),
                RunSessionStatsValid = !captureFailures.Contains("run statistics"),
                RunSessionStats = runSessionStats,
                RandomState = JsonConvert.DeserializeObject<RandomStateSave>(JsonUtility.ToJson(randomState)),
                EnemyRelationships = enemyRelationships,
                AudioSources = audioSources,
                PlayerAnimators = Capture("animations", () => CaptureAnimators(player, "player"), new List<AnimatorSave>()),
                Snares = Capture("snares", () => CaptureSnares(wm), new List<SnareSave>()),
                FirstSnareIndex = FindSnareIndex(wm.snareScripts, MonoFields.FirstSnare(wm)),
                Enemies = enemies,
                WorldObjects = worldObjects
            };

            if (captureFailures.Count > 0)
                throw new InvalidOperationException($"Checkpoint capture failed: {string.Join(", ", captureFailures)}.");
            if (!IsCompatible(save)) throw new InvalidDataException("Captured checkpoint is incomplete.");

            WriteCheckpoint(JsonConvert.SerializeObject(save, Formatting.Indented));
            Show($"Checkpoint saved at {FormatTime(gm.timePassed)}.");
            Logger?.LogInfo($"Checkpoint saved: map={save.Map}, difficulty={save.Difficulty}, time={gm.timePassed:0.0}s, upgrades={save.ChosenUpgrades.Count}, enemies={save.Enemies.Count}, worldObjects={save.WorldObjects.Count}, expOrbs={save.ExpOrbs.Count}.");
        }
        catch (Exception ex)
        {
            Show("Checkpoint save failed; see LogOutput.log.");
            Logger?.LogError($"Checkpoint save failed: {ex}");
            return;
        }

        ReturnToMenu(scene);
    }

    private static void WriteCheckpoint(string contents)
    {
        var temp = SavePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(SavePath)) File.Replace(temp, SavePath, null);
            else File.Move(temp, SavePath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void ConsumeCheckpoint()
    {
        try
        {
            if (File.ReadAllText(SavePath) != pendingCheckpointContents)
                throw new IOException("The checkpoint changed while resuming.");
            File.Delete(SavePath);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"Run resumed, but its checkpoint could not be consumed: {ex.Message}");
        }
        finally
        {
            pendingCheckpointContents = null;
        }
    }

    internal static bool BeginResume()
    {
        if (MultiplayerActive || HasPendingRestore) return false;
        try
        {
            var contents = File.ReadAllText(SavePath);
            var save = JsonConvert.DeserializeObject<RunSave>(contents);
            if (!IsCompatible(save))
            {
                Show("No compatible checkpoint found.");
                return false;
            }

            pendingRestore = save;
            pendingCheckpointContents = contents;
            if (save.TestMode && !cheatmanager.Instance.isTestMode)
            {
                cheatmanager.Instance.isTestMode = true;
                checkpointEnabledTestMode = true;
            }
            launchPending = true;
            launchStage = 1;
            launchReadyFrame = Time.frameCount + 1;
            launchStageDeadline = Time.unscaledTime + 12f;
            launchNextButton = null;
            restoreNotBefore = 0f;
            restoreCompleted = false;
            waitingForResumeInput = false;
            try
            {
                CheckpointUi.ShowLoadingOverlay();
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"Loading cover unavailable; resume will continue: {ex.Message}");
            }
            Show("Loading checkpoint...");
            return true;
        }
        catch (Exception ex)
        {
            FailLaunch($"Checkpoint load failed: {ex}");
            return false;
        }
    }

    private static void Restore()
    {
        var save = pendingRestore!;
        var refs = GameRefs.Instance;
        var gm = GameManager.instance;
        var wm = refs.weaponManager;
        var ps = refs.playerStats;
        if (gm.map != save.Map || gm.difficulty != save.Difficulty || NormalizeMenuText(wm.weaponName) != NormalizeMenuText(save.WeaponId))
            throw new InvalidOperationException("The loaded map, difficulty, or weapon does not match the checkpoint.");

        resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        var failedChunks = new List<string>();
        var restoredUpgrades = 0;
        var restoredEnemies = new List<Enemy>();
        var restoredWorldObjects = 0;

        void Chunk(string name, Action restore)
        {
            try
            {
                restore();
            }
            catch (Exception ex)
            {
                failedChunks.Add(name);
                Logger?.LogError($"Checkpoint chunk '{name}' failed: {ex}");
            }
        }

        Time.timeScale = 0f;

        {
            Chunk("upgrades", () => restoredUpgrades = ReplayUpgrades(save));
            Chunk("game manager", () => RestoreGameManagerState(gm, save.GameManagerState));
            Chunk("player", () =>
            {
                ps.level = save.Level;
                ps.maxHealth = save.MaxHealth;
                ps.currentMutationCount = save.CurrentMutationCount;
                ps.mutationOffersSkippedThisRun = save.MutationOffersSkipped;
                Replace(ps.chosenUpgrades, save.ChosenUpgrades);
                Replace(ps.unlockedWeaponPerksList, save.WeaponPerks);
                wm.exp = save.Experience;
                wm.levelupexp = save.LevelUpExperience;
                MonoFields.RemainingSkillPoints(wm) = save.SkillPoints;
                wm.setCurrentHealth(Math.Clamp(save.Health, 1, Math.Max(1, save.MaxHealth)));
                RestoreRuntimeState(ps, typeof(PlayerStats), save.PlayerStatsState);
                RestoreRuntimeVectors(ps, typeof(PlayerStats), save.PlayerStatsVectors);
                RestoreRuntimeState(wm, typeof(WeaponManager), save.WeaponManagerState);
                RestoreRuntimeVectors(wm, typeof(WeaponManager), save.WeaponManagerVectors);
                if (refs.playerMove != null)
                {
                    RestoreRuntimeState(refs.playerMove, typeof(PlayerMove), save.PlayerMoveState);
                    RestoreRuntimeVectors(refs.playerMove, typeof(PlayerMove), save.PlayerMoveVectors);
                }
            });
            Chunk("score", () =>
            {
                if (refs.scoreText == null) return;
                if (save.ScoreTextState.Count > 0)
                {
                    RestoreRuntimeState(refs.scoreText, typeof(ScoreText), save.ScoreTextState);
                    RestoreRuntimeVectors(refs.scoreText, typeof(ScoreText), save.ScoreTextVectors);
                    MonoFields.SnapDisplayToScore(refs.scoreText);
                }
                else
                {
                    MonoFields.Kills(refs.scoreText) = save.Kills;
                }
            });
            if (save.RunSessionStatsValid)
                Chunk("run statistics", () => RestoreRunSessionStats(save.RunSessionStats));
            Chunk("player placement", () => PreparePlayerPlacement(wm, save));
            Chunk("enemies", () => restoredEnemies = RestoreEnemies(gm, save.Enemies));
            Chunk("enemy relationships", () => RestoreEnemyRelationships(ps, wm, save.EnemyRelationships, restoredEnemies));
            Chunk("world objects", () => restoredWorldObjects = RestoreWorldObjects(gm, wm, save.WorldObjects, restoredEnemies));
            Chunk("post-spawn player state", () =>
            {
                RestoreRuntimeState(wm, typeof(WeaponManager), save.WeaponManagerState);
                RestoreRuntimeVectors(wm, typeof(WeaponManager), save.WeaponManagerVectors);
            });
            Chunk("EXP", () =>
            {
                if (ExpManager.instance == null) return;
                RestoreRuntimeState(ExpManager.instance, typeof(ExpManager), save.ExpManagerState);
                RestoreRuntimeVectors(ExpManager.instance, typeof(ExpManager), save.ExpManagerVectors);
                if (save.ExpOrbDataValid) RestoreExpOrbs(ExpManager.instance, save.ExpOrbs);
                else Logger?.LogWarning("Dropped EXP was recorded by 0.8.4 with an invalid array stride and was skipped.");
            });
            Chunk("music", () =>
            {
                if (refs.musicManager == null) return;
                RestoreRuntimeState(refs.musicManager, typeof(MusicManager), save.MusicManagerState);
                RestoreRuntimeVectors(refs.musicManager, typeof(MusicManager), save.MusicManagerVectors);
            });
            Chunk("audio manager", () =>
            {
                if (refs.generalAudioSource == null) return;
                RestoreRuntimeState(refs.generalAudioSource, typeof(GeneralAudioSource), save.GeneralAudioState);
                RestoreRuntimeVectors(refs.generalAudioSource, typeof(GeneralAudioSource), save.GeneralAudioVectors);
            });
            Chunk("audio sources", () => RestoreRunAudio(refs, save.AudioSources));
            Chunk("animations", () => RestoreAnimators(MonoFields.Player(wm).transform, save.PlayerAnimators));
            Chunk("random state", () => RestoreRandomState(save.RandomState));
            Logger?.LogInfo($"Checkpoint restore chunks applied: time={gm.timePassed:0.0}s, upgrades={restoredUpgrades}, enemies={restoredEnemies.Count}/{save.Enemies.Count}, worldObjects={restoredWorldObjects}/{save.WorldObjects.Count}, expOrbs={(save.ExpOrbDataValid ? save.ExpOrbs.Count : 0)}, failedChunks={failedChunks.Count}.");
            if (failedChunks.Count > 0)
                throw new InvalidOperationException($"Checkpoint restore failed: {string.Join(", ", failedChunks)}.");
        }

        pendingPlacementSave = save;
        gm.pauseEnemySpawn = true;
        Time.timeScale = 0f;
        restoreCompleted = true;
    }

    private static bool AnyResumeInputPressed()
    {
        if (Keyboard.current?.anyKey?.wasPressedThisFrame == true) return true;
        var mouse = Mouse.current;
        if (Pressed(mouse?.leftButton) || Pressed(mouse?.rightButton) || Pressed(mouse?.middleButton)
            || Pressed(mouse?.backButton) || Pressed(mouse?.forwardButton)) return true;
        var gamepad = Gamepad.current;
        return Pressed(gamepad?.buttonSouth) || Pressed(gamepad?.buttonEast) || Pressed(gamepad?.buttonWest)
            || Pressed(gamepad?.buttonNorth) || Pressed(gamepad?.leftShoulder) || Pressed(gamepad?.rightShoulder)
            || Pressed(gamepad?.leftTrigger) || Pressed(gamepad?.rightTrigger) || Pressed(gamepad?.leftStickButton)
            || Pressed(gamepad?.rightStickButton) || Pressed(gamepad?.startButton) || Pressed(gamepad?.selectButton)
            || Pressed(gamepad?.dpad?.up) || Pressed(gamepad?.dpad?.down)
            || Pressed(gamepad?.dpad?.left) || Pressed(gamepad?.dpad?.right);
    }

    private static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl button) => button?.wasPressedThisFrame == true;

    private static int ReplayUpgrades(RunSave save)
    {
        var catalog = new Dictionary<string, Upgrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var upgrade in Resources.FindObjectsOfTypeAll<Upgrade>())
        {
            var name = Safe(() => upgrade?.getName(), null);
            if (!string.IsNullOrWhiteSpace(name) && !catalog.ContainsKey(name))
            {
                catalog.Add(name, upgrade);
            }
        }

        var ps = GameRefs.Instance.playerStats;
        var display = SideUpgradesDisplayHandling.instance;
        var mutations = new Dictionary<string, mutationUpgrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var mutation in Resources.FindObjectsOfTypeAll<mutationUpgrade>())
        {
            var name = Safe(() => mutation?.getName(), null);
            if (!string.IsNullOrWhiteSpace(name) && !mutations.ContainsKey(name))
            {
                mutations.Add(name, mutation);
            }
        }

        var perks = new Dictionary<string, weaponupgrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var perk in Resources.FindObjectsOfTypeAll<weaponupgrade>())
        {
            var name = Safe(() => perk?.getName(), null);
            if (!string.IsNullOrWhiteSpace(name) && !perks.ContainsKey(name))
            {
                perks.Add(name, perk);
            }
        }

        var restored = 0;
        var perkNames = new HashSet<string>(save.WeaponPerks, StringComparer.OrdinalIgnoreCase);
        foreach (var name in save.ChosenUpgrades)
        {
            if (mutations.TryGetValue(name, out var mutation))
            {
                MonoFields.PS(mutation) = ps;
                mutation.UpgradeStat(true);
                AddSideUpgrade(display, mutation, false, save.SideUpgradeIconNames);
                restored++;
            }
            else if (perkNames.Contains(name))
            {
                // Weapon perks are replayed below from their authoritative saved list.
            }
            else if (catalog.TryGetValue(name, out var upgrade))
            {
                MonoFields.PS(upgrade) = ps;
                ps.unlockStartUpgrade(upgrade);
                restored++;
            }
            else
            {
                throw new InvalidOperationException($"Saved upgrade not found: {name}");
            }
        }

        foreach (var name in save.WeaponPerks)
        {
            if (perks.TryGetValue(name, out var perk))
            {
                MonoFields.PS(perk) = ps;
                perk.weaponUpgradeStat();
                AddSideUpgrade(display, perk, true, save.SideUpgradeIconNames);
                restored++;
            }
            else
            {
                throw new InvalidOperationException($"Saved weapon perk not found: {name}");
            }
        }

        return restored;
    }

    private static Dictionary<string, string> CaptureSideUpgradeIcons()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var display = SideUpgradesDisplayHandling.instance;
        foreach (var slots in new[] { display.sideWeaponUpgrades, display.sideMutationUpgrades })
        {
            for (var i = 0; i < slots?.Length; i++)
            {
                var name = Safe(() => slots[i]?.upgradeToDrawInfoFrom?.getName(), null);
                var spriteName = Safe(() => (slots[i] == null ? default : MonoFields.BackgroundImage(slots[i]))?.sprite?.name, null);
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(spriteName)) result[name] = spriteName;
            }
        }
        return result;
    }

    private static void AddSideUpgrade(SideUpgradesDisplayHandling display, Upgrade upgrade, bool weapon, Dictionary<string, string> iconNames)
    {
        display.addSideUpgrade(upgrade, weapon);
        try
        {
            var slots = weapon ? display.sideWeaponUpgrades : display.sideMutationUpgrades;
            var index = (weapon ? MonoFields.WeaponUpgradeCounter(display) : MonoFields.MutationUpgradeCounter(display)) - 1;
            if (index < 0 || index >= slots.Length) return;
            var image = (slots[index] == null ? default : MonoFields.BackgroundImage(slots[index]));
            if (image == null) return;

            Sprite sprite = null;
            var upgradeName = upgrade.getName();
            if (iconNames.TryGetValue(upgradeName, out var spriteName))
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (string.Equals(candidate?.name, spriteName, StringComparison.Ordinal)) { sprite = candidate; break; }
            }
            sprite ??= upgrade.ResolvePresentationIconSprite(true);
            if (sprite == null) return;
            image.sprite = sprite;
            image.color = Color.white;
            image.enabled = true;
            Logger?.LogInfo($"Restored {upgradeName} side icon as {sprite.name}.");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"Could not restore side-upgrade icon: {ex.Message}");
        }
    }

    private static List<EnemySave> CaptureEnemies(GameManager gm)
    {
        var result = new List<EnemySave>();
        var enemies = new List<Enemy>();
        foreach (var enemy in UnityEngine.Object.FindObjectsOfType<Enemy>())
        {
            if (!IsActiveRootEnemy(enemy))
            {
                continue;
            }

            var transform = enemy.transform;
            var position = transform.position;
            var rotation = transform.rotation;
            var snapshot = new EnemySave
            {
                PoolId = enemy.poolID ?? string.Empty,
                PrefabName = NormalizeEnemyName(enemy.gameObject.name),
                IsMiniBoss = enemy.isMiniBoss,
                MiniBossNumber = enemy.miniBossNumber,
                IsAdEnemy = enemy.isCurrentlyAdEnemy,
                PositionX = position.x,
                PositionY = position.y,
                PositionZ = position.z,
                RotationX = rotation.x,
                RotationY = rotation.y,
                RotationZ = rotation.z,
                RotationW = rotation.w,
                Health = enemy.health,
                CurrentHealth = enemy.currentHealth,
                MaxHealth = MonoFields.MaxHealth(enemy),
                ScoreWorth = enemy.scoreWorth,
                ExperienceLevel = enemy.expLevel,
                DamageMultiplier = enemy.damageMultiplier,
                Speed = enemy.speed,
                BaseSpeed = enemy.baseSpeed,
                CurrentMovementSpeed = MonoFields.CurrentMovementSpeed(enemy),
                CanShoot = MonoFields.CanShoot(enemy),
                ShootCooldown = enemy.shootCooldown,
                ShockVisualWorldScale = MonoFields.ShockTemp(enemy)?.activeInHierarchy == true
                    ? new VectorSave { X = MonoFields.ShockTemp(enemy).transform.lossyScale.x, Y = MonoFields.ShockTemp(enemy).transform.lossyScale.y, Z = MonoFields.ShockTemp(enemy).transform.lossyScale.z }
                    : null,
                State = CaptureEnemyState(enemy),
                Vectors = CaptureEnemyVectors(enemy),
                BulletNumbers = Copy(enemy.bulletNumbersList),
                AudioSources = CaptureAudioSources(transform),
                Animators = CaptureAnimators(transform)
            };

            if (FindEnemyPrefab(gm, snapshot) == null)
            {
                throw new InvalidOperationException($"Enemy prefab not found: {snapshot.PrefabName} ({snapshot.PoolId})");
            }
            result.Add(snapshot);
            enemies.Add(enemy);
        }

        var indicesByInstanceId = new Dictionary<int, int>();
        for (var i = 0; i < enemies.Count; i++) indicesByInstanceId[enemies[i].GetInstanceID()] = i;
        capturedEnemyIndices = indicesByInstanceId;
        for (var i = 0; i < enemies.Count; i++)
        {
            var targets = MonoFields.ShockDamageList(enemies[i]);
            for (var j = 0; j < targets?.Count; j++)
            {
                if (targets[j] != null && indicesByInstanceId.TryGetValue(targets[j].GetInstanceID(), out var index))
                {
                    result[i].ShockDamageEnemyIndices.Add(index);
                }
            }
        }

        return result;
    }

    private static List<Enemy> RestoreEnemies(GameManager gm, List<EnemySave> snapshots)
    {
        var pool = ObjectPool.instance ?? throw new InvalidOperationException("Enemy pool was unavailable.");
        var prefabs = new List<GameObject>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            prefabs.Add(FindEnemyPrefab(gm, snapshot)
                ?? throw new InvalidOperationException($"Saved enemy prefab not found: {snapshot.PrefabName} ({snapshot.PoolId})"));
        }

        foreach (var enemy in UnityEngine.Object.FindObjectsOfType<Enemy>())
        {
            if (IsActiveRootEnemy(enemy))
            {
                enemy.gameObject.SetActive(false);
            }
        }
        gm.enemiesOnScreen = 0;
        gm.minibossAliveList?.Clear();

        var restored = new List<Enemy>(snapshots.Count);
        pendingEnemyPlacements.Clear();
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var prefab = prefabs[i];
            Enemy enemy;
            if (snapshot.IsMiniBoss)
            {
                enemy = pool.getPooledMiniBoss(prefab.GetComponent<Enemy>(), snapshot.MiniBossNumber, true);
            }
            else
            {
                enemy = pool.getPooledEnemy(prefab)?.GetComponent<Enemy>();
            }

            if (enemy == null)
            {
                throw new InvalidOperationException($"Enemy pool could not restore: {snapshot.PrefabName} ({snapshot.PoolId})");
            }

            var position = new Vector3(snapshot.PositionX, snapshot.PositionY, snapshot.PositionZ);
            var rotation = new Quaternion(snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ, snapshot.RotationW);
            enemy.transform.SetPositionAndRotation(position, rotation);
            enemy.gameObject.SetActive(true);
            RestoreEnemyPosition(enemy, position, rotation);
            enemy.poolID = snapshot.PoolId;
            enemy.isMiniBoss = snapshot.IsMiniBoss;
            enemy.miniBossNumber = snapshot.MiniBossNumber;
            enemy.isCurrentlyAdEnemy = snapshot.IsAdEnemy;
            enemy.health = snapshot.Health;
            MonoFields.MaxHealth(enemy) = snapshot.MaxHealth;
            enemy.currentHealth = Math.Clamp(snapshot.CurrentHealth, 0.01f, Math.Max(0.01f, snapshot.MaxHealth));
            enemy.scoreWorth = snapshot.ScoreWorth;
            enemy.expLevel = snapshot.ExperienceLevel;
            enemy.damageMultiplier = snapshot.DamageMultiplier;
            enemy.speed = snapshot.Speed;
            enemy.baseSpeed = snapshot.BaseSpeed;
            MonoFields.CurrentMovementSpeed(enemy) = snapshot.CurrentMovementSpeed;
            MonoFields.CanShoot(enemy) = snapshot.CanShoot;
            enemy.shootCooldown = snapshot.ShootCooldown;
            RestoreEnemyEffects(enemy, snapshot);
            RestoreEnemyState(enemy, snapshot.State);
            RestoreEnemyVectors(enemy, snapshot.Vectors);
            Replace(enemy.bulletNumbersList, snapshot.BulletNumbers);
            RestoreAudioSources(enemy.transform, snapshot.AudioSources);
            RestoreAnimators(enemy.transform, snapshot.Animators);
            RestoreEnemyPosition(enemy, position, rotation);
            restored.Add(enemy);
            pendingEnemyPlacements.Add(new EnemyPlacement(enemy, position, rotation));

            if (snapshot.IsMiniBoss && gm.minibossAliveList != null && !gm.minibossAliveList.Contains(enemy))
            {
                gm.minibossAliveList.Add(enemy);
            }
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            var targets = MonoFields.ShockDamageList(restored[i]);
            if (targets == null) continue;
            targets.Clear();
            foreach (var targetIndex in snapshots[i].ShockDamageEnemyIndices)
            {
                if (targetIndex >= 0 && targetIndex < restored.Count) targets.Add(restored[targetIndex]);
            }
        }

        gm.enemiesOnScreen = snapshots.Count;
        placementFramesRemaining = 3;
        return restored;
    }

    private static List<WorldObjectSave> CaptureWorldObjects()
    {
        var pool = ObjectPool.instance ?? throw new InvalidOperationException("Object pool was unavailable.");
        var result = new List<WorldObjectSave>();
        var seen = new HashSet<int>();

        CaptureFuelPool(result, seen, pool.expPool, 1);
        CaptureFuelPool(result, seen, pool.expPool2, 2);
        CaptureFuelPool(result, seen, pool.expPool3, 3);
        CaptureFuelPool(result, seen, pool.expPool4, 4);
        CaptureFuelPool(result, seen, pool.expPool5, 5);
        CaptureFuelPool(result, seen, pool.expPool6, 6);
        CaptureFuelPool(result, seen, pool.expPool7, 7);
        foreach (var fuel in Resources.FindObjectsOfTypeAll<FuelScript>())
        {
            var saved = CaptureWorldObject(result, seen, fuel?.gameObject, fuel?.isHeartPickup == true ? "heartPickup" : "experience");
            if (saved != null) saved.Variant = Math.Clamp(fuel.expPoolNumber, 1, 7);
        }

        foreach (var mine in pool.landminePool)
            CaptureWorldObject(result, seen, mine?.gameObject, "landmine");
        foreach (var projectile in pool.bulletPool)
            if (projectile != null && !projectile.isLandMine) CaptureWorldObject(result, seen, projectile.gameObject, "projectile");
        foreach (var projectile in Resources.FindObjectsOfTypeAll<Projectile>())
            CaptureWorldObject(result, seen, projectile?.gameObject, projectile?.isLandMine == true ? "landmine" : "projectile");
        CaptureWorldObject(result, seen, GetActiveMissile(), "missile");
        foreach (var charge in pool.plasmaChargePool)
            CaptureWorldObject(result, seen, charge, "plasmaCharge");

        CaptureComponentPool(result, seen, pool.pulseSlashPool, "pulseSlash");
        CaptureGameObjectPool(result, seen, MonoFields.ClonedSentryPool(pool), "clonedSentry");
        CaptureGameObjectPool(result, seen, MonoFields.PlasmaCoilPool(pool), "plasmaCoil");
        CaptureGameObjectPool(result, seen, MonoFields.PlasmaBeamPool(pool), "plasmaBeam");
        CaptureGameObjectPool(result, seen, MonoFields.SingularityPool(pool), "singularity");
        CaptureGameObjectPool(result, seen, MonoFields.SingularitySlingshotPool(pool), "singularitySlingshot");
        CaptureGameObjectPool(result, seen, pool.noxiousRemnantPool, "noxiousRemnant");
        CaptureGameObjectPool(result, seen, MonoFields.PsychicEruptionPool(pool), "psychicEruption");
        CaptureGameObjectPool(result, seen, MonoFields.ConcealedSpiralPool(pool), "concealedSpiral");
        CaptureGameObjectPool(result, seen, MonoFields.PulseAnomalyPool(pool), "pulseAnomaly");
        CaptureGameObjectPool(result, seen, MonoFields.PhoenixMeteorPool(pool), "phoenixMeteor");
        CaptureGameObjectPool(result, seen, MonoFields.GuidedLaserPool(pool), "guidedLaser");
        CaptureGameObjectPool(result, seen, pool.phosphorusMissilePool, "phosphorusMissile");
        CaptureGameObjectPool(result, seen, MonoFields.LightMeteorPool(pool), "lightMeteor");
        CaptureGameObjectPool(result, seen, pool.lightOrbPool, "lightOrb");
        CaptureGameObjectPool(result, seen, MonoFields.CosmicCraterPool(pool), "cosmicCrater");
        CaptureGameObjectPool(result, seen, MonoFields.TitanicAsteroidPool(pool), "titanicAsteroid");
        CaptureGameObjectPool(result, seen, MonoFields.OrbitalMortarParticlePool(pool), "orbitalMortar");
        CaptureGameObjectPool(result, seen, MonoFields.TracerRoundPool(pool), "tracerRound");
        CaptureGameObjectPool(result, seen, MonoFields.DragonsBreathPool(pool), "dragonsBreath");
        CaptureGameObjectPool(result, seen, MonoFields.DragonsBreathFirePool(pool), "dragonsBreathFire");
        CaptureGameObjectPool(result, seen, MonoFields.BoltSnarePool(pool), "boltSnare");
        CaptureGameObjectPool(result, seen, MonoFields.FirePool(pool), "fire");
        CaptureGameObjectPool(result, seen, MonoFields.CombustionPool(pool), "combustion");
        CaptureGameObjectPool(result, seen, MonoFields.RingOfFirePool(pool), "ringOfFire");
        CaptureGameObjectPool(result, seen, MonoFields.WildFireParticlePool(pool), "wildFire");

        CaptureComponentPool(result, seen, pool.frostCurrentPool, "frostCurrent");
        CaptureComponentPool(result, seen, pool.callistoExplosionPool, "callistoExplosion");
        CaptureComponentPool(result, seen, pool.nuclearBombPool, "nuclearBomb");
        CaptureComponentPool(result, seen, pool.pulseCannonPool, "pulseCannon");
        CaptureComponentPool(result, seen, pool.guassSwordStationaryColliderPool, "gaussSwordCollider");

        var bossPickup = CaptureWorldObject(result, seen, pool.mutationsBossPickupObj, "mutationPickup");
        if (bossPickup != null) bossPickup.Variant = 1;
        foreach (var pickup in Resources.FindObjectsOfTypeAll<MutationPickupScript>())
            CaptureWorldObject(result, seen, pickup?.gameObject, "mutationPickup");

        var tesla = Safe(() => GameRefs.Instance?.weaponManager?.teslaCurrentObj, null);
        CaptureWorldObject(result, seen, tesla, "teslaCurrent");
        capturedEnemyIndices.Clear();
        return result;
    }

    private static void CaptureFuelPool(List<WorldObjectSave> result, HashSet<int> seen, List<FuelScript> objects, int variant)
    {
        for (var i = 0; i < objects?.Count; i++)
        {
            var fuel = objects[i];
            if (fuel == null || !fuel.gameObject.activeInHierarchy) continue;
            var saved = CaptureWorldObject(result, seen, fuel.gameObject, fuel.isHeartPickup ? "heartPickup" : "experience");
            if (saved != null) saved.Variant = variant;
        }
    }

    private static void CaptureGameObjectPool(List<WorldObjectSave> result, HashSet<int> seen, List<GameObject> objects, string kind)
    {
        for (var i = 0; i < objects?.Count; i++) CaptureWorldObject(result, seen, objects[i], kind);
    }

    private static void CaptureComponentPool<T>(List<WorldObjectSave> result, HashSet<int> seen, List<T> objects, string kind) where T : Component
    {
        for (var i = 0; i < objects?.Count; i++) CaptureWorldObject(result, seen, objects[i]?.gameObject, kind);
    }

    private static WorldObjectSave CaptureWorldObject(List<WorldObjectSave> result, HashSet<int> seen, GameObject obj, string kind)
    {
        if (obj == null || !obj.activeInHierarchy || !seen.Add(obj.GetInstanceID())) return null;
        var transform = obj.transform;
        var rotation = transform.rotation;
        var scale = transform.localScale;
        var saved = new WorldObjectSave
        {
            Kind = kind,
            PositionX = transform.position.x,
            PositionY = transform.position.y,
            PositionZ = transform.position.z,
            RotationX = rotation.x,
            RotationY = rotation.y,
            RotationZ = rotation.z,
            RotationW = rotation.w,
            ScaleX = scale.x,
            ScaleY = scale.y,
            ScaleZ = scale.z
        };

        var componentOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var component in obj.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var path = GetTransformPath(transform, component.transform);
            var type = component.GetType();
            var typeName = type.FullName ?? type.Name;
            var key = path + "|" + typeName;
            componentOrdinals.TryGetValue(key, out var ordinal);
            componentOrdinals[key] = ordinal + 1;
            saved.Components.Add(new ComponentSave
            {
                Path = path,
                TypeName = typeName,
                Ordinal = ordinal,
                Enabled = component.enabled,
                State = CaptureRuntimeState(component, type),
                Vectors = CaptureRuntimeVectors(component, type)
            });
        }

        CaptureWorldHierarchy(transform, transform, saved.Children);
        foreach (var body in obj.GetComponentsInChildren<Rigidbody>(true))
        {
            if (body == null) continue;
            var velocity = body.velocity;
            var angularVelocity = body.angularVelocity;
            saved.Rigidbodies.Add(new RigidbodySave
            {
                Path = GetTransformPath(transform, body.transform),
                Velocity = new VectorSave { X = velocity.x, Y = velocity.y, Z = velocity.z },
                AngularVelocity = new VectorSave { X = angularVelocity.x, Y = angularVelocity.y, Z = angularVelocity.z },
                IsKinematic = body.isKinematic
            });
        }

        CaptureProjectileRelationships(obj.GetComponentInChildren<Projectile>(true), saved);
        saved.AudioSources = CaptureAudioSources(transform);
        saved.Animators = CaptureAnimators(transform);

        result.Add(saved);
        return saved;
    }

    private static int RestoreWorldObjects(GameManager gm, WeaponManager wm, List<WorldObjectSave> snapshots, List<Enemy> enemies)
    {
        var pool = ObjectPool.instance ?? throw new InvalidOperationException("Object pool was unavailable.");
        DisableRestorableWorldObjects();
        var restored = 0;
        foreach (var saved in snapshots)
        {
            var obj = SpawnWorldObject(gm, wm, pool, saved);
            if (obj == null) throw new InvalidOperationException($"Could not restore world object: {saved.Kind}");
            ApplyWorldObject(obj, saved, enemies);
            restored++;
        }
        return restored;
    }

    private static GameObject SpawnWorldObject(GameManager gm, WeaponManager wm, ObjectPool pool, WorldObjectSave saved)
    {
        switch (saved.Kind)
        {
            case "experience":
                var expPrefab = GetExperiencePrefab(pool, saved.Variant);
                return expPrefab == null ? null : pool.getEXP(expPrefab.gameObject)?.gameObject;
            case "heartPickup":
                return MonoFields.HeartDrop(gm) == null ? null : UnityEngine.Object.Instantiate(MonoFields.HeartDrop(gm));
            case "mutationPickup":
                if (saved.Variant == 1 && pool.mutationsBossPickupObj != null) return pool.mutationsBossPickupObj;
                return MonoFields.MutationsPickupPrefab(pool) == null ? null : UnityEngine.Object.Instantiate(MonoFields.MutationsPickupPrefab(pool));
            case "landmine": return pool.getPooledLandMine()?.gameObject;
            case "projectile": return pool.getPooledBullet(true)?.gameObject;
            case "missile": return MonoFields.MissileObj(wm);
            case "pulseSlash": return pool.getPooledPulseSlashProjectile(true)?.gameObject;
            case "clonedSentry":
                MonoFields.ClonedSentryObj(wm) = pool.getPooledClonedSentry();
                return MonoFields.ClonedSentryObj(wm);
            case "plasmaCharge": return pool.getPooledPlasmaCharge(true);
            case "plasmaCoil": return pool.getPooledPlasmaCoil();
            case "plasmaBeam": return pool.getPooledPlasmaBeam(true);
            case "singularity": return pool.getPooledSingularity();
            case "singularitySlingshot": return pool.getPooledSingularitySlingshotObj();
            case "noxiousRemnant": return pool.getPooledNoxiousRemnantObj();
            case "psychicEruption": return pool.getPooledPsychicEruptionObj();
            case "concealedSpiral": return pool.getPooledConcealedSpiralObj(true);
            case "pulseAnomaly": return pool.getPooledPulseAnomaly();
            case "phoenixMeteor": return pool.getPooledPhoenixMeteor();
            case "guidedLaser": return pool.getPooledGuidedLaser();
            case "phosphorusMissile": return pool.getPooledPhosphorusMissile(true);
            case "lightMeteor": return pool.getPooledLightMeteor(true);
            case "lightOrb": return pool.getPooledLightOrb();
            case "cosmicCrater": return pool.getPooledCosmicCrater(true);
            case "titanicAsteroid": return pool.getPooledTitanicAsteroid(true);
            case "orbitalMortar": return pool.getPooledOrbitalMortarObj();
            case "tracerRound": return pool.getPooledTracerRound();
            case "dragonsBreath": return pool.getPooledDragonsBreathBullet();
            case "dragonsBreathFire": return pool.getPooledDragonsBreathFire();
            case "boltSnare": return pool.getPooledBoltSnare();
            case "fire": return pool.getPooledFire();
            case "combustion": return pool.getPooledCombustionParticle();
            case "ringOfFire": return pool.getPooledRingOfFireParticle();
            case "wildFire": return pool.getPooledWildFireParticle();
            case "frostCurrent": return pool.getPooledFrostCurrent()?.gameObject;
            case "callistoExplosion": return pool.getPooledCallistoExplosion()?.gameObject;
            case "nuclearBomb": return pool.getPooledNuclearBomb()?.gameObject;
            case "pulseCannon": return pool.getPooledPulseCannon(true)?.gameObject;
            case "gaussSwordCollider": return pool.getPooledGuassSwordStationaryCollider(true)?.gameObject;
            case "teslaCurrent":
                if (wm.teslaCurrentObj == null && MonoFields.TeslaCurrentPrefab(pool) != null)
                    wm.teslaCurrentObj = UnityEngine.Object.Instantiate(MonoFields.TeslaCurrentPrefab(pool));
                return wm.teslaCurrentObj;
            default: return null;
        }
    }

    private static FuelScript GetExperiencePrefab(ObjectPool pool, int variant) => variant switch
    {
        2 => MonoFields.ExpDrop2(pool),
        3 => MonoFields.ExpDrop3(pool),
        4 => MonoFields.ExpDrop4(pool),
        5 => MonoFields.ExpDrop5(pool),
        6 => MonoFields.ExpDrop6(pool),
        7 => MonoFields.ExpDrop7(pool),
        _ => MonoFields.ExpDrop(pool)
    };

    private static void ApplyWorldObject(GameObject obj, WorldObjectSave saved, List<Enemy> enemies)
    {
        obj.SetActive(false);
        obj.transform.SetPositionAndRotation(
            new Vector3(saved.PositionX, saved.PositionY, saved.PositionZ),
            new Quaternion(saved.RotationX, saved.RotationY, saved.RotationZ, saved.RotationW));
        obj.transform.localScale = new Vector3(saved.ScaleX, saved.ScaleY, saved.ScaleZ);
        obj.SetActive(true);

        foreach (var componentSave in saved.Components)
        {
            var transform = FindTransform(obj.transform, componentSave.Path);
            if (transform == null) continue;
            var ordinal = 0;
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                if (component == null) continue;
                var type = component.GetType();
                if (!string.Equals(type.FullName ?? type.Name, componentSave.TypeName, StringComparison.Ordinal)) continue;
                if (ordinal++ != componentSave.Ordinal) continue;
                RestoreRuntimeState(component, type, componentSave.State);
                RestoreRuntimeVectors(component, type, componentSave.Vectors);
                component.enabled = componentSave.Enabled;
                break;
            }
        }

        foreach (var bodySave in saved.Rigidbodies)
        {
            var transform = FindTransform(obj.transform, bodySave.Path);
            var body = transform?.GetComponent<Rigidbody>();
            if (body == null) continue;
            body.isKinematic = bodySave.IsKinematic;
            body.velocity = new Vector3(bodySave.Velocity.X, bodySave.Velocity.Y, bodySave.Velocity.Z);
            body.angularVelocity = new Vector3(bodySave.AngularVelocity.X, bodySave.AngularVelocity.Y, bodySave.AngularVelocity.Z);
        }

        foreach (var child in saved.Children)
        {
            var transform = FindTransform(obj.transform, child.Path);
            if (transform != null && transform != obj.transform) transform.gameObject.SetActive(child.Active);
        }

        RestoreProjectileRelationships(obj.GetComponentInChildren<Projectile>(true), saved, enemies);
        RestoreAudioSources(obj.transform, saved.AudioSources);
        RestoreAnimators(obj.transform, saved.Animators);
    }

    private static void CaptureProjectileRelationships(Projectile projectile, WorldObjectSave saved)
    {
        if (projectile == null) return;
        for (var i = 0; i < MonoFields.PierceList(projectile)?.Count; i++)
            AddEnemyObjectReference(saved.PierceTargets, MonoFields.PierceList(projectile)[i]);
        if (MonoFields.LaserDamageList(projectile) != null)
            foreach (var collider in MonoFields.LaserDamageList(projectile))
                AddEnemyObjectReference(saved.LaserTargets, collider?.gameObject);
        for (var i = 0; i < MonoFields.FrostSawsList(projectile)?.Count; i++)
            AddEnemyIndex(saved.FrostSawTargets, MonoFields.FrostSawsList(projectile)[i]);
        for (var i = 0; i < MonoFields.ExecutionSawsList(projectile)?.Count; i++)
            AddEnemyIndex(saved.ExecutionSawTargets, MonoFields.ExecutionSawsList(projectile)[i]);
        saved.ImmuneEnemy = CreateEnemyObjectReference(projectile.immuneEnemy);
    }

    private static void RestoreProjectileRelationships(Projectile projectile, WorldObjectSave saved, List<Enemy> enemies)
    {
        if (projectile == null) return;
        MonoFields.PierceList(projectile)?.Clear();
        foreach (var target in saved.PierceTargets)
        {
            var obj = ResolveEnemyObject(target, enemies);
            if (obj != null) MonoFields.PierceList(projectile)?.Add(obj);
        }

        MonoFields.LaserDamageList(projectile)?.Clear();
        foreach (var target in saved.LaserTargets)
        {
            var collider = ResolveEnemyObject(target, enemies)?.GetComponent<Collider>();
            if (collider != null) MonoFields.LaserDamageList(projectile)?.Add(collider);
        }

        MonoFields.FrostSawsList(projectile)?.Clear();
        foreach (var index in saved.FrostSawTargets)
            if (index >= 0 && index < enemies.Count) MonoFields.FrostSawsList(projectile)?.Add(enemies[index]);
        MonoFields.ExecutionSawsList(projectile)?.Clear();
        foreach (var index in saved.ExecutionSawTargets)
            if (index >= 0 && index < enemies.Count) MonoFields.ExecutionSawsList(projectile)?.Add(enemies[index]);
        projectile.immuneEnemy = ResolveEnemyObject(saved.ImmuneEnemy, enemies);
    }

    private static EnemyRelationshipsSave CaptureEnemyRelationships(PlayerStats ps, WeaponManager wm)
    {
        var saved = new EnemyRelationshipsSave();
        for (var i = 0; i < ps.shockList?.Count; i++) AddEnemyObjectReference(saved.ShockTargets, ps.shockList[i]);
        for (var i = 0; i < ps.executionStrikeList?.Count; i++) AddEnemyIndex(saved.ExecutionStrikeTargets, ps.executionStrikeList[i]);
        if (MonoFields.BlightDecayTrackedEnemies(ps) != null)
            foreach (var enemy in MonoFields.BlightDecayTrackedEnemies(ps)) AddEnemyIndex(saved.BlightTrackedTargets, enemy);
        for (var i = 0; i < wm.visibleEnemiesList?.Count; i++) AddEnemyIndex(saved.VisibleTargets, wm.visibleEnemiesList[i]);
        for (var i = 0; i < MonoFields.RadiationDamageList(wm)?.Count; i++) AddEnemyIndex(saved.RadiationTargets, MonoFields.RadiationDamageList(wm)[i]);
        saved.ParriedObject = CreateEnemyObjectReference(wm.parriedGameObject);
        return saved;
    }

    private static void RestoreEnemyRelationships(PlayerStats ps, WeaponManager wm, EnemyRelationshipsSave saved, List<Enemy> enemies)
    {
        if (saved == null) return;
        ps.shockList?.Clear();
        foreach (var target in saved.ShockTargets)
        {
            var obj = ResolveEnemyObject(target, enemies);
            if (obj != null) ps.shockList?.Add(obj);
        }

        RestoreEnemyList(ps.executionStrikeList, saved.ExecutionStrikeTargets, enemies);
        MonoFields.BlightDecayTrackedEnemies(ps)?.Clear();
        foreach (var index in saved.BlightTrackedTargets)
            if (index >= 0 && index < enemies.Count) MonoFields.BlightDecayTrackedEnemies(ps)?.Add(enemies[index]);
        RestoreEnemyList(wm.visibleEnemiesList, saved.VisibleTargets, enemies);
        RestoreEnemyList(MonoFields.RadiationDamageList(wm), saved.RadiationTargets, enemies);
        wm.parriedGameObject = ResolveEnemyObject(saved.ParriedObject, enemies);
    }

    private static void RestoreEnemyList(List<Enemy> target, List<int> indices, List<Enemy> enemies)
    {
        if (target == null) return;
        target.Clear();
        foreach (var index in indices)
            if (index >= 0 && index < enemies.Count) target.Add(enemies[index]);
    }

    private static void AddEnemyIndex(List<int> result, Enemy enemy)
    {
        enemy = RootEnemy(enemy);
        if (enemy != null && capturedEnemyIndices.TryGetValue(enemy.GetInstanceID(), out var index)) result.Add(index);
    }

    private static void AddEnemyObjectReference(List<EnemyObjectReferenceSave> result, GameObject obj)
    {
        var saved = CreateEnemyObjectReference(obj);
        if (saved != null) result.Add(saved);
    }

    private static EnemyObjectReferenceSave CreateEnemyObjectReference(GameObject obj)
    {
        var enemy = RootEnemy(obj?.GetComponentInParent<Enemy>());
        if (enemy == null || !capturedEnemyIndices.TryGetValue(enemy.GetInstanceID(), out var index)) return null;
        return new EnemyObjectReferenceSave { EnemyIndex = index, Path = GetTransformPath(enemy.transform, obj.transform) };
    }

    private static GameObject ResolveEnemyObject(EnemyObjectReferenceSave saved, List<Enemy> enemies)
    {
        if (saved == null || saved.EnemyIndex < 0 || saved.EnemyIndex >= enemies.Count) return null;
        return FindTransform(enemies[saved.EnemyIndex].transform, saved.Path)?.gameObject;
    }

    private static Enemy RootEnemy(Enemy enemy)
    {
        while (enemy?.parentenemy != null) enemy = enemy.parentenemy;
        return enemy;
    }

    private static GameObject GetActiveMissile()
    {
        var wm = GameRefs.Instance?.weaponManager;
        return wm == null ? null : MonoFields.MissileObj(wm);
    }

    private static void DisableRestorableWorldObjects()
    {
        foreach (var fuel in Resources.FindObjectsOfTypeAll<FuelScript>()) DisableIfActive(fuel?.gameObject);
        foreach (var pickup in Resources.FindObjectsOfTypeAll<MutationPickupScript>()) DisableIfActive(pickup?.gameObject);
        foreach (var mine in Resources.FindObjectsOfTypeAll<Projectile>())
            DisableIfActive(mine?.gameObject);
        DisableIfActive(GetActiveMissile());

        var pool = ObjectPool.instance;
        if (pool == null) return;
        DisablePool(MonoFields.ClonedSentryPool(pool));
        DisablePool(MonoFields.PlasmaCoilPool(pool));
        DisablePool(MonoFields.PlasmaBeamPool(pool));
        DisablePool(MonoFields.SingularityPool(pool));
        DisablePool(MonoFields.SingularitySlingshotPool(pool));
        DisablePool(pool.noxiousRemnantPool);
        DisablePool(MonoFields.PsychicEruptionPool(pool));
        DisablePool(MonoFields.ConcealedSpiralPool(pool));
        DisablePool(MonoFields.PulseAnomalyPool(pool));
        DisablePool(MonoFields.PhoenixMeteorPool(pool));
        DisablePool(MonoFields.GuidedLaserPool(pool));
        DisablePool(pool.phosphorusMissilePool);
        DisablePool(MonoFields.LightMeteorPool(pool));
        DisablePool(pool.lightOrbPool);
        DisablePool(MonoFields.CosmicCraterPool(pool));
        DisablePool(MonoFields.TitanicAsteroidPool(pool));
        DisablePool(MonoFields.OrbitalMortarParticlePool(pool));
        DisablePool(MonoFields.TracerRoundPool(pool));
        DisablePool(MonoFields.DragonsBreathPool(pool));
        DisablePool(MonoFields.DragonsBreathFirePool(pool));
        DisablePool(MonoFields.BoltSnarePool(pool));
        DisablePool(MonoFields.FirePool(pool));
        DisablePool(MonoFields.CombustionPool(pool));
        DisablePool(MonoFields.RingOfFirePool(pool));
        DisablePool(MonoFields.WildFireParticlePool(pool));
        foreach (var charge in pool.plasmaChargePool) DisableIfActive(charge);
        foreach (var current in pool.frostCurrentPool) DisableIfActive(current?.gameObject);
        foreach (var explosion in pool.callistoExplosionPool) DisableIfActive(explosion?.gameObject);
        foreach (var bomb in pool.nuclearBombPool) DisableIfActive(bomb?.gameObject);
        foreach (var cannon in pool.pulseCannonPool) DisableIfActive(cannon?.gameObject);
        foreach (var collider in pool.guassSwordStationaryColliderPool) DisableIfActive(collider?.gameObject);
    }

    private static void DisablePool(List<GameObject> objects)
    {
        for (var i = 0; i < objects?.Count; i++) DisableIfActive(objects[i]);
    }

    private static void DisableIfActive(GameObject obj)
    {
        if (obj != null && obj.activeInHierarchy) obj.SetActive(false);
    }

    private static void CaptureWorldHierarchy(Transform root, Transform current, List<ChildSave> result)
    {
        for (var i = 0; i < current.childCount; i++)
        {
            var child = current.GetChild(i);
            result.Add(new ChildSave { Path = GetTransformPath(root, child), Active = child.gameObject.activeSelf });
            CaptureWorldHierarchy(root, child, result);
        }
    }

    private static string GetTransformPath(Transform root, Transform target)
    {
        if (target == root) return string.Empty;
        var indices = new List<int>();
        for (var current = target; current != null && current != root; current = current.parent)
            indices.Add(current.GetSiblingIndex());
        indices.Reverse();
        return string.Join("/", indices);
    }

    private static Transform FindTransform(Transform root, string path)
    {
        var current = root;
        if (string.IsNullOrEmpty(path)) return current;
        foreach (var part in path.Split('/'))
        {
            if (!int.TryParse(part, out var index) || index < 0 || index >= current.childCount) return null;
            current = current.GetChild(index);
        }
        return current;
    }

    private static readonly FieldInfo OrbArrayField = AccessTools.Field(typeof(ExpManager), "orbs");
    private static readonly Type OrbType = OrbArrayField.FieldType.GetElementType();
    private static readonly FieldInfo OrbPosition = AccessTools.Field(OrbType, "pos");
    private static readonly FieldInfo OrbVelocity = AccessTools.Field(OrbType, "vel");
    private static readonly FieldInfo OrbSpeed = AccessTools.Field(OrbType, "speed");
    private static readonly FieldInfo OrbActive = AccessTools.Field(OrbType, "active");
    private static readonly FieldInfo OrbSpawnTime = AccessTools.Field(OrbType, "spawnTime");
    private static readonly FieldInfo OrbLevel = AccessTools.Field(OrbType, "level");
    private static readonly FieldInfo OrbValue = AccessTools.Field(OrbType, "value");
    private static readonly FieldInfo OrbCanFloat = AccessTools.Field(OrbType, "canFloatExp");
    private static readonly FieldInfo OrbIsShinedOn = AccessTools.Field(OrbType, "isShinedOn");

    private static List<ExpOrbSave> CaptureExpOrbs(ExpManager manager)
    {
        var orbs = (Array)OrbArrayField.GetValue(manager);
        var result = new List<ExpOrbSave>();
        for (var i = 0; i < MonoFields.OrbCount(manager); i++)
        {
            var orb = orbs.GetValue(i);
            if (!(bool)OrbActive.GetValue(orb)) continue;
            var position = (Vector3)OrbPosition.GetValue(orb);
            var velocity = (Vector3)OrbVelocity.GetValue(orb);
            result.Add(new ExpOrbSave
            {
                Position = new VectorSave { X = position.x, Y = position.y, Z = position.z },
                Velocity = new VectorSave { X = velocity.x, Y = velocity.y, Z = velocity.z },
                Speed = (float)OrbSpeed.GetValue(orb),
                Age = Math.Max(0f, Time.time - (float)OrbSpawnTime.GetValue(orb)),
                Level = (int)OrbLevel.GetValue(orb),
                Value = (int)OrbValue.GetValue(orb),
                CanFloat = (bool)OrbCanFloat.GetValue(orb),
                IsShinedOn = (bool)OrbIsShinedOn.GetValue(orb)
            });
        }
        return result;
    }

    private static void RestoreExpOrbs(ExpManager manager, List<ExpOrbSave> savedOrbs)
    {
        var currentOrbs = (Array)OrbArrayField.GetValue(manager);
        if (currentOrbs == null || savedOrbs.Count > currentOrbs.Length)
            throw new InvalidOperationException($"EXP orb storage could not fit {savedOrbs.Count} saved orbs.");

        var orbs = Array.CreateInstance(OrbType, currentOrbs.Length);
        for (var i = 0; i < savedOrbs.Count; i++)
        {
            var saved = savedOrbs[i];
            var orb = Activator.CreateInstance(OrbType);
            OrbPosition.SetValue(orb, new Vector3(saved.Position.X, saved.Position.Y, saved.Position.Z));
            OrbVelocity.SetValue(orb, new Vector3(saved.Velocity.X, saved.Velocity.Y, saved.Velocity.Z));
            OrbSpeed.SetValue(orb, saved.Speed);
            OrbActive.SetValue(orb, true);
            OrbSpawnTime.SetValue(orb, Time.time - saved.Age);
            OrbLevel.SetValue(orb, saved.Level);
            OrbValue.SetValue(orb, saved.Value);
            OrbCanFloat.SetValue(orb, saved.CanFloat);
            OrbIsShinedOn.SetValue(orb, saved.IsShinedOn);
            orbs.SetValue(orb, i);
        }

        OrbArrayField.SetValue(manager, orbs);
        MonoFields.OrbCount(manager) = savedOrbs.Count;
        MonoFields.CollectionParticleCount(manager) = 0;
        manager.pSystem?.Clear(true);
        manager.pSystemHidden?.Clear(true);
        manager.pSystemGlow?.Clear(true);
        manager.collectionEffectSystem?.Clear(true);
        manager.collectionGlowSystem?.Clear(true);
    }

    private static RunSessionStatsSave CaptureRunSessionStats() => new()
    {
        HitsTaken = RunSessionStats.HitsTaken,
        HeartsGained = RunSessionStats.HeartsGained,
        TotalDamageDealt = RunSessionStats.TotalDamageDealt,
        BulletsFired = RunSessionStats.BulletsFired,
        HighestDamageNumber = RunSessionStats.HighestDamageNumber
    };

    private static void RestoreRunSessionStats(RunSessionStatsSave saved)
    {
        AccessTools.Property(typeof(RunSessionStats), nameof(RunSessionStats.HitsTaken)).SetValue(null, saved.HitsTaken);
        AccessTools.Property(typeof(RunSessionStats), nameof(RunSessionStats.HeartsGained)).SetValue(null, saved.HeartsGained);
        AccessTools.Property(typeof(RunSessionStats), nameof(RunSessionStats.TotalDamageDealt)).SetValue(null, saved.TotalDamageDealt);
        AccessTools.Property(typeof(RunSessionStats), nameof(RunSessionStats.BulletsFired)).SetValue(null, saved.BulletsFired);
        AccessTools.Property(typeof(RunSessionStats), nameof(RunSessionStats.HighestDamageNumber)).SetValue(null, saved.HighestDamageNumber);
    }

    private static List<AudioSourceSave> CaptureRunAudio(GameRefs refs)
    {
        var result = new List<AudioSourceSave>();
        var seen = new HashSet<int>();
        CaptureAudioRoot(result, seen, refs.musicManager?.transform, "music");
        CaptureAudioRoot(result, seen, refs.generalAudioSource?.transform, "general");
        CaptureAudioRoot(result, seen, refs.playerObject?.transform, "player");
        CaptureAudioRoot(result, seen, refs.soundManagerObject?.transform, "sound");
        return result;
    }

    private static void CaptureAudioRoot(List<AudioSourceSave> result, HashSet<int> seen, Transform root, string owner)
    {
        if (root == null) return;
        foreach (var saved in CaptureAudioSources(root, owner, seen)) result.Add(saved);
    }

    private static List<AudioSourceSave> CaptureAudioSources(Transform root, string owner = "", HashSet<int> seen = null)
    {
        var result = new List<AudioSourceSave>();
        if (root == null) return result;
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
        {
            if (source == null || seen != null && !seen.Add(source.GetInstanceID())) continue;
            var path = GetTransformPath(root, source.transform);
            ordinals.TryGetValue(path, out var ordinal);
            ordinals[path] = ordinal + 1;
            result.Add(new AudioSourceSave
            {
                Owner = owner,
                Path = path,
                Ordinal = ordinal,
                ClipName = Safe(() => source.clip?.name, string.Empty) ?? string.Empty,
                TimeSamples = Safe(() => source.timeSamples, 0),
                Time = Safe(() => source.time, 0f),
                WasPlaying = Safe(() => source.isPlaying, false),
                Enabled = source.enabled,
                Loop = source.loop,
                Mute = source.mute,
                PlayOnAwake = source.playOnAwake,
                Volume = source.volume,
                Pitch = source.pitch,
                SpatialBlend = source.spatialBlend
            });
        }
        return result;
    }

    private static void RestoreRunAudio(GameRefs refs, List<AudioSourceSave> savedSources)
    {
        foreach (var saved in savedSources)
        {
            var root = saved.Owner switch
            {
                "music" => refs.musicManager?.transform,
                "general" => refs.generalAudioSource?.transform,
                "player" => refs.playerObject?.transform,
                "sound" => refs.soundManagerObject?.transform,
                _ => null
            };
            RestoreAudioSource(root, saved);
        }
    }

    private static void RestoreAudioSources(Transform root, List<AudioSourceSave> savedSources)
    {
        foreach (var saved in savedSources) RestoreAudioSource(root, saved);
    }

    private static void RestoreAudioSource(Transform root, AudioSourceSave saved)
    {
        var transform = root == null ? null : FindTransform(root, saved.Path);
        if (transform == null) return;
        var sources = transform.GetComponents<AudioSource>();
        if (saved.Ordinal < 0 || saved.Ordinal >= sources.Length) return;
        var source = sources[saved.Ordinal];
        if (source == null) return;

        try
        {
            source.Stop();
            if (!string.IsNullOrEmpty(saved.ClipName) && !string.Equals(source.clip?.name, saved.ClipName, StringComparison.Ordinal))
            {
                foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                {
                    if (clip != null && string.Equals(clip.name, saved.ClipName, StringComparison.Ordinal))
                    {
                        source.clip = clip;
                        break;
                    }
                }
            }
            source.loop = saved.Loop;
            source.mute = saved.Mute;
            source.playOnAwake = saved.PlayOnAwake;
            source.volume = saved.Volume;
            source.pitch = saved.Pitch;
            source.spatialBlend = saved.SpatialBlend;
            source.enabled = saved.Enabled;
            if (!saved.WasPlaying || !saved.Enabled || source.clip == null) return;
            source.Play();
            if (saved.TimeSamples > 0 && source.clip.samples > 0)
                source.timeSamples = Math.Clamp(saved.TimeSamples, 0, source.clip.samples - 1);
            else if (saved.Time > 0f && source.clip.length > 0f)
                source.time = Math.Clamp(saved.Time, 0f, Math.Max(0f, source.clip.length - 0.01f));
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"Could not restore audio source {saved.Owner}:{saved.Path}: {ex.Message}");
        }
    }

    private static List<AnimatorSave> CaptureAnimators(Transform root, string owner = "")
    {
        var result = new List<AnimatorSave>();
        if (root == null) return result;
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null || animator.runtimeAnimatorController == null) continue;
            var path = GetTransformPath(root, animator.transform);
            ordinals.TryGetValue(path, out var ordinal);
            ordinals[path] = ordinal + 1;
            var saved = new AnimatorSave
            {
                Owner = owner,
                Path = path,
                Ordinal = ordinal,
                Enabled = animator.enabled,
                Speed = animator.speed
            };
            for (var layer = 0; layer < animator.layerCount; layer++)
            {
                var state = animator.GetCurrentAnimatorStateInfo(layer);
                saved.Layers.Add(new AnimatorLayerSave
                {
                    StateHash = state.fullPathHash,
                    NormalizedTime = state.normalizedTime,
                    Weight = animator.GetLayerWeight(layer)
                });
            }
            result.Add(saved);
        }
        return result;
    }

    private static void RestoreAnimators(Transform root, List<AnimatorSave> savedAnimators)
    {
        if (root == null) return;
        foreach (var saved in savedAnimators)
        {
            var transform = FindTransform(root, saved.Path);
            if (transform == null) continue;
            var animators = transform.GetComponents<Animator>();
            if (saved.Ordinal < 0 || saved.Ordinal >= animators.Length) continue;
            var animator = animators[saved.Ordinal];
            if (animator == null || animator.runtimeAnimatorController == null) continue;
            animator.speed = saved.Speed;
            animator.enabled = saved.Enabled;
            for (var layer = 0; layer < saved.Layers.Count && layer < animator.layerCount; layer++)
            {
                var state = saved.Layers[layer];
                animator.SetLayerWeight(layer, state.Weight);
                if (state.StateHash != 0) animator.Play(state.StateHash, layer, state.NormalizedTime);
            }
        }
    }

    private static void RestoreRandomState(RandomStateSave saved)
    {
        if (saved == null) return;
        var json = JsonConvert.SerializeObject(new { s0 = saved.S0, s1 = saved.S1, s2 = saved.S2, s3 = saved.S3 });
        UnityEngine.Random.state = JsonUtility.FromJson<UnityEngine.Random.State>(json);
    }

    private static void PreparePlayerPlacement(WeaponManager wm, RunSave save)
    {
        pendingPlayerTransform = MonoFields.Player(wm).transform;
        pendingPlayerBody = MonoFields.PlayerBody(wm);
        pendingPlayerPosition = new Vector3(save.PlayerPositionX, save.PlayerPositionY, save.PlayerPositionZ);
        pendingPlayerRotation = new Quaternion(save.PlayerRotationX, save.PlayerRotationY, save.PlayerRotationZ, save.PlayerRotationW);
        RestorePlayerPosition();
    }

    private static void ApplyPendingPlacements()
    {
        if (placementFramesRemaining <= 0) return;
        var refs = GameRefs.Instance;
        if (pendingPlayerTransform == null || refs == null || refs.playerStats == null || refs.weaponManager == null)
            throw new InvalidOperationException("Player disappeared during checkpoint placement.");

        var save = pendingPlacementSave;
        RestorePlayerPosition();
        Replace(refs.playerStats.chosenUpgrades, save.ChosenUpgrades);
        Replace(refs.playerStats.unlockedWeaponPerksList, save.WeaponPerks);
        foreach (var placement in pendingEnemyPlacements)
        {
            if (placement.Enemy == null || !placement.Enemy.gameObject.activeInHierarchy)
                throw new InvalidOperationException("An enemy disappeared during checkpoint placement.");
            RestoreEnemyPosition(placement.Enemy, placement.Position, placement.Rotation);
        }
        if (--placementFramesRemaining == 0)
        {
            // Replayed snare objects register with WeaponManager in Start.
            RestoreSnares(refs.weaponManager, save.Snares, save.FirstSnareIndex, ReadInt(save.WeaponManagerState, "currentSnareCount"));
            foreach (var screen in Resources.FindObjectsOfTypeAll<EquippedWeaponPerkScreen>())
                Safe(() => { MonoFields.RefreshPerkScreen(screen); return true; }, false);
            pendingEnemyPlacements.Clear();
            pendingPlayerTransform = null;
            pendingPlayerBody = null;
            pendingPlacementSave = null;
        }
    }

    private static void RestorePlayerPosition()
    {
        if (pendingPlayerTransform == null) return;
        pendingPlayerTransform.SetPositionAndRotation(pendingPlayerPosition, pendingPlayerRotation);
        if (pendingPlayerBody != null)
        {
            pendingPlayerBody.position = pendingPlayerPosition;
            pendingPlayerBody.rotation = pendingPlayerRotation;
        }
        pendingPlayerTransform.SetPositionAndRotation(pendingPlayerPosition, pendingPlayerRotation);
    }

    private static void RestoreEnemyPosition(Enemy enemy, Vector3 position, Quaternion rotation)
    {
        enemy.transform.SetPositionAndRotation(position, rotation);
        if (MonoFields.EnemyBody(enemy) != null) MonoFields.EnemyBody(enemy).position = position;
        if (enemy.navMeshAgent != null && enemy.navMeshAgent.enabled)
        {
            enemy.navMeshAgent.Warp(position);
            enemy.navMeshAgent.nextPosition = position;
        }
        enemy.transform.SetPositionAndRotation(position, rotation);
    }

    private static Dictionary<string, string> CaptureEnemyState(Enemy enemy) =>
        CaptureRuntimeState(enemy, typeof(Enemy));

    private static void RestoreEnemyState(Enemy enemy, Dictionary<string, string> values) =>
        RestoreRuntimeState(enemy, typeof(Enemy), values);

    private static Dictionary<string, string> CaptureRuntimeState(object instance, Type declaredType)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in AccessTools.GetDeclaredFields(declaredType))
        {
            if (!IsRuntimeField(field) || !IsCheckpointType(field.FieldType, declaredType)) continue;
            values[field.Name] = Convert.ToString(field.GetValue(instance), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        return values;
    }

    private static void RestoreRuntimeState(object instance, Type declaredType, Dictionary<string, string> values)
    {
        foreach (var field in AccessTools.GetDeclaredFields(declaredType))
        {
            if (!IsRuntimeField(field) || !IsCheckpointType(field.FieldType, declaredType)
                || !values.TryGetValue(field.Name, out var text)) continue;
            field.SetValue(instance, Convert.ChangeType(text, field.FieldType, CultureInfo.InvariantCulture));
        }
    }

    private static bool IsRuntimeField(FieldInfo field) =>
        !field.IsStatic && !field.IsInitOnly && !field.IsLiteral && !field.Name.StartsWith("<", StringComparison.Ordinal);

    private static bool IsCheckpointType(Type type, Type owner) =>
        type == typeof(bool) || type == typeof(int) || type == typeof(float)
        || (type == typeof(string) && owner != typeof(GameManager));

    private static Dictionary<string, VectorSave> CaptureEnemyVectors(Enemy enemy) =>
        CaptureRuntimeVectors(enemy, typeof(Enemy));

    private static void RestoreEnemyVectors(Enemy enemy, Dictionary<string, VectorSave> values) =>
        RestoreRuntimeVectors(enemy, typeof(Enemy), values);

    private static Dictionary<string, VectorSave> CaptureRuntimeVectors(object instance, Type declaredType)
    {
        var values = new Dictionary<string, VectorSave>(StringComparer.Ordinal);
        foreach (var field in AccessTools.GetDeclaredFields(declaredType))
        {
            if (!IsRuntimeField(field) || field.FieldType != typeof(Vector3)) continue;
            var value = (Vector3)field.GetValue(instance);
            values[field.Name] = new VectorSave { X = value.x, Y = value.y, Z = value.z };
        }
        return values;
    }

    private static void RestoreRuntimeVectors(object instance, Type declaredType, Dictionary<string, VectorSave> values)
    {
        foreach (var field in AccessTools.GetDeclaredFields(declaredType))
        {
            if (!IsRuntimeField(field) || field.FieldType != typeof(Vector3)
                || !values.TryGetValue(field.Name, out var value)) continue;
            field.SetValue(instance, new Vector3(value.X, value.Y, value.Z));
        }
    }

    private static List<SnareSave> CaptureSnares(WeaponManager wm)
    {
        var result = new List<SnareSave>();
        for (var i = 0; i < wm.snareScripts?.Count; i++)
        {
            var snare = wm.snareScripts[i];
            if (snare == null) continue;
            var transform = snare.transform;
            var rotation = transform.localRotation;
            result.Add(new SnareSave
            {
                Active = snare.gameObject.activeSelf,
                ParticleActive = snare.particleObj?.activeSelf == true,
                ColliderEnabled = MonoFields.SnareCollider(snare)?.enabled == true,
                EnableTimer = snare.enableTimer,
                ColliderEnableTimer = MonoFields.SnareColliderTimer(snare),
                DisableObjectBuffer = snare.disableObjBuffer,
                SelectedForDash = snare.isSelectedForDash,
                LocalPosition = new VectorSave { X = transform.localPosition.x, Y = transform.localPosition.y, Z = transform.localPosition.z },
                LocalRotationX = rotation.x,
                LocalRotationY = rotation.y,
                LocalRotationZ = rotation.z,
                LocalRotationW = rotation.w
            });
        }
        return result;
    }

    private static void RestoreSnares(WeaponManager wm, List<SnareSave> snapshots, int firstSnareIndex, int savedCount)
    {
        var snares = wm.snareScripts;
        if (snapshots.Count > 0 && (snares == null || snares.Count < snapshots.Count))
            throw new InvalidOperationException($"Saved stab snares were unavailable: {snares?.Count ?? 0}/{snapshots.Count}");

        for (var i = 0; i < snares?.Count; i++)
        {
            var snare = snares[i];
            if (snare == null) continue;
            if (i >= snapshots.Count)
            {
                snare.gameObject.SetActive(false);
                continue;
            }

            var saved = snapshots[i];
            snare.gameObject.SetActive(false);
            snare.transform.localPosition = new Vector3(saved.LocalPosition.X, saved.LocalPosition.Y, saved.LocalPosition.Z);
            snare.transform.localRotation = new Quaternion(saved.LocalRotationX, saved.LocalRotationY, saved.LocalRotationZ, saved.LocalRotationW);
            snare.gameObject.SetActive(saved.Active);
            snare.enableTimer = saved.EnableTimer;
            MonoFields.SnareColliderTimer(snare) = saved.ColliderEnableTimer;
            snare.disableObjBuffer = saved.DisableObjectBuffer;
            if (saved.SelectedForDash) snare.selectForDashColor();
            snare.isSelectedForDash = saved.SelectedForDash;
            if (snare.particleObj != null) snare.particleObj.SetActive(saved.ParticleActive);
            if (MonoFields.SnareCollider(snare) != null) MonoFields.SnareCollider(snare).enabled = saved.ColliderEnabled;
        }

        wm.currentSnareCount = savedCount;
        MonoFields.FirstSnare(wm) = firstSnareIndex >= 0 && firstSnareIndex < (snares?.Count ?? 0) ? snares[firstSnareIndex] : null;
    }

    private static int FindSnareIndex(List<stabSnareScript> snares, stabSnareScript target)
    {
        if (target == null) return -1;
        for (var i = 0; i < snares?.Count; i++)
            if (snares[i] != null && snares[i].GetInstanceID() == target.GetInstanceID()) return i;
        return -1;
    }

    private static void RestoreEnemyEffects(Enemy enemy, EnemySave snapshot)
    {
        if (ReadBool(snapshot.State, "isFire")) enemy.setOnFire();
        if (ReadBool(snapshot.State, "isPoisoned")) enemy.poisonEnemy();

        var frozenTime = ReadFloat(snapshot.State, "frozenTime");
        if (ReadFloat(snapshot.State, "frozenTimer") > 0f || frozenTime > 0f)
        {
            enemy.freezeInstant(Math.Max(0.05f, frozenTime));
        }

        var slowTimer = ReadFloat(snapshot.State, "slowTimer");
        if (slowTimer > 0f) enemy.slowed(ReadFloat(snapshot.State, "slowedSpeed"), slowTimer);

        if (ReadInt(snapshot.State, "blightStacks") > 0) MonoFields.ShowBlightParticle(enemy);
        if (ReadInt(snapshot.State, "scourgeBlightTickStacks") > 0) MonoFields.ShowScourgeParticle(enemy);

        if (ReadBool(snapshot.State, "isShocked"))
        {
            try
            {
                var particle = ObjectPool.instance?.getPooledElectricityParticle();
                var holder = enemy.getParticleHolderTransform();
                if (particle != null && holder != null)
                {
                    particle.transform.SetParent(holder, false);
                    particle.transform.localPosition = Vector3.zero;
                    particle.transform.localRotation = Quaternion.identity;
                    var scale = snapshot.ShockVisualWorldScale ?? new VectorSave { X = 1f, Y = 1f, Z = 1f };
                    var parentScale = holder.lossyScale;
                    particle.transform.localScale = new Vector3(
                        DivideScale(scale.X, parentScale.x),
                        DivideScale(scale.Y, parentScale.y),
                        DivideScale(scale.Z, parentScale.z));
                    particle.SetActive(true);
                    MonoFields.ShockTemp(enemy) = particle;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"Could not restore enemy electricity visual: {ex.Message}");
            }
        }
    }

    private static bool ReadBool(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var text) && bool.TryParse(text, out var value) && value;

    private static int ReadInt(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static float ReadFloat(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;

    private static float DivideScale(float worldScale, float parentScale) =>
        Math.Abs(parentScale) > 0.0001f ? worldScale / parentScale : worldScale;

    private static bool IsActiveRootEnemy(Enemy enemy) => enemy != null && enemy.gameObject.activeInHierarchy
        && !MonoFields.EnemyDied(enemy) && !enemy.isEnemyLeg && enemy.parentenemy == null;

    private static GameObject FindEnemyPrefab(GameManager gm, EnemySave snapshot)
    {
        if (snapshot.IsMiniBoss)
        {
            for (var i = 0; i < gm.miniBossList?.Count; i++)
            {
                var prefab = gm.miniBossList[i];
                if (MatchesEnemy(prefab, snapshot)) return prefab.gameObject;
            }
            if (gm.miniBossList != null && snapshot.MiniBossNumber >= 0 && snapshot.MiniBossNumber < gm.miniBossList.Count)
            {
                return gm.miniBossList[snapshot.MiniBossNumber]?.gameObject;
            }
            return null;
        }

        var prefabObject = FindEnemyPrefab(MonoFields.EnemyPrefabs(gm), snapshot) ?? FindEnemyPrefab(MonoFields.AdEnemyPrefabs(gm), snapshot);
        return prefabObject;
    }

    private static GameObject FindEnemyPrefab(List<GameObject> prefabs, EnemySave snapshot)
    {
        for (var i = 0; i < prefabs?.Count; i++)
        {
            if (MatchesEnemy(prefabs[i]?.GetComponent<Enemy>(), snapshot)) return prefabs[i];
        }
        return null;
    }

    private static bool MatchesEnemy(Enemy prefab, EnemySave snapshot) => prefab != null
        && ((!string.IsNullOrWhiteSpace(snapshot.PoolId) && string.Equals(prefab.poolID, snapshot.PoolId, StringComparison.Ordinal))
            || string.Equals(NormalizeEnemyName(prefab.gameObject.name), snapshot.PrefabName, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeEnemyName(string name) => (name ?? string.Empty).Replace("(Clone)", string.Empty).Trim();

    private static Dictionary<string, string> CaptureGameManagerState(GameManager gm)
    {
        var values = CaptureRuntimeState(gm, typeof(GameManager));
        foreach (var name in SkippedGameManagerFields) values.Remove(name);
        return values;
    }

    private static void RestoreGameManagerState(GameManager gm, Dictionary<string, string> values)
    {
        var state = new Dictionary<string, string>(values, StringComparer.Ordinal);
        foreach (var name in SkippedGameManagerFields) state.Remove(name);
        RestoreRuntimeState(gm, typeof(GameManager), state);
    }

    private static StartMenu FindActiveStartMenu()
    {
        foreach (var menu in Resources.FindObjectsOfTypeAll<StartMenu>())
        {
            if (menu != null && menu.gameObject.scene.IsValid()) return menu;
        }
        return null;
    }

    private static weaponselect FindSavedWeapon(string weaponId)
    {
        var expected = NormalizeMenuText(weaponId);
        weaponselect fallback = null;
        foreach (var weapon in Resources.FindObjectsOfTypeAll<weaponselect>())
        {
            if (weapon == null) continue;
            var menuName = NormalizeMenuText(weapon.weaponname);
            if (menuName != expected && !NormalizeMenuText(weapon.gameObject.name).Contains(expected)) continue;
            if (weapon.gameObject.activeInHierarchy) return weapon;
            if (weapon.gameObject.scene.IsValid()) fallback = weapon;
        }
        return fallback;
    }

    private static MutationsMenu FindActiveMutationMenu()
    {
        MutationsMenu fallback = null;
        foreach (var menu in Resources.FindObjectsOfTypeAll<MutationsMenu>())
        {
            if (menu?.gameObject.activeInHierarchy == true) return menu;
            if (menu?.gameObject.scene.IsValid() == true) fallback = menu;
        }
        return fallback;
    }

    private static List<int> CaptureMutationLoadout()
    {
        var result = new List<int>(5);
        for (var slot = 1; slot <= 5; slot++) result.Add(EquippedMutationLoadout.GetSlot(slot));
        return result;
    }

    private static void ApplyMutationLoadout(List<int> loadout)
    {
        if (loadout == null || loadout.Count != 5) return;
        for (var slot = 1; slot <= 5; slot++) EquippedMutationLoadout.SetSlot(slot, loadout[slot - 1]);
    }

    private static bool PressActiveButton(string label)
    {
        var expected = NormalizeMenuText(label);
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button?.gameObject.activeInHierarchy != true || !button.interactable) continue;
            var legacy = button.GetComponentInChildren<Text>(true)?.text;
            var tmp = button.GetComponentInChildren<TMP_Text>(true)?.text;
            if (NormalizeMenuText(legacy) != expected && NormalizeMenuText(tmp) != expected) continue;
            button.onClick.Invoke();
            Logger?.LogInfo($"Pressed menu button: {label}.");
            return true;
        }
        return false;
    }

    private static bool PressButton(GameObject buttonObject, string label)
    {
        var button = buttonObject?.GetComponent<Button>() ?? buttonObject?.GetComponentInChildren<Button>(true);
        if (button == null) return PressActiveButton(label);
        button.onClick.Invoke();
        Logger?.LogInfo($"Pressed saved setup button: {label}.");
        return true;
    }

    private static string NormalizeMenuText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var chars = new char[value.Length];
        var count = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c)) chars[count++] = char.ToLowerInvariant(c);
        }
        return new string(chars, 0, count);
    }

    private static void AdvanceLaunchStage(int stage)
    {
        launchStage = stage;
        launchReadyFrame = Time.frameCount + 6;
        launchStageDeadline = Time.unscaledTime + 12f;
    }

    private static void FailLaunch(string detail, bool returnToMenu = false)
    {
        Show("Could not resume the run. Checkpoint retained.");
        Logger?.LogError(detail);
        pendingRestore = null;
        pendingCheckpointContents = null;
        launchPending = false;
        launchStage = 0;
        launchReadyFrame = 0;
        launchStageDeadline = 0f;
        launchNextButton = null;
        restoreNotBefore = 0f;
        restoreCompleted = false;
        waitingForResumeInput = false;
        placementFramesRemaining = 0;
        pendingEnemyPlacements.Clear();
        pendingPlayerTransform = null;
        pendingPlayerBody = null;
        pendingPlacementSave = null;
        CheckpointUi.HideLoadingOverlay();

        if (returnToMenu && !MultiplayerActive)
        {
            var scene = UnityEngine.Object.FindFirstObjectByType<SceneManagement>();
            if (scene != null)
            {
                ReturnToMenu(scene);
                return;
            }
        }
        EndCheckpointTestMode();
    }

    internal static void EndCheckpointTestMode()
    {
        if (!checkpointEnabledTestMode) return;
        cheatmanager.Instance.isTestMode = false;
        checkpointEnabledTestMode = false;
    }

    private static void ReturnToMenu(SceneManagement scene)
    {
        ReturningFromCheckpoint = true;
        try
        {
            scene.LoadMenuScene();
        }
        catch (Exception ex)
        {
            Show("Checkpoint retained. Returning to the menu failed.");
            Logger?.LogError($"Could not return to the menu: {ex}");
        }
        finally
        {
            ReturningFromCheckpoint = false;
        }
    }

    private static bool PressDifficultyButton(int difficulty)
    {
        var labels = new[] { "EASY", "NORMAL", "HARD", "NIGHTMARE" };
        var expected = labels[Math.Clamp(difficulty + 1, 0, labels.Length - 1)];
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (!button.gameObject.activeInHierarchy)
            {
                continue;
            }

            var legacy = button.GetComponentInChildren<Text>(true)?.text;
            var tmp = button.GetComponentInChildren<TMP_Text>(true)?.text;
            if (string.Equals(legacy?.Trim(), expected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tmp?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                button.onClick.Invoke();
                Logger?.LogInfo($"Selected saved difficulty: {expected}.");
                return true;
            }
        }

        return false;
    }

    private static List<string> Copy(List<string> source)
    {
        var result = new List<string>();
        for (var i = 0; i < source?.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(source[i]))
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    private static void Replace(List<string> target, List<string> values)
    {
        if (target == null)
        {
            return;
        }

        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static List<int> Copy(List<int> source)
    {
        var result = new List<int>();
        for (var i = 0; i < source?.Count; i++) result.Add(source[i]);
        return result;
    }

    private static void Replace(List<int> target, List<int> values)
    {
        if (target == null) return;
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static void Show(string text)
    {
        LastMessage = text;
        Logger?.LogInfo(text);
    }

    private static string FormatTime(float seconds) => $"{Math.Max(0, (int)seconds) / 60}:{Math.Max(0, (int)seconds) % 60:00}";

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    public sealed class RunSave
    {
        public int Version { get; set; } = SaveVersion;
        public DateTime SavedAtUtc { get; set; }
        public int Map { get; set; }
        public int Difficulty { get; set; }
        public bool TestMode { get; set; }
        public string WeaponId { get; set; } = "pistol";
        public List<int> MutationLoadout { get; set; } = new();
        public float PlayerPositionX { get; set; }
        public float PlayerPositionY { get; set; }
        public float PlayerPositionZ { get; set; }
        public float PlayerRotationX { get; set; }
        public float PlayerRotationY { get; set; }
        public float PlayerRotationZ { get; set; }
        public float PlayerRotationW { get; set; } = 1f;
        public int Health { get; set; }
        public int Level { get; set; }
        public int MaxHealth { get; set; }
        public float Experience { get; set; }
        public float LevelUpExperience { get; set; }
        public int SkillPoints { get; set; }
        public int Kills { get; set; }
        public int CurrentMutationCount { get; set; }
        public int MutationOffersSkipped { get; set; }
        public List<string> ChosenUpgrades { get; set; } = new();
        public List<string> WeaponPerks { get; set; } = new();
        public Dictionary<string, string> SideUpgradeIconNames { get; set; } = new();
        public Dictionary<string, string> GameManagerState { get; set; } = new();
        public Dictionary<string, string> ScoreTextState { get; set; } = new();
        public Dictionary<string, VectorSave> ScoreTextVectors { get; set; } = new();
        public Dictionary<string, string> PlayerStatsState { get; set; } = new();
        public Dictionary<string, VectorSave> PlayerStatsVectors { get; set; } = new();
        public Dictionary<string, string> WeaponManagerState { get; set; } = new();
        public Dictionary<string, VectorSave> WeaponManagerVectors { get; set; } = new();
        public Dictionary<string, string> PlayerMoveState { get; set; } = new();
        public Dictionary<string, VectorSave> PlayerMoveVectors { get; set; } = new();
        public Dictionary<string, string> ExpManagerState { get; set; } = new();
        public Dictionary<string, VectorSave> ExpManagerVectors { get; set; } = new();
        public bool ExpOrbDataValid { get; set; }
        public List<ExpOrbSave> ExpOrbs { get; set; } = new();
        public Dictionary<string, string> MusicManagerState { get; set; } = new();
        public Dictionary<string, VectorSave> MusicManagerVectors { get; set; } = new();
        public Dictionary<string, string> GeneralAudioState { get; set; } = new();
        public Dictionary<string, VectorSave> GeneralAudioVectors { get; set; } = new();
        public bool RunSessionStatsValid { get; set; }
        public RunSessionStatsSave RunSessionStats { get; set; } = new();
        public RandomStateSave RandomState { get; set; }
        public EnemyRelationshipsSave EnemyRelationships { get; set; } = new();
        public List<AudioSourceSave> AudioSources { get; set; } = new();
        public List<AnimatorSave> PlayerAnimators { get; set; } = new();
        public List<SnareSave> Snares { get; set; } = new();
        public int FirstSnareIndex { get; set; } = -1;
        public List<EnemySave> Enemies { get; set; } = new();
        public List<WorldObjectSave> WorldObjects { get; set; } = new();
    }

    public sealed class RunSessionStatsSave
    {
        public int HitsTaken { get; set; }
        public int HeartsGained { get; set; }
        public long TotalDamageDealt { get; set; }
        public int BulletsFired { get; set; }
        public int HighestDamageNumber { get; set; }
    }

    public sealed class EnemySave
    {
        public string PoolId { get; set; } = string.Empty;
        public string PrefabName { get; set; } = string.Empty;
        public bool IsMiniBoss { get; set; }
        public int MiniBossNumber { get; set; }
        public bool IsAdEnemy { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; } = 1f;
        public float Health { get; set; }
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; set; }
        public int ScoreWorth { get; set; }
        public int ExperienceLevel { get; set; }
        public float DamageMultiplier { get; set; }
        public float Speed { get; set; }
        public float BaseSpeed { get; set; }
        public float CurrentMovementSpeed { get; set; }
        public bool CanShoot { get; set; }
        public float ShootCooldown { get; set; }
        public VectorSave ShockVisualWorldScale { get; set; }
        public Dictionary<string, string> State { get; set; } = new();
        public Dictionary<string, VectorSave> Vectors { get; set; } = new();
        public List<int> BulletNumbers { get; set; } = new();
        public List<int> ShockDamageEnemyIndices { get; set; } = new();
        public List<AudioSourceSave> AudioSources { get; set; } = new();
        public List<AnimatorSave> Animators { get; set; } = new();
    }

    public sealed class VectorSave
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public sealed class SnareSave
    {
        public bool Active { get; set; }
        public bool ParticleActive { get; set; }
        public bool ColliderEnabled { get; set; }
        public float EnableTimer { get; set; }
        public float ColliderEnableTimer { get; set; }
        public bool DisableObjectBuffer { get; set; }
        public bool SelectedForDash { get; set; }
        public VectorSave LocalPosition { get; set; } = new();
        public float LocalRotationX { get; set; }
        public float LocalRotationY { get; set; }
        public float LocalRotationZ { get; set; }
        public float LocalRotationW { get; set; } = 1f;
    }

    public sealed class WorldObjectSave
    {
        public string Kind { get; set; } = string.Empty;
        public int Variant { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; } = 1f;
        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public float ScaleZ { get; set; } = 1f;
        public List<ComponentSave> Components { get; set; } = new();
        public List<ChildSave> Children { get; set; } = new();
        public List<RigidbodySave> Rigidbodies { get; set; } = new();
        public List<EnemyObjectReferenceSave> PierceTargets { get; set; } = new();
        public List<EnemyObjectReferenceSave> LaserTargets { get; set; } = new();
        public List<int> FrostSawTargets { get; set; } = new();
        public List<int> ExecutionSawTargets { get; set; } = new();
        public EnemyObjectReferenceSave ImmuneEnemy { get; set; }
        public List<AudioSourceSave> AudioSources { get; set; } = new();
        public List<AnimatorSave> Animators { get; set; } = new();
    }

    public sealed class RandomStateSave
    {
        public int S0 { get; set; }
        public int S1 { get; set; }
        public int S2 { get; set; }
        public int S3 { get; set; }
    }

    public sealed class ExpOrbSave
    {
        public VectorSave Position { get; set; } = new();
        public VectorSave Velocity { get; set; } = new();
        public float Speed { get; set; }
        public float Age { get; set; }
        public int Level { get; set; }
        public int Value { get; set; }
        public bool CanFloat { get; set; }
        public bool IsShinedOn { get; set; }
    }

    public sealed class EnemyRelationshipsSave
    {
        public List<EnemyObjectReferenceSave> ShockTargets { get; set; } = new();
        public List<int> ExecutionStrikeTargets { get; set; } = new();
        public List<int> BlightTrackedTargets { get; set; } = new();
        public List<int> VisibleTargets { get; set; } = new();
        public List<int> RadiationTargets { get; set; } = new();
        public EnemyObjectReferenceSave ParriedObject { get; set; }
    }

    public sealed class AudioSourceSave
    {
        public string Owner { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public string ClipName { get; set; } = string.Empty;
        public int TimeSamples { get; set; }
        public float Time { get; set; }
        public bool WasPlaying { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Loop { get; set; }
        public bool Mute { get; set; }
        public bool PlayOnAwake { get; set; }
        public float Volume { get; set; }
        public float Pitch { get; set; } = 1f;
        public float SpatialBlend { get; set; }
    }

    public sealed class AnimatorSave
    {
        public string Owner { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public bool Enabled { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public List<AnimatorLayerSave> Layers { get; set; } = new();
    }

    public sealed class AnimatorLayerSave
    {
        public int StateHash { get; set; }
        public float NormalizedTime { get; set; }
        public float Weight { get; set; }
    }

    public sealed class ComponentSave
    {
        public string Path { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public bool Enabled { get; set; } = true;
        public Dictionary<string, string> State { get; set; } = new();
        public Dictionary<string, VectorSave> Vectors { get; set; } = new();
    }

    public sealed class ChildSave
    {
        public string Path { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    public sealed class RigidbodySave
    {
        public string Path { get; set; } = string.Empty;
        public bool IsKinematic { get; set; }
        public VectorSave Velocity { get; set; } = new();
        public VectorSave AngularVelocity { get; set; } = new();
    }

    public sealed class EnemyObjectReferenceSave
    {
        public int EnemyIndex { get; set; } = -1;
        public string Path { get; set; } = string.Empty;
    }

    private sealed class EnemyPlacement
    {
        public Enemy Enemy { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        public EnemyPlacement(Enemy enemy, Vector3 position, Quaternion rotation)
        {
            Enemy = enemy;
            Position = position;
            Rotation = rotation;
        }
    }
}

internal static class CheckpointUi
{
    internal const string SaveButtonName = "RunCheckpoint_SaveRun";
    internal const string ResumeButtonName = "RunCheckpoint_ResumeRun";
    private static GameObject saveButton;
    private static GameObject pauseMenu;
    private static GameObject resumeButton;
    private static readonly Dictionary<Transform, Vector3> soloRows = new();
    private static Vector3 rowOffset;
    private static GameObject loadingOverlay;
    private static GameObject readyPrompt;
    private static Text readyPromptText;

    internal static void ShowLoadingOverlay()
    {
        HideLoadingOverlay();
        try
        {
            Mainmenu main = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Mainmenu>())
            {
                if (candidate?.gameObject.activeInHierarchy == true)
                {
                    main = candidate;
                    break;
                }
            }
            if (main == null) throw new InvalidOperationException("Main menu was unavailable.");
            var template = MonoFields.Blackfadeinscreen(main) ?? MonoFields.Black(main)?.gameObject;
            var rootCanvas = template?.GetComponentInParent<Canvas>()?.rootCanvas;
            if (template == null || rootCanvas == null) throw new InvalidOperationException("Native black screen was unavailable.");

            loadingOverlay = UnityEngine.Object.Instantiate(template, template.transform.parent);
            loadingOverlay.name = "RunCheckpoint_LoadingOverlay";
            loadingOverlay.SetActive(true);
            foreach (var animator in loadingOverlay.GetComponentsInChildren<Animator>(true)) if (animator != null) animator.enabled = false;
            foreach (var group in loadingOverlay.GetComponentsInChildren<CanvasGroup>(true)) if (group != null) group.alpha = 1f;
            foreach (var image in loadingOverlay.GetComponentsInChildren<Image>(true)) if (image != null) image.color = Color.black;

            Text sourceLabel = null;
            foreach (var source in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (source?.gameObject.activeInHierarchy == true && source.font != null)
                {
                    sourceLabel = source;
                    break;
                }
            }
            if (sourceLabel != null)
            {
                var labelParent = loadingOverlay.GetComponentInChildren<Image>(true)?.transform ?? loadingOverlay.transform;
                var labelObject = UnityEngine.Object.Instantiate(sourceLabel.gameObject, labelParent);
                labelObject.name = "Label";
                SetLabel(labelObject, "SAVE LOADING");
                var label = labelObject.GetComponent<Text>() ?? labelObject.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.fontSize = 48;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.color = Color.white;
                    label.raycastTarget = false;
                }
                var labelRect = labelObject.transform as RectTransform;
                if (labelRect != null)
                {
                    labelRect.localScale = Vector3.one;
                    labelRect.anchorMin = new Vector2(0.25f, 0.4f);
                    labelRect.anchorMax = new Vector2(0.75f, 0.6f);
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;
                }
            }
            loadingOverlay.transform.SetSiblingIndex(Math.Min(template.transform.GetSiblingIndex() + 1, loadingOverlay.transform.parent.childCount - 1));
        }
        catch
        {
            HideLoadingOverlay();
            throw;
        }
    }

    internal static void HideLoadingOverlay()
    {
        if (loadingOverlay != null) UnityEngine.Object.Destroy(loadingOverlay);
        loadingOverlay = null;
        if (readyPrompt != null) UnityEngine.Object.Destroy(readyPrompt);
        readyPrompt = null;
        readyPromptText = null;
    }

    internal static void ShowReadyPrompt()
    {
        if (loadingOverlay != null)
        {
            foreach (var image in loadingOverlay.GetComponentsInChildren<Image>(true))
            {
                if (image == null) continue;
                image.color = Color.clear;
                image.raycastTarget = false;
            }
        }
        var scene = UnityEngine.Object.FindObjectOfType<SceneManagement>();
        if (scene?.hud == null)
        {
            RunCheckpointController.Logger?.LogWarning("Ready prompt could not find the gameplay HUD.");
            return;
        }

        Text source = null;
        foreach (var candidate in scene.hud.GetComponentsInChildren<Text>(true))
        {
            if (candidate == null || candidate.font == null) continue;
            if (string.Equals(candidate.gameObject.name, "Timer", StringComparison.OrdinalIgnoreCase))
            {
                source = candidate;
                break;
            }
            if (source == null && candidate.gameObject.activeInHierarchy) source = candidate;
        }
        if (source == null)
        {
            RunCheckpointController.Logger?.LogWarning("Ready prompt could not find a HUD text template.");
            return;
        }

        readyPrompt = UnityEngine.Object.Instantiate(source.gameObject, scene.hud.transform);
        readyPrompt.name = "RunCheckpoint_ReadyPrompt";
        readyPromptText = readyPrompt.GetComponent<Text>();
        foreach (var behaviour in readyPrompt.GetComponents<Behaviour>())
        {
            if (behaviour != null && behaviour != readyPromptText) behaviour.enabled = false;
        }
        readyPromptText.fontSize = 36;
        readyPromptText.alignment = TextAnchor.MiddleCenter;
        readyPromptText.horizontalOverflow = HorizontalWrapMode.Overflow;
        readyPromptText.verticalOverflow = VerticalWrapMode.Overflow;
        readyPromptText.raycastTarget = false;
        var rect = readyPromptText.rectTransform;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 70f);
        rect.sizeDelta = new Vector2(1600f, 80f);
        MaintainReadyPrompt();
        Canvas.ForceUpdateCanvases();
        RunCheckpointController.Logger?.LogInfo($"Ready prompt created on the gameplay HUD from {source.gameObject.name}; active={readyPrompt.activeInHierarchy}, alpha={readyPromptText.canvasRenderer.GetAlpha():0.00}.");
    }

    internal static void MaintainReadyPrompt()
    {
        if (readyPromptText == null) return;
        readyPromptText.gameObject.SetActive(true);
        readyPromptText.enabled = true;
        readyPromptText.text = "PRESS ANY BUTTON TO RESUME";
        readyPromptText.color = Color.white;
        readyPromptText.canvasRenderer.SetAlpha(1f);
        readyPromptText.transform.SetAsLastSibling();
    }

    internal static void AddPauseButton(SceneManagement scene)
    {
        try
        {
            if (RunCheckpointController.MultiplayerActive) return;
            var buttons = scene.pausemenu?.GetComponentsInChildren<Button>(true);
            if (buttons == null || buttons.Length == 0)
            {
                RunCheckpointController.Logger?.LogWarning("Pause menu has no button to clone.");
                return;
            }

            pauseMenu = scene.pausemenu;
            saveButton = CreateButton(buttons[0], SaveButtonName, "SAVE & QUIT", true);
            if (saveButton == null) return;
            var buttonObject = saveButton;
            buttonObject.GetComponent<Button>().onClick.AddListener(() =>
            {
                RunCheckpointController.Save();
                SetLabel(buttonObject, RunCheckpointController.LastMessage);
            });
            UpdateLobbyVisibility();
            RunCheckpointController.Logger?.LogInfo("Save Run button added to the pause menu.");
        }
        catch (Exception ex)
        {
            RunCheckpointController.Logger?.LogError($"Could not add pause Save Run button: {ex}");
        }
    }

    internal static void AddResumeButton(Mainmenu menu)
    {
        try
        {
            if (!RunCheckpointController.HasCompatibleSave())
            {
                return;
            }

            var leaderboard = MonoFields.LeaderboardsButton(menu);
            var play = menu.transform.Find("Play")?.GetComponent<Button>();
            if (leaderboard == null || play == null)
            {
                RunCheckpointController.Logger?.LogWarning("Main menu Play or Leaderboards button was unavailable.");
                return;
            }

            rowOffset = leaderboard.transform.position - play.transform.position;
            var playScreenY = ScreenY(play.transform);
            soloRows.Clear();
            foreach (var button in menu.GetComponentsInChildren<Button>(true))
            {
                if (button == play || (button.gameObject.activeInHierarchy && button.GetComponent<ButtonControl>() != null
                    && !IsWarningButton(menu, button) && ScreenY(button.transform) < playScreenY))
                {
                    soloRows.Add(button.transform, button.transform.position);
                }
            }

            resumeButton = UnityEngine.Object.Instantiate(play.gameObject, play.transform.parent);
            resumeButton.name = ResumeButtonName;
            SetNativeLabel(resumeButton, "RESUME RUN");
            var control = play.GetComponent<ButtonControl>();
            var buttonComponent = resumeButton.GetComponent<Button>();
            buttonComponent.onClick = new Button.ButtonClickedEvent();
            buttonComponent.onClick.AddListener(() =>
            {
                if (RunCheckpointController.BeginResume()) control.OPENstart();
            });
            resumeButton.SetActive(false);
            UpdateLobbyVisibility();
            RunCheckpointController.Logger?.LogInfo("Resume Run button added to the main menu.");
        }
        catch (Exception ex)
        {
            RunCheckpointController.Logger?.LogError($"Could not customize the main menu: {ex}");
        }
    }

    internal static void UpdateLobbyVisibility()
    {
        if (saveButton == null && resumeButton == null) return;
        var solo = !RunCheckpointController.MultiplayerActive;
        if (saveButton != null)
        {
            var visible = solo && pauseMenu != null && pauseMenu.activeInHierarchy;
            if (saveButton.activeSelf != visible) saveButton.SetActive(visible);
        }
        if (resumeButton == null || resumeButton.activeSelf == solo) return;

        foreach (var row in soloRows)
            if (row.Key != null) row.Key.position = row.Value + (solo ? rowOffset : Vector3.zero);
        resumeButton.SetActive(solo);
    }

    private static bool IsWarningButton(Mainmenu menu, Button button) =>
        MonoFields.Flashinglightswarningscreen(menu) != null && button.transform.IsChildOf(MonoFields.Flashinglightswarningscreen(menu).transform);

    private static float ScreenY(Transform transform)
    {
        var canvas = transform.GetComponentInParent<Canvas>()?.rootCanvas;
        return RectTransformUtility.WorldToScreenPoint(canvas?.worldCamera ?? Camera.main, transform.position).y;
    }

    private static void SetNativeLabel(GameObject buttonObject, string text)
    {
        foreach (var legacy in buttonObject.GetComponentsInChildren<Text>(true))
        {
            legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacy.verticalOverflow = VerticalWrapMode.Overflow;
            legacy.text = text;
        }
        foreach (var tmp in buttonObject.GetComponentsInChildren<TMP_Text>(true))
        {
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.text = text;
        }
    }

    internal static void SetLabel(GameObject buttonObject, string text)
    {
        foreach (var legacy in buttonObject.GetComponentsInChildren<Text>(true))
        {
            legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacy.verticalOverflow = VerticalWrapMode.Overflow;
            legacy.alignment = TextAnchor.MiddleCenter;
            legacy.color = Color.white;
            legacy.text = text;

            var outline = legacy.GetComponent<Outline>() ?? legacy.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
        }
        foreach (var tmp in buttonObject.GetComponentsInChildren<TMP_Text>(true))
        {
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.outlineColor = new Color32(0, 0, 0, 255);
            tmp.outlineWidth = 0.18f;
            tmp.text = text;
        }
    }

    private static GameObject CreateButton(Button template, string name, string label, bool interactable)
    {
        var rootCanvas = template.GetComponentInParent<Canvas>()?.rootCanvas
            ?? UnityEngine.Object.FindObjectOfType<Canvas>()?.rootCanvas;
        var canvasRect = rootCanvas?.transform as RectTransform;
        if ((canvasRect?.Find(name) ?? template.transform.parent?.Find(name)) != null)
        {
            RunCheckpointController.Logger?.LogWarning($"Could not add {name}: a button with that name already exists.");
            return null;
        }

        var buttonObject = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
        buttonObject.name = name;
        buttonObject.SetActive(false);

        var rect = buttonObject.transform as RectTransform;
        var button = buttonObject.GetComponent<Button>();
        if (button == null)
        {
            RunCheckpointController.Logger?.LogWarning($"Could not add {name}: cloned object was missing its Button.");
            UnityEngine.Object.Destroy(buttonObject);
            return null;
        }

        button.onClick = new Button.ButtonClickedEvent();
        button.interactable = interactable;
        SetLabel(buttonObject, label);

        var control = buttonObject.GetComponent<ButtonControl>();
        if (control != null)
        {
            if (MonoFields.Border(control) != null && MonoFields.Border(control).transform.IsChildOf(buttonObject.transform))
            {
                MonoFields.Border(control).SetActive(false);
            }
            UnityEngine.Object.DestroyImmediate(control);
        }

        foreach (var animator in buttonObject.GetComponentsInChildren<Animator>(true))
        {
            UnityEngine.Object.DestroyImmediate(animator);
        }

        var fitter = buttonObject.GetComponent<ContentSizeFitter>();
        if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);
        var layout = buttonObject.GetComponent<LayoutElement>();
        if (layout != null) UnityEngine.Object.DestroyImmediate(layout);

        PlaceButton(template, buttonObject);
        return buttonObject;
    }

    private static void PlaceButton(Button template, GameObject buttonObject)
    {
        buttonObject.SetActive(true);
        Canvas.ForceUpdateCanvases();

        var templateRect = template.transform as RectTransform;
        var rect = buttonObject.transform as RectTransform;
        var rootCanvas = template.GetComponentInParent<Canvas>()?.rootCanvas
            ?? UnityEngine.Object.FindObjectOfType<Canvas>()?.rootCanvas;
        var canvasRect = rootCanvas?.transform as RectTransform;
        if (canvasRect == null || templateRect == null || rect == null)
        {
            var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (camera == null) throw new InvalidOperationException("Menu camera was unavailable.");

            var sourcePoint = camera.WorldToScreenPoint(template.transform.position);
            var bottom = buttonObject.name == ResumeButtonName ? 130f : 60f;
            var menuHost = template.transform.parent?.parent ?? template.transform.parent ?? template.transform.root;
            buttonObject.transform.SetParent(menuHost, true);
            buttonObject.transform.position = camera.ScreenToWorldPoint(new Vector3(sourcePoint.x, bottom, sourcePoint.z));
            buttonObject.transform.SetAsLastSibling();
            var finalScreenPoint = camera.WorldToScreenPoint(buttonObject.transform.position);
            RunCheckpointController.Logger?.LogInfo($"Placed {buttonObject.name} in world UI: screen=({finalScreenPoint.x:0},{finalScreenPoint.y:0}), active={buttonObject.activeInHierarchy}.");
            return;
        }

        var sourceText = template.GetComponentInChildren<Text>(true);
        var screenPoint = RectTransformUtility.WorldToScreenPoint(rootCanvas!.worldCamera, templateRect.position);
        var horizontalAnchor = Screen.width > 0 ? Mathf.Clamp01(screenPoint.x / Screen.width) : 0.5f;
        var worldScale = templateRect.lossyScale;
        var canvasScale = canvasRect.lossyScale;
        var width = Math.Max(templateRect.rect.width * Math.Abs(worldScale.x / canvasScale.x), (sourceText?.preferredWidth ?? 120f) + 32f);
        var height = Math.Max(templateRect.rect.height * Math.Abs(worldScale.y / canvasScale.y), (sourceText?.preferredHeight ?? 32f) + 16f);

        rect.SetParent(canvasRect, false);
        rect.anchorMin = new Vector2(horizontalAnchor, 0f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, (float)width);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (float)height);
        var bottomMargin = buttonObject.name == ResumeButtonName ? 100f : 24f;
        rect.anchoredPosition = new Vector2(0f, bottomMargin + (float)height * 0.5f);
        rect.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();

        var finalPoint = RectTransformUtility.WorldToScreenPoint(rootCanvas.worldCamera, rect.position);
        RunCheckpointController.Logger?.LogInfo($"Placed {buttonObject.name}: screen=({finalPoint.x:0},{finalPoint.y:0}), size=({rect.rect.width:0},{rect.rect.height:0}), canvas=({canvasRect.rect.width:0},{canvasRect.rect.height:0}), active={buttonObject.activeInHierarchy}.");
    }
}

[HarmonyPatch(typeof(SceneManagement), "Start")]
internal static class PauseButtonPatch
{
    private static void Postfix(SceneManagement __instance)
    {
        CheckpointUi.AddPauseButton(__instance);
    }
}

[HarmonyPatch(typeof(Mainmenu), "Start")]
internal static class ResumeButtonPatch
{
    private static void Prefix() => RunCheckpointController.EndCheckpointTestMode();

    private static void Postfix(Mainmenu __instance)
    {
        CheckpointUi.AddResumeButton(__instance);
    }
}

[HarmonyPatch]
internal static class DisableLeaderboardScorePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(LeadMan), nameof(LeadMan.UpdateScore));
        yield return AccessTools.Method(typeof(LeadMan), nameof(LeadMan.UpdateScoreAlways));
    }

    private static bool Prefix() => RunCheckpointController.MultiplayerActive;
}

[HarmonyPatch(typeof(SceneManagement), "FadingThenLoadLoadingScreen")]
internal static class CheckpointExitPatch
{
    private static void Prefix(ref SceneManagement.PostFadeRunEndIoMode postFadeIo)
    {
        if (RunCheckpointController.ReturningFromCheckpoint)
            postFadeIo = SceneManagement.PostFadeRunEndIoMode.None;
    }
}
