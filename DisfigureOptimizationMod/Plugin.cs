using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using FSG.MeshAnimator;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DisfigureOptimizationMod;

[BepInPlugin("casto.disfigure.optimization-mod", "Disfigure Optimization Mod", "0.3.4")]
public sealed class Plugin : BasePlugin
{
    private ConfigFile? modConfig;
    private Harmony? harmony;

    public override void Load()
    {
        SelfCheck.Run();
        modConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, "DisfigureOptimizationMod.cfg"), true);
        var settings = new Settings(
            modConfig.Bind("General", "EnablePlugin", true,
                "Master switch for the optimization mod."),
            modConfig.Bind("ScreenEffects", "DisablePostProcessing", true,
                "Disable scene volumes and URP camera post-processing."),
            modConfig.Bind("EnemyVisuals", "FreezeMeshAnimations", false,
                "Experimental: freeze custom enemy mesh animation. Animation-driven events may be affected."),
            modConfig.Bind("EnemyVisuals", "FreezeUnityAnimators", false,
                "Experimental: also freeze Unity Animators on enemies. This may suppress animation events."),
            modConfig.Bind("EnemyVisuals", "HideEnemyRenderers", false,
                "Diagnostic: hide enemy renderers without disabling enemy gameplay objects."),
            modConfig.Bind("EnemyVisuals", "DisableEnemyShadows", true,
                "Disable shadow casting, receiving, and motion vectors on enemy renderers."),
            modConfig.Bind("ProjectileVisuals", "DisableProjectileShadows", true,
                "Disable shadow casting, receiving, and motion vectors on player and enemy projectiles."),
            modConfig.Bind("ProjectileVisuals", "DisableTrailsAtBulletCount", 1,
                "Disable projectile TrailRenderers after total live projectiles reach this count; 0 disables this option."),
            modConfig.Bind("Diagnostics", "EnableDiagnostics", false,
                "Log FPS, worst frame time, and cheap built-in enemy/projectile counts for stress testing."),
            modConfig.Bind("Diagnostics", "InvulnerableForTesting", false,
                "Diagnostic: allow taking hits without dying."),
            modConfig.Bind("Diagnostics", "LogIntervalSeconds", 5f,
                "Seconds averaged by each diagnostic line; minimum 1."));

        OptimizationController.Configure(settings, Log);
        harmony = new Harmony("casto.disfigure.optimization-mod");
        harmony.PatchAll(typeof(Plugin).Assembly);
        AddComponent<OptimizationController>();
        Log.LogInfo(settings.Enabled.Value
            ? "Disfigure Optimization Mod loaded active."
            : "Disfigure Optimization Mod loaded inert. Set EnablePlugin=true in its config to opt in.");
    }
}

internal sealed class Settings
{
    internal Settings(ConfigEntry<bool> enabled, ConfigEntry<bool> disablePostProcessing,
        ConfigEntry<bool> freezeMeshAnimations, ConfigEntry<bool> freezeUnityAnimators,
        ConfigEntry<bool> hideEnemyRenderers, ConfigEntry<bool> disableEnemyShadows,
        ConfigEntry<bool> disableProjectileShadows, ConfigEntry<int> disableTrailsAtBulletCount,
        ConfigEntry<bool> enableDiagnostics, ConfigEntry<bool> invulnerableForTesting,
        ConfigEntry<float> diagnosticInterval)
    {
        Enabled = enabled;
        DisablePostProcessing = disablePostProcessing;
        FreezeMeshAnimations = freezeMeshAnimations;
        FreezeUnityAnimators = freezeUnityAnimators;
        HideEnemyRenderers = hideEnemyRenderers;
        DisableEnemyShadows = disableEnemyShadows;
        DisableProjectileShadows = disableProjectileShadows;
        DisableTrailsAtBulletCount = disableTrailsAtBulletCount;
        EnableDiagnostics = enableDiagnostics;
        InvulnerableForTesting = invulnerableForTesting;
        DiagnosticInterval = diagnosticInterval;
    }

    internal ConfigEntry<bool> Enabled { get; }
    internal ConfigEntry<bool> DisablePostProcessing { get; }
    internal ConfigEntry<bool> FreezeMeshAnimations { get; }
    internal ConfigEntry<bool> FreezeUnityAnimators { get; }
    internal ConfigEntry<bool> HideEnemyRenderers { get; }
    internal ConfigEntry<bool> DisableEnemyShadows { get; }
    internal ConfigEntry<bool> DisableProjectileShadows { get; }
    internal ConfigEntry<int> DisableTrailsAtBulletCount { get; }
    internal ConfigEntry<bool> EnableDiagnostics { get; }
    internal ConfigEntry<bool> InvulnerableForTesting { get; }
    internal ConfigEntry<float> DiagnosticInterval { get; }
}

public sealed class OptimizationController : MonoBehaviour
{
    private static Settings? settings;
    private static ManualLogSource? logger;
    private static int activeProjectiles;
    private static bool projectileTrailsDisabled;

