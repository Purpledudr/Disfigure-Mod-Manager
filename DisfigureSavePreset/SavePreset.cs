using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DisfigureSavePreset;

internal static class SavePreset
{
    internal const string RequestFileName = "DisfigureSavePreset.apply.json";
    internal const string SaveFileName = "saveData.txt";
    private const int MaxPresetBytes = 2 * 1024 * 1024;
    internal static readonly string[] SidecarSuffixes = { ".bak", ".tmp", ".write-in-progress", ".deferred-flags" };

    internal static string GetSaveDirectory()
    {
        var localLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        var result = SHGetKnownFolderPath(ref localLow, 0, IntPtr.Zero, out var path);
        try
        {
            Marshal.ThrowExceptionForHR(result);
            var directory = Marshal.PtrToStringUni(path);
            if (string.IsNullOrEmpty(directory))
                throw new IOException("Windows did not provide the LocalLow folder.");
            return Path.Combine(directory, "ColdBrew Entertainment", "Disfigure");
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);

    internal static string Apply(string request, string saveDirectory)
    {
        var archive = Path.Combine(Path.GetDirectoryName(request)!, "DisfigureSavePreset-backups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        var archivedPreset = Path.Combine(archive, "preset.json");

        // Claim the request before touching any save files, so a launch can only apply it once.
        File.Move(request, archivedPreset);
        try
        {
            var encoded = Encode(ReadPreset(archivedPreset));
            Directory.CreateDirectory(saveDirectory);
            Install(encoded, saveDirectory, archive);
            return archive;
        }
        catch (Exception error)
        {
            throw new IOException("The claimed preset and any archived originals are in " + archive, error);
        }
    }

    private static void Install(string encoded, string saveDirectory, string archive)
    {
        var live = Path.Combine(saveDirectory, SaveFileName);
        var sidecars = SidecarSuffixes.Select(suffix => live + suffix).Where(File.Exists).ToArray();
        var hadLive = File.Exists(live);
        foreach (var original in hadLive ? sidecars.Prepend(live) : sidecars)
            File.Copy(original, Path.Combine(archive, Path.GetFileName(original)));

        var removed = new List<string>();
        try
        {
            // Old recovery markers and journals can bring back state from the archived profile.
            ReplaceFile(live, Encoding.UTF8.GetBytes(encoded), () =>
            {
                foreach (var sidecar in sidecars)
                {
                    File.Delete(sidecar);
                    removed.Add(sidecar);
                }
            });
        }
        catch (Exception installError)
        {
            var errors = new List<Exception> { installError };
            foreach (var sidecar in removed)
            {
                try { File.Copy(Path.Combine(archive, Path.GetFileName(sidecar)), sidecar); }
                catch (Exception error) { errors.Add(error); }
            }

            if (errors.Count > 1)
                throw new AggregateException("Installation and cleanup failed. The originals are in the archive.", errors);
            throw;
        }
    }

    internal static void ReplaceFile(string destination, byte[] bytes, Action? beforeCommit = null)
    {
        var stage = destination + ".preset-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(flushToDisk: true);
            }
            beforeCommit?.Invoke();
            if (File.Exists(destination))
                File.Replace(stage, destination, null);
            else
                File.Move(stage, destination);
        }
        catch (Exception error)
        {
            try { File.Delete(stage); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
    }

    internal static byte[] CreateProfile(Dictionary<string, int> ints, float credits) =>
        Encoding.UTF8.GetBytes(Encode(new SaveModel
        {
            IntData = Entries(ints),
            FloatData = Entries(new Dictionary<string, float> { ["Credits"] = credits })
        }));

    private static SaveModel ReadPreset(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > MaxPresetBytes)
            throw new InvalidDataException("Preset must be at most 2 MiB.");

        using var textReader = new StreamReader(file, new UTF8Encoding(false, true));
        var json = textReader.ReadToEnd().Trim(' ', '\t', '\r', '\n');
        EnsureSingleObject(json);
        var quotas = new XmlDictionaryReaderQuotas
        {
            MaxDepth = 8,
            MaxNameTableCharCount = MaxPresetBytes,
            MaxStringContentLength = MaxPresetBytes
        };
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), quotas);
        var root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
        if ((string?)root.Attribute("type") != "object")
            throw new InvalidDataException("Preset must be a JSON object.");

