using System;
using System.Collections.Generic;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DisfigureTranslationMod;

internal sealed class TextScanner
{
    private readonly TranslationManager manager;
    private readonly ManualLogSource logger;
    private readonly FontSupport fontSupport;
    private readonly HashSet<string> loggedAssignmentFailures = new(StringComparer.Ordinal);
    private readonly HashSet<int> inspectedUpgradeObjects = new();

    internal TextScanner(TranslationManager manager, ManualLogSource logger)
    {
        this.manager = manager;
        this.logger = logger;
        fontSupport = new FontSupport(logger);
    }

    internal ScanResult Scan(bool detect, bool replace)
    {
        var detectedBefore = manager.DetectedCount;
        var componentCount = 0;
        var newStrings = 0;
        var replacements = 0;

        try
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (!IsUsable(text))
                {
                    continue;
                }

                componentCount++;
                ProcessTmp(text, detect, replace, ref newStrings, ref replacements);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"TMP text enumeration failed: {ex.Message}");
        }

        try
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (!IsUsable(text))
                {
                    continue;
                }

                componentCount++;
                ProcessLegacy(text, detect, replace, ref newStrings, ref replacements);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Legacy UI text enumeration failed: {ex.Message}");
        }

        if (detect)
        {
            ScanUpgradeMetadata();
        }

        newStrings = manager.DetectedCount - detectedBefore;
        manager.SaveDetected();

        return new ScanResult(componentCount, newStrings, replacements);
    }

    private void ScanUpgradeMetadata()
    {
        try
        {
            foreach (var upgrade in Resources.FindObjectsOfTypeAll<Upgrade>())
            {
                if (!IsUsable(upgrade) || !inspectedUpgradeObjects.Add(upgrade.GetInstanceID()))
                {
                    continue;
                }

                _ = upgrade.getName();
                _ = upgrade.getDescription();
            }

            foreach (var upgrade in Resources.FindObjectsOfTypeAll<StatUpgrade>())
            {
                if (!IsUsable(upgrade) || !inspectedUpgradeObjects.Add(upgrade.GetInstanceID()))
                {
                    continue;
                }

                _ = upgrade.getName();
                _ = upgrade.getDescription();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Upgrade metadata enumeration failed: {ex.Message}");
        }
    }

    private void ProcessTmp(TMP_Text component, bool detect, bool replace, ref int newStrings, ref int replacements)
    {
        Process(
            component,
            () => component.text,
            value => component.text = value,
            value => fontSupport.Check(component, value),
            (source, translated) => TextLayoutSupport.FitLabels(component, source, translated),
            detect,
            replace,
            ref newStrings,
            ref replacements);
    }

    private void ProcessLegacy(Text component, bool detect, bool replace, ref int newStrings, ref int replacements)
    {
        Process(
            component,
            () => component.text,
            value => component.text = value,
            value => fontSupport.Check(component, value),
            (source, translated) => TextLayoutSupport.FitLabels(component, source, translated),
            detect,
            replace,
            ref newStrings,
            ref replacements);
    }

    private void Process(
        Component component,
        Func<string> read,
        Action<string> write,
        Action<string> checkFont,
        Action<string, string> fitLayout,
        bool detect,
        bool replace,
        ref int newStrings,
        ref int replacements)
    {
        try
        {
            var source = read();
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            if (detect && !manager.IsTranslatedOutput(source) && manager.Observe(source))
            {
                newStrings++;
            }

            if (!replace || !manager.TryTranslate(source, out var translated) || translated == source)
            {
                return;
            }

            checkFont(translated);
            write(translated);
            fitLayout(source, translated);
            replacements++;
        }
        catch (Exception ex)
        {
            var identity = SafeIdentity(component);
            if (loggedAssignmentFailures.Add(identity))
            {
                logger.LogWarning($"Failed translation assignment for {identity}: {ex.Message}");
            }
        }
    }

    private static bool IsUsable(Behaviour component)
    {
        try
        {
            return component != null && component.gameObject != null;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeIdentity(Component component)
    {
        try
        {
            return $"{component.GetType().Name} '{component.gameObject.name}'";
        }
        catch
        {
            return "destroyed text component";
        }
    }
}

internal readonly record struct ScanResult(int ComponentCount, int NewStrings, int Replacements);
