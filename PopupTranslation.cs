using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DisfigureTranslationMod;

internal static class PopupTranslation
{
    private static TranslationManager? manager;
    private static PluginSettings? settings;
    private static ManualLogSource? logger;
    private static FontSupport? fontSupport;
    private static bool legacyHookLogged;
    private static bool modernHookLogged;
    private static readonly HashSet<string> audited = new(StringComparer.Ordinal);

    internal static void Initialize(TranslationManager translationManager, PluginSettings pluginSettings, ManualLogSource log)
    {
        manager = translationManager;
        settings = pluginSettings;
        logger = log;
        fontSupport = new FontSupport(log);
    }

    internal static void Translate(Text? component, string context = "popup")
    {
        if (component == null || manager == null || settings?.Enabled.Value != true)
        {
            return;
        }

        try
        {
            var source = component.text;
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            if (settings.DetectionEnabled.Value
                && !manager.IsTranslatedOutput(source)
                && manager.Observe(source))
            {
                manager.SaveDetected();
            }

            var matched = manager.TryTranslate(source, out var translated);
            Audit(context, source, translated, matched);
            if (settings.ReplacementEnabled.Value && matched && translated != source)
            {
                fontSupport?.Check(component, translated);
                component.text = translated;
                TextLayoutSupport.FitLabels(component, source, translated);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"Popup translation failed: {ex.Message}");
        }
    }

    internal static void Translate(TMP_Text? component, string context = "TMP text")
    {
        if (component == null || manager == null || settings?.Enabled.Value != true)
        {
            return;
        }

        try
        {
            var source = component.text;
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            if (settings.DetectionEnabled.Value
                && !manager.IsTranslatedOutput(source)
                && manager.Observe(source))
            {
                manager.SaveDetected();
            }

            var matched = manager.TryTranslate(source, out var translated);
            Audit(context, source, translated, matched);
            if (settings.ReplacementEnabled.Value && matched && translated != source)
            {
                fontSupport?.Check(component, translated);
                component.text = translated;
                TextLayoutSupport.FitLabels(component, source, translated);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning($"TMP translation failed: {ex.Message}");
        }
    }

    internal static string TranslateValue(string source, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(source) || manager == null || settings?.Enabled.Value != true)
        {
            return source;
        }

        if (settings.DetectionEnabled.Value)
        {
            manager.Observe(source);
        }

        var matched = manager.TryTranslate(source, out var translated);
        if (context != null)
        {
            Audit(context, source, translated, matched);
        }

        return settings.ReplacementEnabled.Value && matched ? translated : source;
    }

    internal static void FinishAssignment(TMP_Text component, string source)
    {
        var translated = component.text;
        if (translated == source)
        {
            return;
        }

        fontSupport?.Check(component, translated);
        TextLayoutSupport.FitLabels(component, source, translated);
    }

    internal static Il2CppStringArray TranslateCopy(Il2CppStringArray values, string context)
    {
        var copy = new Il2CppStringArray(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            copy[i] = TranslateValue(values[i], context);
        }

        return copy;
    }

    private static void Audit(string context, string source, string translated, bool matched)
    {
        if (settings?.LogPopupTranslationAudit.Value != true)
        {
            return;
        }

        var key = $"{context}\0{source}";
        if (!audited.Add(key))
        {
            return;
        }

        var escaped = source.Replace("\r", "\\r").Replace("\n", "\\n");
        if (!matched)
        {
            logger?.LogWarning($"Popup translation MISS [{context}]: {escaped}");
            return;
        }

        var output = translated.Replace("\r", "\\r").Replace("\n", "\\n");
        logger?.LogInfo($"Popup translation HIT [{context}]: {escaped} -> {output}");
    }

    internal static void LogLegacyHook()
    {
        if (!legacyHookLogged)
        {
            legacyHookLogged = true;
            logger?.LogInfo("Popup hook active: weaponupgradetooltip.setText");
        }
    }

    internal static void LogModernHook()
    {
        if (!modernHookLogged)
        {
            modernHookLogged = true;
            logger?.LogInfo("Popup hook active: UpgradeDescriptionTooltip.Show");
        }
    }

    internal static void ResetAudit() => audited.Clear();
}

[HarmonyPatch(typeof(TMP_Text), "set_text")]
internal static class TmpTextAssignmentPatch
{
    private static void Prefix(ref string __0, out string __state)
    {
        __state = __0;
        __0 = PopupTranslation.TranslateValue(__0);
    }

