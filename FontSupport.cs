using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DisfigureTranslationMod;

internal sealed class FontSupport
{
    private static readonly Regex RichTextTag = new("<[^>]+>", RegexOptions.CultureInvariant);
    private readonly ManualLogSource logger;
    private readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private bool fallbackLogged;
    private static readonly Dictionary<int, Font> FallbackFonts = new();
    private static bool fallbackUnavailable;
    private static readonly string[] FallbackFontNames =
    {
        "Segoe UI", "Microsoft YaHei UI", "Microsoft JhengHei UI",
        "Yu Gothic UI", "Meiryo UI", "Malgun Gothic", "Arial Unicode MS"
    };

    internal FontSupport(ManualLogSource logger)
    {
        this.logger = logger;
    }

    internal void Check(TMP_Text component, string translated)
    {
        try
        {
            var font = component.font;
            if (font == null)
            {
                WarnOnce("<no TMP font>", translated, "no font is assigned");
                return;
            }

            foreach (var rune in RemoveRichTextTags(translated).EnumerateRunes())
            {
                if (!RuneIsIgnorable(rune.Value) && !font.HasCharacter(rune.Value))
                {
                    WarnOnce(font.name ?? "<unnamed TMP font>", translated, $"missing U+{rune.Value:X4}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            WarnOnce("<TMP font check>", translated, ex.Message);
        }
    }

    internal void Check(Text component, string translated)
    {
        try
        {
            var font = component.font;
            if (font == null)
            {
                WarnOnce("<no legacy font>", translated, "no font is assigned");
                return;
            }

            foreach (var character in RemoveRichTextTags(translated))
            {
                if (!char.IsWhiteSpace(character) && !font.HasCharacter(character))
                {
                    if (TryApplyFallback(component, translated))
                    {
                        return;
                    }

                    WarnOnce(font.name ?? "<unnamed legacy font>", translated, $"missing U+{(int)character:X4}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            WarnOnce("<legacy font check>", translated, ex.Message);
        }
    }

    internal static string RemoveRichTextTags(string value) => RichTextTag.Replace(value, string.Empty);

    private bool TryApplyFallback(Text component, string translated)
    {
        var size = Math.Max(1, component.fontSize);
        var fallback = GetSystemFallbackFont(size);
        foreach (var character in RemoveRichTextTags(translated))
        {
            if (!char.IsWhiteSpace(character) && !fallback.HasCharacter(character))
            {
                return false;
            }
        }

        component.font = fallback;
        if (!fallbackLogged)
        {
            fallbackLogged = true;
            logger.LogInfo($"Applied system font fallback '{fallback.name}' for translated legacy UI text.");
        }
        return true;
    }

    internal static Font GetSystemFallbackFont(int size)
    {
        if (fallbackUnavailable)
        {
            return Font.GetDefault();
        }

        if (!FallbackFonts.TryGetValue(size, out var fallback))
        {
            try
            {
                var names = new Il2CppStringArray(FallbackFontNames.Length);
                for (var i = 0; i < FallbackFontNames.Length; i++)
                {
                    names[i] = FallbackFontNames[i];
                }

                fallback = new Font(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<Font>.NativeClassPtr));
                Font.Internal_CreateDynamicFont(fallback, names, size);
                FallbackFonts[size] = fallback;
            }
            catch
            {
                fallbackUnavailable = true;
                return Font.GetDefault();
            }
        }

        return fallback;
    }

    private static bool RuneIsIgnorable(int value) => value is 9 or 10 or 13 or 32;

    private void WarnOnce(string font, string text, string reason)
    {
        if (warnings.Add(font + "\0" + text))
        {
            logger.LogWarning($"Font '{font}' may not support translated text '{text}': {reason}.");
        }
    }
}
