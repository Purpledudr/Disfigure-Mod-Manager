using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using BepInEx.Logging;

namespace DisfigureTranslationMod;

internal sealed class TranslationFileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly ManualLogSource logger;

    internal TranslationFileManager(string directory, string configuredFilename, ManualLogSource logger)
    {
        DirectoryPath = directory;
        this.logger = logger;
        TranslationPath = Path.Combine(directory, "translated.json");
        SetTranslationFilename(configuredFilename);
        DetectedPath = Path.Combine(directory, "detected_strings.json");
        EnglishPath = Path.Combine(directory, "en.json");
    }

    internal bool SetTranslationFilename(string configuredFilename)
    {
        var filename = Path.GetFileName(configuredFilename);
        if (string.IsNullOrWhiteSpace(filename) || filename != configuredFilename)
        {
            logger.LogWarning($"Invalid translation filename '{configuredFilename}'.");
            return false;
        }

        TranslationPath = Path.Combine(DirectoryPath, filename);
        return true;
    }

    internal string DirectoryPath { get; }
    internal string TranslationPath { get; private set; }
    internal string DetectedPath { get; }
    internal string EnglishPath { get; }

    internal void EnsureFiles()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            CreateEmptyFile(DetectedPath);
            CreateEmptyFile(TranslationPath);
            CreateEmptyFile(EnglishPath);
            logger.LogInfo($"Translation file: {TranslationPath}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Could not create translation files in {DirectoryPath}: {ex.Message}");
        }
    }

    internal Dictionary<string, string> LoadTranslations() => LoadDictionary(TranslationPath);

    internal IEnumerable<Dictionary<string, string>> LoadLanguageCatalogs()
    {
        foreach (var path in Directory.GetFiles(DirectoryPath, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path);
            if (code.Length == 2 && !code.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                yield return LoadDictionary(path);
            }
        }
    }

    internal SortedDictionary<string, string> LoadDetected()
    {
        return new SortedDictionary<string, string>(LoadDictionary(DetectedPath), StringComparer.Ordinal);
    }

    internal void SaveDetected(SortedDictionary<string, string> strings)
    {
        SafeWrite(DetectedPath, strings);
    }

    private void CreateEmptyFile(string path)
    {
        if (!File.Exists(path))
        {
            SafeWrite(path, new SortedDictionary<string, string>(StringComparer.Ordinal));
        }
    }

    private Dictionary<string, string> LoadDictionary(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            logger.LogError($"JSON parsing error in {path}: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Could not read {path}: {ex.Message}");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void SafeWrite<T>(string path, T value)
    {
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, path, true);
        }
        catch (Exception ex)
        {
            logger.LogError($"Could not safely write {path}: {ex.Message}");
        }
    }
}