    private static void Postfix(TMP_Text __instance, string __state)
    {
        PopupTranslation.FinishAssignment(__instance, __state);
    }
}

[HarmonyPatch]
internal static class TmpTextActivationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var ugui = AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable");
        if (ugui != null)
        {
            yield return ugui;
        }

        var world = AccessTools.Method(typeof(TextMeshPro), "OnEnable");
        if (world != null)
        {
            yield return world;
        }
    }

    private static void Postfix(TMP_Text __instance)
    {
        PopupTranslation.Translate(__instance);
    }
}

[HarmonyPatch(typeof(Upgrade), nameof(Upgrade.getName))]
internal static class UpgradeNamePatch
{
    private static void Postfix(ref string __result) => __result = PopupTranslation.TranslateValue(__result);
}

[HarmonyPatch(typeof(Upgrade), nameof(Upgrade.getDescription))]
internal static class UpgradeDescriptionPatch
{
    private static void Postfix(ref string __result) => __result = PopupTranslation.TranslateValue(__result);
}

[HarmonyPatch(typeof(StatUpgrade), nameof(StatUpgrade.getName))]
internal static class StatUpgradeNamePatch
{
    private static void Postfix(ref string __result) => __result = PopupTranslation.TranslateValue(__result);
}

[HarmonyPatch(typeof(StatUpgrade), nameof(StatUpgrade.getDescription))]
internal static class StatUpgradeDescriptionPatch
{
    private static void Postfix(ref string __result) => __result = PopupTranslation.TranslateValue(__result);
}

[HarmonyPatch(typeof(Upgrade), nameof(Upgrade.fillOnSelect))]
internal static class UpgradeSelectionPatch
{
    private static void Postfix(Upgrade __instance)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(Upgrade), nameof(Upgrade.OnPointerEnter))]
internal static class UpgradeHoverPatch
{
    private static void Postfix(Upgrade __instance, PointerEventData eventData)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(StatUpgrade), nameof(StatUpgrade.fillOnSelect))]
internal static class StatUpgradeSelectionPatch
{
    private static void Postfix(StatUpgrade __instance)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(StatUpgrade), nameof(StatUpgrade.OnPointerEnter))]
internal static class StatUpgradeHoverPatch
{
    private static void Postfix(StatUpgrade __instance, PointerEventData eventData)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(upgradepath), nameof(upgradepath.OnLevelUpPointerEnter))]
internal static class UpgradePathLevelUpPatch
{
    private static void Postfix(upgradepath __instance)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(upgradepath), nameof(upgradepath.OnCompendiumOrMenuPointerEnter))]
internal static class UpgradePathMenuPatch
{
    private static void Postfix(upgradepath __instance)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(upgradeIconScript), nameof(upgradeIconScript.OnPointerEnter))]
internal static class UpgradeIconHoverPatch
{
    private static void Postfix(upgradeIconScript __instance, PointerEventData eventData)
    {
        PopupTranslation.Translate(__instance.panelname);
        PopupTranslation.Translate(__instance.paneldisc);
    }
}

[HarmonyPatch(typeof(weaponupgradetooltip), nameof(weaponupgradetooltip.setText))]
internal static class WeaponUpgradeTooltipPatch
{
    private static void Prefix(ref string upgradeName, ref string statdescription, ref Il2CppStringArray desclines)
    {
        upgradeName = PopupTranslation.TranslateValue(upgradeName, "weapon tooltip name");
        statdescription = PopupTranslation.TranslateValue(statdescription, "weapon tooltip description");
        if (desclines != null)
        {
            desclines = PopupTranslation.TranslateCopy(desclines, "weapon tooltip detail");
        }
    }

    private static void Postfix(weaponupgradetooltip __instance)
    {
        PopupTranslation.LogLegacyHook();
        PopupTranslation.Translate(__instance.panelname, "weapon tooltip displayed name");
        PopupTranslation.Translate(__instance.paneldisc, "weapon tooltip displayed description");
    }
}

[HarmonyPatch(typeof(UpgradeDescriptionTooltip), nameof(UpgradeDescriptionTooltip.Show))]
internal static class UpgradeDescriptionTooltipPatch
{
    private static void Prefix(ref string upgradeName, ref string statdescription, ref Il2CppStringArray desclines)
    {
        upgradeName = PopupTranslation.TranslateValue(upgradeName, "upgrade tooltip name");
        statdescription = PopupTranslation.TranslateValue(statdescription, "upgrade tooltip description");
        if (desclines != null)
        {
            desclines = PopupTranslation.TranslateCopy(desclines, "upgrade tooltip detail");
        }
    }

