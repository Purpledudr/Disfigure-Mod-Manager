using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DisfigureTranslationMod;

internal sealed class TranslationManager
{
    private readonly TranslationFileManager files;
    private readonly ManualLogSource logger;
    private readonly ConfigEntry<bool> logDetectedStrings;
    private readonly ConfigEntry<bool> dynamicPatternsEnabled;
    private readonly SortedDictionary<string, string> detected;
    private Dictionary<string, string> exact = new(StringComparer.Ordinal);
    private Dictionary<string, string> sourcesByTranslatedOutput = new(StringComparer.Ordinal);
    private HashSet<string> translatedOutputs = new(StringComparer.Ordinal);
    private DynamicTranslationMatcher dynamic = new(Array.Empty<KeyValuePair<string, string>>());
    private bool detectedDirty;

    internal TranslationManager(
        TranslationFileManager files,
        ManualLogSource logger,
        ConfigEntry<bool> logDetectedStrings,
        ConfigEntry<bool> dynamicPatternsEnabled)
    {
        this.files = files;
        this.logger = logger;
        this.logDetectedStrings = logDetectedStrings;
        this.dynamicPatternsEnabled = dynamicPatternsEnabled;
        detected = files.LoadDetected();
    }

    internal void ReloadTranslations()
    {
        var loaded = files.LoadTranslations();
        exact = new Dictionary<string, string>(StringComparer.Ordinal);
        translatedOutputs = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new List<KeyValuePair<string, string>>();

        foreach (var pair in loaded)
        {
            if (pair.Value is null)
            {
                logger.LogWarning($"Ignoring translation '{pair.Key}' because its value is null.");
                continue;
            }

            if (DynamicTranslationMatcher.ContainsPlaceholder(pair.Key))
            {
                patterns.Add(pair);
            }
            else
            {
                exact[NormalizeLineEndings(pair.Key)] = pair.Value;
                translatedOutputs.Add(NormalizeLineEndings(pair.Value));
            }
        }

        dynamic = new DynamicTranslationMatcher(patterns);
        sourcesByTranslatedOutput = BuildOutputSources(files.LoadLanguageCatalogs());
        logger.LogInfo($"Translation reload complete: {exact.Count} exact, {dynamic.Count} pattern, {loaded.Count} total.");
    }

    internal bool SwitchTranslations(string filename)
    {
        if (!files.SetTranslationFilename(filename))
        {
            return false;
        }

        files.EnsureFiles();
        ReloadTranslations();
        return true;
    }

    internal static bool LanguageSwitchSelfCheck()
    {
        var reverse = BuildOutputSources(new[]
        {
            new Dictionary<string, string> { ["PLAY"] = "JOUER" },
            new Dictionary<string, string> { ["PLAY"] = "SPIELEN" }
        });
        return reverse.TryGetValue("JOUER", out var french) && french == "PLAY"
            && reverse.TryGetValue("SPIELEN", out var german) && german == "PLAY";
    }

    internal bool TryTranslate(string source, out string translated)
    {
        if (TryTranslateAtomic(source, out translated))
        {
            return true;
        }

        if (source.Contains('\n'))
        {
            var composed = TranslateLines(source, line =>
            {
                if (TryTranslateAtomic(line, out var lineTranslation))
                {
                    return lineTranslation;
                }

                return IsAtomicTranslatedOutput(line) ? line : null;
            });
            if (composed != null && composed != source)
            {
                translated = composed;
                return true;
            }
        }

        translated = source;
        return false;
    }

    internal bool IsTranslatedOutput(string text)
    {
        if (IsAtomicTranslatedOutput(text))
        {
            return true;
        }

        return text.Contains('\n')
            && TranslateLines(text, line => IsAtomicTranslatedOutput(line) ? line : null) != null;
    }

    internal static bool SelfCheck()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Damage <color=white>+25%</color>"] = "Daño <color=white>+25%</color>",
            ["Speed <color=white>+10%</color>"] = "Velocidad <color=white>+10%</color>"
        };
        var result = TranslateLines(
            "Damage <color=white>+25%</color>\n\nSpeed <color=white>+10%</color>\n",
            line => values.TryGetValue(line, out var value) ? value : null);
        return result == "Daño <color=white>+25%</color>\n\nVelocidad <color=white>+10%</color>\n"
            && NormalizeLineEndings("one\r\ntwo") == "one\ntwo";
    }

    internal bool Observe(string source)
    {
        if (!IsMeaningfulSource(source) || IsTranslatedOutput(source) || detected.ContainsKey(source))
        {
            return false;
        }

        detected[source] = source;
        detectedDirty = true;
        if (logDetectedStrings.Value)
        {
            logger.LogInfo($"Detected new text: {source.Replace("\r", "\\r").Replace("\n", "\\n")}");
        }

        return true;
    }

    internal int DetectedCount => detected.Count;

    internal void SaveDetected()
    {
        if (!detectedDirty)
        {
            return;
        }

        files.SaveDetected(detected);
        detectedDirty = false;
    }

    private static bool IsMeaningfulSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var withoutTags = FontSupport.RemoveRichTextTags(source);
        foreach (var character in withoutTags)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryTranslateAtomic(string source, out string translated)
    {
        var lookupSource = NormalizeLineEndings(source);
        if (exact.TryGetValue(lookupSource, out translated!))
        {
            return true;
        }

        if (sourcesByTranslatedOutput.TryGetValue(lookupSource, out var original)
            && exact.TryGetValue(original, out translated!))
        {
            return true;
        }

        if (dynamicPatternsEnabled.Value && dynamic.TryTranslate(lookupSource, out translated))
        {
            return true;
        }

        translated = source;
        return false;
    }

    private bool IsAtomicTranslatedOutput(string text)
    {
        var lookupText = NormalizeLineEndings(text);
        return translatedOutputs.Contains(lookupText)
            || sourcesByTranslatedOutput.ContainsKey(lookupText)
            || dynamicPatternsEnabled.Value && dynamic.IsTranslatedOutput(lookupText);
    }

    private static string NormalizeLineEndings(string text) =>
        text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;

    private static Dictionary<string, string> BuildOutputSources(IEnumerable<Dictionary<string, string>> catalogs)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var translations in catalogs)
        {
            foreach (var pair in translations)
            {
                result.TryAdd(NormalizeLineEndings(pair.Value), NormalizeLineEndings(pair.Key));
            }
        }

        return result;
    }

    private static string? TranslateLines(string source, Func<string, string?> translateLine)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var carriageReturn = lines[i].EndsWith('\r');
            var line = carriageReturn ? lines[i][..^1] : lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var translated = translateLine(line);
            if (translated == null)
            {
                return null;
            }

            lines[i] = translated + (carriageReturn ? "\r" : string.Empty);
        }

        return string.Join('\n', lines);
    }
}
