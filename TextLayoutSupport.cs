using System;
using TMPro;
using UnityEngine.UI;

namespace DisfigureTranslationMod;

internal static class TextLayoutSupport
{
    private const int MaxLabelLength = 32;
    private const float MinimumScale = 0.6f;

    internal static void FitLabels(Text component, string source, string translated)
    {
        if (!IsLabelBlock(source, translated))
        {
            return;
        }

        if (source.Contains('\n'))
        {
            component.resizeTextForBestFit = false;
            component.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
            return;
        }

        var width = component.rectTransform.rect.width;
        if (width <= 0f || component.preferredWidth <= width)
        {
            return;
        }

        component.resizeTextMaxSize = component.fontSize;
        component.resizeTextMinSize = Math.Max(8, (int)Math.Floor(component.fontSize * MinimumScale));
        component.resizeTextForBestFit = true;
    }

    internal static void FitLabels(TMP_Text component, string source, string translated)
    {
        if (!IsLabelBlock(source, translated))
        {
            return;
        }

        var width = component.rectTransform.rect.width;
        if (width <= 0f || !source.Contains('\n') && component.preferredWidth <= width)
        {
            return;
        }

        component.fontSizeMax = component.fontSize;
        component.fontSizeMin = Math.Max(8f, component.fontSize * MinimumScale);
        component.enableAutoSizing = true;
    }

    internal static bool SelfCheck()
    {
        return IsLabelBlock("Damage\nFire Rate", "Урон\nСкорострельность")
            && !IsLabelBlock("A normal description that should not be resized because it is prose.", "Описание");
    }

    private static bool IsLabelBlock(string source, string translated)
    {
        var sourceLines = source.Split('\n');
        var translatedLines = translated.Split('\n');
        return sourceLines.Length <= 8
            && translatedLines.Length == sourceLines.Length
            && Array.TrueForAll(sourceLines, line => line.TrimEnd('\r').Length <= MaxLabelLength)
            && Array.TrueForAll(translatedLines, line => line.TrimEnd('\r').Length <= MaxLabelLength * 2);
    }
}