    private int sceneHandle = int.MinValue;
    private bool reportedChanges;
    private bool diagnosticInvulnerabilityApplied;
    private float diagnosticElapsed;
    private float diagnosticWorstFrame;
    private int diagnosticFrames;

    internal static void Configure(Settings value, ManualLogSource log)
    {
        settings = value;
        logger = log;
    }

    private void Update()
    {
        if (settings?.Enabled.Value != true)
        {
            sceneHandle = int.MinValue;
            diagnosticInvulnerabilityApplied = false;
            return;
        }

        if (settings.EnableDiagnostics.Value) RecordDiagnostics();

        var currentSceneHandle = SceneManager.GetActiveScene().handle;
        if (currentSceneHandle != sceneHandle)
        {
            sceneHandle = currentSceneHandle;
            diagnosticInvulnerabilityApplied = false;
            InitializeScene();
        }

        if (settings.InvulnerableForTesting.Value && !diagnosticInvulnerabilityApplied)
        {
            var weaponManager = GameRefs.Instance?.weaponManager;
            if (weaponManager != null)
            {
                weaponManager.takeDamageButNoDying = true;
                diagnosticInvulnerabilityApplied = true;
                logger?.LogInfo("Diagnostic invulnerability active.");
            }
        }
    }

    private void InitializeScene()
    {
        try
        {
            activeProjectiles = 0;
            projectileTrailsDisabled = false;
            var changed = settings!.DisablePostProcessing.Value ? DisablePostProcessing() : 0;

            foreach (var enemy in Resources.FindObjectsOfTypeAll<Enemy>())
                if (IsActiveSceneObject(enemy)) changed += OptimizeEnemy(enemy);

            var playerProjectiles = Resources.FindObjectsOfTypeAll<Projectile>();
            var enemyProjectiles = Resources.FindObjectsOfTypeAll<ShooterProjectile>();
            activeProjectiles = CountActive(playerProjectiles) + CountActive(enemyProjectiles);
            projectileTrailsDisabled = SelfCheck.ShouldDisableTrails(activeProjectiles,
                Math.Max(0, settings.DisableTrailsAtBulletCount.Value));
            foreach (var projectile in playerProjectiles)
                if (IsActiveSceneObject(projectile)) changed += OptimizeProjectile(projectile);
            foreach (var projectile in enemyProjectiles)
                if (IsActiveSceneObject(projectile)) changed += OptimizeProjectile(projectile);

            if (changed > 0 && !reportedChanges)
            {
                reportedChanges = true;
                logger?.LogInfo($"Optimization active; changed {changed} visual components during scene initialization.");
            }
        }
        catch (Exception exception)
        {
            logger?.LogWarning($"Optimization scene initialization failed safely: {exception.Message}");
        }
    }

    private static int DisablePostProcessing()
    {
        var changed = 0;
        foreach (var volume in Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.Volume>())
        {
            if (!IsActiveSceneObject(volume) || !volume.enabled) continue;
            volume.enabled = false;
            changed++;
        }

        foreach (var cameraData in Resources.FindObjectsOfTypeAll<UniversalAdditionalCameraData>())
        {
            if (!IsActiveSceneObject(cameraData) || !cameraData.renderPostProcessing) continue;
            cameraData.renderPostProcessing = false;
            changed++;
        }

        return changed;
    }

    internal static void OnEnemyEnabled(Enemy enemy)
    {
        if (settings?.Enabled.Value == true && IsActiveSceneObject(enemy)) OptimizeEnemy(enemy);
    }

    private static int OptimizeEnemy(Enemy enemy)
    {
        if (settings == null) return 0;
        var changed = 0;

        if (settings.HideEnemyRenderers.Value || settings.DisableEnemyShadows.Value)
            foreach (var renderer in enemy.GetComponentsInChildren<Renderer>(true))
            {
                if (settings.HideEnemyRenderers.Value)
                {
                    if (!renderer.enabled) continue;
                    renderer.enabled = false;
                    changed++;
                }
                else
                {
                    changed += DisableShadows(renderer);
                }
            }

        if (settings.FreezeMeshAnimations.Value)
        {
            foreach (var animator in enemy.GetComponentsInChildren<MeshAnimatorBase>(true))
            {
                if (!animator.enabled || !animator.gameObject.activeInHierarchy) continue;
                animator.Pause();
                animator.enabled = false;
                changed++;
            }
        }

        if (settings.FreezeUnityAnimators.Value)
        {
            foreach (var animator in enemy.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.enabled || !animator.gameObject.activeInHierarchy) continue;
                animator.enabled = false;
                changed++;
            }
        }

