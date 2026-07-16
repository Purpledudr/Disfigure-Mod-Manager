using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DisfigureTranslationMod;

internal sealed class DynamicTranslationMatcher
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);
    private const string NumberPattern = @"[-+]?\d+(?:[.,]\d+)?%?";
    private readonly List<Entry> entries = new();

    internal DynamicTranslationMatcher(IEnumerable<KeyValuePair<string, string>> patterns)
    {
        foreach (var pair in patterns)
        {
            entries.Add(new Entry(pair.Key, pair.Value));
        }
    }

    internal int Count => entries.Count;

    internal static bool ContainsPlaceholder(string value) => Placeholder.IsMatch(value);

    internal bool TryTranslate(string source, out string translated)
    {
        foreach (var entry in entries)
        {
            if (entry.TryTranslate(source, out translated))
            {
                return true;
            }
        }

        translated = source;
        return false;
    }

    internal bool IsTranslatedOutput(string text)
    {
        foreach (var entry in entries)
        {
            if (entry.OutputPattern.IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool SelfCheck()
    {
        var matcher = new DynamicTranslationMatcher(new[]
        {
            new KeyValuePair<string, string>("Damage: {0}", "Translated Damage: {0}"),
            new KeyValuePair<string, string>("KILLS [{0}]", "BAJAS [{0}]")
        });
        return matcher.TryTranslate("Damage: 25", out var result)
            && result == "Translated Damage: 25"
            && matcher.IsTranslatedOutput(result)
            && matcher.TryTranslate("KILLS [5]", out var endScreen)
            && endScreen == "BAJAS [5]";
    }

    private sealed class Entry
    {
        private readonly string target;
        private readonly Regex sourcePattern;

        internal Entry(string source, string target)
        {
            this.target = target;
            sourcePattern = BuildRegex(source);
            OutputPattern = BuildRegex(target);
        }

        internal Regex OutputPattern { get; }

        internal bool TryTranslate(string source, out string translated)
        {
            var match = sourcePattern.Match(source);
            if (!match.Success)
            {
                translated = source;
                return false;
            }

            translated = Placeholder.Replace(target, placeholder =>
            {
                var group = match.Groups["p" + placeholder.Groups[1].Value];
                return group.Success ? group.Value : placeholder.Value;
            });
            return true;
        }

        private static Regex BuildRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            var usedGroups = new HashSet<string>(StringComparer.Ordinal);
            var position = 0;
            foreach (Match placeholder in Placeholder.Matches(pattern))
            {
                builder.Append(Regex.Escape(pattern[position..placeholder.Index]));
                var group = "p" + placeholder.Groups[1].Value;
                builder.Append(usedGroups.Add(group) ? $"(?<{group}>{NumberPattern})" : $"\\k<{group}>");
                position = placeholder.Index + placeholder.Length;
            }

            builder.Append(Regex.Escape(pattern[position..]));
            builder.Append('$');
            return new Regex(builder.ToString(), RegexOptions.CultureInvariant);
        }
    }
}