        var groups = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var group in root.Elements())
        {
            var name = JsonName(group);
            if (name != "ints" && name != "floats" && name != "strings" && name != "bools")
                throw new InvalidDataException("Unknown preset group: " + name);
            if (groups.ContainsKey(name) || (string?)group.Attribute("type") != "object")
                throw new InvalidDataException("Preset group must be a unique object: " + name);
            groups.Add(name, group);
        }

        var ints = ReadGroup(groups, "ints", "number", value => int.Parse(value, CultureInfo.InvariantCulture));
        var floats = ReadGroup(groups, "floats", "number", value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
        var strings = ReadGroup(groups, "strings", "string", value => value);
        var bools = ReadGroup(groups, "bools", "boolean", bool.Parse);

        if (ints.ContainsKey("Credits") || strings.ContainsKey("Credits") || bools.ContainsKey("Credits"))
            throw new InvalidDataException("Credits belongs in floats.");
        if (floats.Values.Any(value => float.IsNaN(value) || float.IsInfinity(value))
            || (floats.TryGetValue("Credits", out var credits) && credits < 0))
            throw new InvalidDataException("Floats must be finite and Credits must be nonnegative.");
        if (ints.TryGetValue("pistolUnlocked", out var pistol) && pistol != 1)
            throw new InvalidDataException("The starter pistol must stay unlocked.");

        ints["pistolUnlocked"] = 1;
        if (!floats.ContainsKey("Credits"))
            floats.Add("Credits", 0);

        return new SaveModel
        {
            StringData = Entries(strings),
            IntData = Entries(ints),
            FloatData = Entries(floats),
            BoolData = Entries(bools)
        };
    }

    private static void EnsureSingleObject(string json)
    {
        // The BCL JSON reader can stop at the first root and silently ignore trailing data.
        if (json.Length == 0 || json[0] != '{')
            throw new InvalidDataException("Preset must be a JSON object.");

        var depth = 0;
        var quoted = false;
        for (var i = 0; i < json.Length; i++)
        {
            var character = json[i];
            if (quoted && character == '\\')
                i++;
            else if (character == '"')
                quoted = !quoted;
            else if (!quoted && character == '{')
                depth++;
            else if (!quoted && character == '}')
            {
                depth--;
                if (depth != 0)
                    continue;
                if (i == json.Length - 1)
                    return;
                break;
            }
        }

        throw new InvalidDataException("Preset must contain exactly one complete JSON object.");
    }

    private static Dictionary<string, T> ReadGroup<T>(
        Dictionary<string, XElement> groups, string groupName, string type, Func<string, T> parse)
    {
        var values = new Dictionary<string, T>(StringComparer.Ordinal);
        if (!groups.TryGetValue(groupName, out var group))
            return values;

        foreach (var entry in group.Elements())
        {
            var key = JsonName(entry);
            if (string.IsNullOrWhiteSpace(key) || values.ContainsKey(key) || (string?)entry.Attribute("type") != type)
                throw new InvalidDataException("Invalid or duplicate " + groupName + " entry: " + key);
            values.Add(key, parse(entry.Value));
        }
        return values;
    }

    private static string JsonName(XElement element) => (string?)element.Attribute("item") ?? element.Name.LocalName;

    private static Entry<T>[] Entries<T>(Dictionary<string, T> entries) =>
        entries.Select(pair => new Entry<T> { Key = pair.Key, Value = pair.Value }).ToArray();

    private static string Encode(SaveModel model)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(SaveModel)).WriteObject(stream, model);
        return Scramble(Encoding.UTF8.GetString(stream.ToArray()));
    }

    internal static string Scramble(string value)
    {
        var json = value.ToCharArray();
        // Disfigure's FBPP config uses this repeating XOR key for saveData.txt.
        const string key = "my-secret";
        for (var i = 0; i < json.Length; i++)
            json[i] = (char)(json[i] ^ key[i % key.Length]);
        return new string(json);
    }

    [DataContract]
    private sealed class SaveModel
    {
        [DataMember] public Entry<string>[] StringData = Array.Empty<Entry<string>>();
        [DataMember] public Entry<int>[] IntData = Array.Empty<Entry<int>>();
        [DataMember] public Entry<float>[] FloatData = Array.Empty<Entry<float>>();
        [DataMember] public Entry<bool>[] BoolData = Array.Empty<Entry<bool>>();
    }

    [DataContract]
    private sealed class Entry<T>
    {
        [DataMember] public string Key = "";
        [DataMember] public T Value = default!;
    }
}