        return changed;
    }

    internal static void OnProjectileEnabled(Component projectile)
    {
        if (settings?.Enabled.Value != true || !IsActiveSceneObject(projectile)) return;
        activeProjectiles++;
        var threshold = Math.Max(0, settings.DisableTrailsAtBulletCount.Value);
        if (!projectileTrailsDisabled && SelfCheck.ShouldDisableTrails(activeProjectiles, threshold))
        {
            projectileTrailsDisabled = true;
            OptimizeAllActiveProjectiles();
        }
        else
        {
            OptimizeProjectile(projectile);
        }
    }

    internal static void OnProjectileDisabled()
    {
        if (settings?.Enabled.Value == true) activeProjectiles = Math.Max(0, activeProjectiles - 1);
    }

    private static void OptimizeAllActiveProjectiles()
    {
        foreach (var projectile in Resources.FindObjectsOfTypeAll<Projectile>())
            if (IsActiveSceneObject(projectile)) OptimizeProjectile(projectile);
        foreach (var projectile in Resources.FindObjectsOfTypeAll<ShooterProjectile>())
            if (IsActiveSceneObject(projectile)) OptimizeProjectile(projectile);
    }

    private static int OptimizeProjectile(Component projectile)
    {
        if (settings == null) return 0;
        var changed = 0;
        foreach (var renderer in projectile.GetComponentsInChildren<Renderer>(true))
        {
            if (settings.DisableProjectileShadows.Value) changed += DisableShadows(renderer);
            var trail = renderer.TryCast<TrailRenderer>();
            if (projectileTrailsDisabled && trail != null && trail.enabled)
            {
                trail.enabled = false;
                changed++;
            }
        }
        return changed;
    }

    private static int DisableShadows(Renderer renderer)
    {
        var changed = 0;
        if (renderer.shadowCastingMode != ShadowCastingMode.Off)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            changed++;
        }
        if (renderer.receiveShadows)
        {
            renderer.receiveShadows = false;
            changed++;
        }
        if (renderer.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion)
        {
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            changed++;
        }
        return changed;
    }

    private void RecordDiagnostics()
    {
        var gm = GameManager.instance;
        if (gm == null)
        {
            diagnosticElapsed = 0f;
            diagnosticWorstFrame = 0f;
            diagnosticFrames = 0;
            return;
        }

        var frame = Time.unscaledDeltaTime;
        diagnosticElapsed += frame;
        diagnosticWorstFrame = Math.Max(diagnosticWorstFrame, frame);
        diagnosticFrames++;
        if (diagnosticElapsed < Math.Max(1f, settings!.DiagnosticInterval.Value)) return;

        var fps = diagnosticFrames / diagnosticElapsed;
        logger?.LogInfo($"PERF avgFps={fps:0.0} worstMs={diagnosticWorstFrame * 1000f:0.0} "
            + $"enemies={gm.enemiesOnScreen} playerBullets={GameRefs.Instance?.weaponManager?.bulletsOnScreen ?? 0} "
            + $"activeProjectiles={activeProjectiles}");
        diagnosticElapsed = 0f;
        diagnosticWorstFrame = 0f;
        diagnosticFrames = 0;
    }

    private static int CountActive<T>(Il2CppArrayBase<T> components) where T : Component
    {
        var count = 0;
        foreach (var component in components)
            if (IsActiveSceneObject(component)) count++;
        return count;
    }

    private static bool IsActiveSceneObject(Component component) =>
        component != null && component.gameObject != null && component.gameObject.scene.IsValid()
        && component.gameObject.activeInHierarchy;
}

[HarmonyPatch(typeof(Enemy), "OnEnable")]
internal static class EnemyEnabledPatch
{
    [HarmonyPostfix]
    private static void Postfix(Enemy __instance) => OptimizationController.OnEnemyEnabled(__instance);
}

[HarmonyPatch(typeof(Projectile), "OnEnable")]
internal static class PlayerProjectileEnabledPatch
{
    [HarmonyPostfix]
    private static void Postfix(Projectile __instance) => OptimizationController.OnProjectileEnabled(__instance);
}

[HarmonyPatch(typeof(Projectile), "OnDisable")]
internal static class PlayerProjectileDisabledPatch
{
    [HarmonyPostfix]
    private static void Postfix() => OptimizationController.OnProjectileDisabled();
}

[HarmonyPatch(typeof(ShooterProjectile), "OnEnable")]
internal static class EnemyProjectileEnabledPatch
{
    [HarmonyPostfix]
    private static void Postfix(ShooterProjectile __instance) => OptimizationController.OnProjectileEnabled(__instance);
}

[HarmonyPatch(typeof(ShooterProjectile), "OnDisable")]
internal static class EnemyProjectileDisabledPatch
{
    [HarmonyPostfix]
    private static void Postfix() => OptimizationController.OnProjectileDisabled();
}

internal static class SelfCheck
{
    internal static bool ShouldDisableTrails(int activeProjectiles, int threshold) =>
        threshold > 0 && activeProjectiles >= threshold;

    internal static void Run()
    {
        if (ShouldDisableTrails(100, 0) || ShouldDisableTrails(99, 100)
            || !ShouldDisableTrails(100, 100))
            throw new InvalidOperationException("Optimization mod self-check failed.");
    }
}
