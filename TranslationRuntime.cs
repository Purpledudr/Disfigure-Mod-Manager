using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DisfigureTranslationMod;

public sealed class TranslationRuntime : MonoBehaviour
{
    private const float DelayedSceneScanSeconds = 1.5f;
    private static PluginSettings? configuredSettings;
    private static ManualLogSource? configuredLogger;
    private static string? configuredDirectory;

    private PluginSettings settings = null!;
    private ManualLogSource logger = null!;
    private TranslationManager manager = null!;
    private TextScanner scanner = null!;
    private string currentScene = string.Empty;
    private float nextPeriodicScan;
    private float delayedSceneScan = -1f;
    private int lastComponentCount = -1;
    private bool inputScanPending;
    private UnityAction<Scene, LoadSceneMode>? sceneLoadedHandler;
    private bool languageMenuOpen;
    private string[] languageFiles = Array.Empty<string>();
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["es"] = "Español",
        ["fr"] = "Français",
        ["ru"] = "Русский",
        ["pt"] = "Português (Brasil)",
        ["de"] = "Deutsch",
        ["zh"] = "简体中文",
        ["ja"] = "日本語",
        ["pl"] = "Polski"
    };

    internal static void Configure(PluginSettings settings, ManualLogSource logger, string translationDirectory)
    {
        configuredSettings = settings;
        configuredLogger = logger;
        configuredDirectory = translationDirectory;
    }

    private void Awake()
    {
        settings = configuredSettings ?? throw new InvalidOperationException("Translation runtime was not configured.");
        logger = configuredLogger ?? throw new InvalidOperationException("Translation logger was not configured.");
        var fileManager = new TranslationFileManager(configuredDirectory!, settings.TranslationFilename.Value, logger);
        fileManager.EnsureFiles();
        manager = new TranslationManager(fileManager, logger, settings.LogDetectedStrings, settings.DynamicPatternsEnabled);
        PopupTranslation.Initialize(manager, settings, logger);
        scanner = new TextScanner(manager, logger);

        if (!DynamicTranslationMatcher.SelfCheck())
        {
            logger.LogError("Dynamic translation matcher self-check failed; placeholder translations may not work.");
        }

        if (!TranslationManager.SelfCheck())
        {
            logger.LogError("Multiline translation self-check failed; composed tooltip translations may not work.");
        }

        if (!TranslationManager.LanguageSwitchSelfCheck())
        {
            logger.LogError("Language switch self-check failed; visible text may not update immediately.");
        }

        if (!TextLayoutSupport.SelfCheck())
        {
            logger.LogError("Translated label layout self-check failed; compact stat blocks may overlap.");
        }

        manager.ReloadTranslations();
        currentScene = SafeSceneName();
        sceneLoadedHandler = DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(
            new Action<Scene, LoadSceneMode>(OnSceneLoaded));
        SceneManager.sceneLoaded += sceneLoadedHandler;
        Scan("startup");
        delayedSceneScan = Time.realtimeSinceStartup + DelayedSceneScanSeconds;
        nextPeriodicScan = Time.realtimeSinceStartup + Interval;
    }

    private void OnDestroy()
    {
        if (sceneLoadedHandler != null)
        {
            SceneManager.sceneLoaded -= sceneLoadedHandler;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var previousScene = currentScene;
        currentScene = scene.name ?? "<unknown>";
        logger.LogInfo($"Scene loaded: {previousScene} -> {currentScene}");
        Scan("scene loaded");
        delayedSceneScan = Time.realtimeSinceStartup + DelayedSceneScanSeconds;
        nextPeriodicScan = Time.realtimeSinceStartup + Interval;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[settings.ReloadHotkey.Value].wasPressedThisFrame)
        {
            manager.ReloadTranslations();
            PopupTranslation.ResetAudit();
            Scan("translation reload");
        }

        if (keyboard != null && keyboard[settings.RescanHotkey.Value].wasPressedThisFrame)
        {
            Scan("force-rescan hotkey");
            nextPeriodicScan = Time.realtimeSinceStartup + Interval;
        }

        if (keyboard != null && keyboard[settings.LanguageMenuHotkey.Value].wasPressedThisFrame)
        {
            languageMenuOpen = !languageMenuOpen;
            if (languageMenuOpen)
            {
                RefreshLanguageFiles();
            }
        }

        if (!settings.Enabled.Value)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse != null
            && (mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame))
        {
            inputScanPending = true;
        }

        var scene = SafeSceneName();
        if (scene != currentScene)
        {
            logger.LogInfo($"Scene changed: {currentScene} -> {scene}");
            currentScene = scene;
            Scan("scene change");
            delayedSceneScan = Time.realtimeSinceStartup + DelayedSceneScanSeconds;
            nextPeriodicScan = Time.realtimeSinceStartup + Interval;
            return;
        }

        var now = Time.realtimeSinceStartup;

        if (delayedSceneScan >= 0f && now >= delayedSceneScan)
        {
            delayedSceneScan = -1f;
            Scan("delayed scene scan");
            return;
        }

        if (now >= nextPeriodicScan)
        {
            nextPeriodicScan = now + Interval;
            Scan("periodic");
        }
    }

    private void LateUpdate()
    {
        if (!settings.Enabled.Value)
        {
            return;
        }

        if (inputScanPending)
        {
            inputScanPending = false;
            Scan("input");
            nextPeriodicScan = Time.realtimeSinceStartup + Interval;
        }

        if (settings.ReplacementEnabled.Value)
        {
            scanner.TranslateKnown();
        }
    }

    private float Interval => Math.Max(0.25f, settings.ScanInterval.Value);

    private void OnGUI()
    {
        if (!languageMenuOpen)
        {
            return;
        }

        var previousFont = GUI.skin.font;
        GUI.skin.font = FontSupport.GetSystemFallbackFont(18);
        try
        {
        const float width = 340f;
        const float rowHeight = 34f;
        var height = 70f + languageFiles.Length * rowHeight;
        var left = (Screen.width - width) / 2f;
        var top = (Screen.height - height) / 2f;
        GUI.Box(new Rect(left, top, width, height), "Disfigure Language");

        for (var i = 0; i < languageFiles.Length; i++)
        {
            var filename = languageFiles[i];
            var code = Path.GetFileNameWithoutExtension(filename);
            var selected = string.Equals(filename, settings.TranslationFilename.Value, StringComparison.OrdinalIgnoreCase);
            var label = (selected ? "[x] " : string.Empty) + LanguageNames[code];
            if (GUI.Button(new Rect(left + 20f, top + 35f + i * rowHeight, width - 40f, 28f), label))
            {
                SelectLanguage(filename);
            }
        }

        if (GUI.Button(new Rect(left + width - 28f, top + 4f, 22f, 22f), "X"))
        {
            languageMenuOpen = false;
        }
        }
        finally
        {
            GUI.skin.font = previousFont;
        }
    }

    private void RefreshLanguageFiles()
    {
        var found = new List<string>();
        foreach (var path in Directory.GetFiles(configuredDirectory!, "*.json"))
        {
            var filename = Path.GetFileName(path);
            if (LanguageNames.ContainsKey(Path.GetFileNameWithoutExtension(filename)))
            {
                found.Add(filename);
            }
        }

        found.Sort((left, right) => string.Compare(LanguageNames[Path.GetFileNameWithoutExtension(left)], LanguageNames[Path.GetFileNameWithoutExtension(right)], StringComparison.Ordinal));
        languageFiles = found.ToArray();
    }

    private void SelectLanguage(string filename)
    {
        if (!manager.SwitchTranslations(filename))
        {
            return;
        }

        settings.TranslationFilename.Value = filename;
        PopupTranslation.ResetAudit();
        Scan("language switch");
        logger.LogInfo($"Language switched to {LanguageNames[Path.GetFileNameWithoutExtension(filename)]} ({filename}).");
        languageMenuOpen = false;
    }

    private void Scan(string reason)
    {
        if (!settings.Enabled.Value)
        {
            return;
        }

        var result = scanner.Scan(settings.DetectionEnabled.Value, settings.ReplacementEnabled.Value);
        var routineScan = reason is "periodic" or "input";
        if (!routineScan
            || result.ComponentCount != lastComponentCount
            || result.NewStrings > 0
            || result.Replacements > 0)
        {
            logger.LogInfo($"Text scan ({reason}): {result.ComponentCount} components, {result.NewStrings} new strings, {result.Replacements} replacements.");
        }

        lastComponentCount = result.ComponentCount;
    }

    private static string SafeSceneName()
    {
        try
        {
            return SceneManager.GetActiveScene().name ?? "<unknown>";
        }
        catch
        {
            return "<unknown>";
        }
    }
}