    private static void Postfix(UpgradeDescriptionTooltip __instance)
    {
        PopupTranslation.LogModernHook();
        PopupTranslation.Translate(__instance.titleText, "upgrade tooltip displayed name");
        PopupTranslation.Translate(__instance.bodyText, "upgrade tooltip displayed description");
    }
}

[HarmonyPatch(typeof(weaponupgrade), nameof(weaponupgrade.OnPointerEnter))]
internal static class WeaponPerkHoverPatch
{
    private static void Postfix(weaponupgrade __instance)
    {
        PopupTranslation.Translate(__instance.wTooltip?.panelname, "weapon perk hover name");
        PopupTranslation.Translate(__instance.wTooltip?.paneldisc, "weapon perk hover description");
    }
}

[HarmonyPatch(typeof(unlockedupgradespanel), nameof(unlockedupgradespanel.OnPointerEnter))]
internal static class UnlockedUpgradeHoverPatch
{
    private static void Postfix(unlockedupgradespanel __instance)
    {
        PopupTranslation.Translate(__instance.tooltipText, "unlocked upgrade tooltip");
        PopupTranslation.Translate(__instance.tempForPanelName, "unlocked upgrade name");
        PopupTranslation.Translate(__instance.tempForPanelDisc, "unlocked upgrade description");
    }
}

[HarmonyPatch(typeof(ScoreText2), nameof(ScoreText2.OnEnable))]
internal static class EndScreenTranslationPatch
{
    private static void Postfix(ScoreText2 __instance)
    {
        PopupTranslation.Translate(__instance.finalScore, "end screen total score");
        PopupTranslation.Translate(__instance.credits, "end screen credits");
        PopupTranslation.Translate(__instance.killText, "end screen kills score");
        PopupTranslation.Translate(__instance.minibossKillsText, "end screen miniboss score");
        PopupTranslation.Translate(__instance.bossKillsText, "end screen boss score");
        PopupTranslation.Translate(__instance.levelupsText, "end screen levels score");
        PopupTranslation.Translate(__instance.killTextNotScore, "end screen kills label");
        PopupTranslation.Translate(__instance.minibossKillsTextNotScore, "end screen miniboss label");
        PopupTranslation.Translate(__instance.bossKillsTextNotScore, "end screen boss label");
        PopupTranslation.Translate(__instance.levelupsTextNotScore, "end screen levels label");
    }
}

[HarmonyPatch(typeof(weaponupgradetooltip), nameof(weaponupgradetooltip.Update))]
internal static class WeaponTooltipLateRewritePatch
{
    private static void Postfix(weaponupgradetooltip __instance)
    {
        PopupTranslation.Translate(__instance.panelname, "weapon tooltip update name");
        PopupTranslation.Translate(__instance.paneldisc, "weapon tooltip update description");
    }
}

[HarmonyPatch(typeof(UpgradeDescriptionTooltip), nameof(UpgradeDescriptionTooltip.LateUpdate))]
internal static class ModernTooltipLateRewritePatch
{
    private static void Postfix(UpgradeDescriptionTooltip __instance)
    {
        PopupTranslation.Translate(__instance.titleText, "upgrade tooltip late name");
        PopupTranslation.Translate(__instance.bodyText, "upgrade tooltip late description");
    }
}

[HarmonyPatch(typeof(Upgrade), nameof(Upgrade.Update))]
internal static class UpgradeLateRewritePatch
{
    private static void Postfix(Upgrade __instance)
    {
        PopupTranslation.Translate(__instance.panelname, "upgrade update name");
        PopupTranslation.Translate(__instance.paneldisc, "upgrade update description");
    }
}

[HarmonyPatch(typeof(StatUpgrade), nameof(StatUpgrade.Update))]
internal static class StatUpgradeLateRewritePatch
{
    private static void Postfix(StatUpgrade __instance)
    {
        PopupTranslation.Translate(__instance.panelname, "stat upgrade update name");
        PopupTranslation.Translate(__instance.paneldisc, "stat upgrade update description");
    }
}

[HarmonyPatch(typeof(leveltext), nameof(leveltext.Update))]
internal static class LevelTextTranslationPatch
{
    private static void Postfix(leveltext __instance)
    {
        __instance.levelText.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
        __instance.shadowText.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
        PopupTranslation.Translate(__instance.levelText, "gameplay level");
        PopupTranslation.Translate(__instance.shadowText, "gameplay level shadow");
    }
}
