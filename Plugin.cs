using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace DisfigureTranslationMod;

[BepInPlugin("casto.disfigure.translation-mod", "DisfigureTranslationMod", "0.7.9")]
public sealed class Plugin : BasePlugin
{
    private ConfigFile? modConfig;
    private Harmony? harmony;

    public override void Load()
    {
        var configPath = Path.Combine(Paths.ConfigPath, "DisfigureTranslationMod.cfg");
        modConfig = new ConfigFile(configPath, true);
        var settings = new PluginSettings(
            modConfig.Bind("General", "EnablePlugin", true, "Enable runtime text discovery and replacement."),
            modConfig.Bind("Detection", "EnableStringDetection", true, "Export newly observed source strings."),
            modConfig.Bind("Translation", "EnableTranslationReplacement", true, "Replace matching visible text."),
            modConfig.Bind("Scanning", "ScanIntervalSeconds", 1f, "Seconds between active text scans (minimum 0.25)."),
            modConfig.Bind("Detection", "LogNewlyDetectedStrings", false, "Log each newly exported source string."),
            modConfig.Bind("Translation", "TranslationFilename", "en.json", "JSON dictionary loaded from the mod translation directory."),
            modConfig.Bind("Hotkeys", "ReloadTranslations", Key.F5, "Reload the translation JSON file."),
            modConfig.Bind("Hotkeys", "ForceRescan", Key.F4, "Immediately scan visible text."),
            modConfig.Bind("Hotkeys", "LanguageMenu", Key.F8, "Open the in-game language picker."),
            modConfig.Bind("Translation", "EnableDynamicPatterns", true, "Enable numbered placeholders such as Damage: {0}."),
            modConfig.Bind("Diagnostics", "LogPopupTranslationAudit", false, "Log each unique popup translation hit or miss once."));

        var translationDirectory = Path.Combine(Paths.BepInExRootPath, "translations", "DisfigureTranslationMod");
        TranslationRuntime.Configure(settings, Log, translationDirectory);
        harmony = new Harmony("casto.disfigure.translation-mod");
        harmony.PatchAll(typeof(Plugin).Assembly);
        AddComponent<TranslationRuntime>();
        Log.LogInfo("DisfigureTranslationMod loaded. F8 opens the language picker; F5 reloads translations; F4 forces a rescan.");
    }
}

internal sealed record PluginSettings(
    ConfigEntry<bool> Enabled,
    ConfigEntry<bool> DetectionEnabled,
    ConfigEntry<bool> ReplacementEnabled,
    ConfigEntry<float> ScanInterval,
    ConfigEntry<bool> LogDetectedStrings,
    ConfigEntry<string> TranslationFilename,
    ConfigEntry<Key> ReloadHotkey,
    ConfigEntry<Key> RescanHotkey,
    ConfigEntry<Key> LanguageMenuHotkey,
    ConfigEntry<bool> DynamicPatternsEnabled,
    ConfigEntry<bool> LogPopupTranslationAudit);
