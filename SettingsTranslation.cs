using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DisfigureTranslationMod;

[HarmonyPatch(typeof(ingamesettingsscript))]
internal static class InGameSettingsTranslationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ingamesettingsscript), nameof(ingamesettingsscript.Start));
        yield return AccessTools.Method(typeof(ingamesettingsscript), nameof(ingamesettingsscript.ToggleOptionFunction));
    }

    private static void Prefix(ingamesettingsscript __instance)
    {
        // These are the values the game assigns on each click. Translating the
        // backing strings makes the game write Spanish directly.
        __instance.onTextToDisplay = PopupTranslation.TranslateValue(__instance.onTextToDisplay);
        __instance.offTextToDisplay = PopupTranslation.TranslateValue(__instance.offTextToDisplay);
    }

    private static void Postfix(ingamesettingsscript __instance)
    {
        PopupTranslation.Translate(__instance.buttonText);
    }
}

[HarmonyPatch(typeof(OtherSettings))]
internal static class OtherSettingsTranslationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var names = new[]
        {
            "OnEnable", "SetFullscreen", "SetCameraMovement", "SetEXPIndicator",
            "SetPlayerShadow", "SetEffects", "SetChromaticAberration", "SetScanlines",
            "SetFilmGrain", "SetFPS", "SetToggleVision", "SetToggleMutationIntro",
            "SetDamageText", "SetDamageColors", "SetVsync"
        };

        foreach (var name in names)
        {
            yield return AccessTools.Method(typeof(OtherSettings), name);
        }
    }

    private static void Postfix(OtherSettings __instance)
    {
        PopupTranslation.Translate(__instance.onofftext);
        PopupTranslation.Translate(__instance.onofftextvsync);
        PopupTranslation.Translate(__instance.fpstext);
        PopupTranslation.Translate(__instance.cameraMovementText);
        PopupTranslation.Translate(__instance.playerShadowText);
        PopupTranslation.Translate(__instance.EXPIndicatorText);
        PopupTranslation.Translate(__instance.toggleVisionText);
        PopupTranslation.Translate(__instance.toggleMutationIntroText);
        PopupTranslation.Translate(__instance.damageText);
        PopupTranslation.Translate(__instance.damageColorsText);
        PopupTranslation.Translate(__instance.effectsText);
        PopupTranslation.Translate(__instance.aberrationText);
        PopupTranslation.Translate(__instance.scanlinesText);
        PopupTranslation.Translate(__instance.filmGrainText);
    }
}
