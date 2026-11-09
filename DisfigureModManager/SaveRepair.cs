using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DisfigureSavePreset;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DisfigureModManager;

internal sealed record SaveInspection(DateTime? Modified, byte[]? Bytes, float? Credits, int Weapons, string Status)
{
    internal string Describe() => Credits is float credits
        ? $"{Modified:g} · {credits:N0} credits · {Weapons}/{SaveRepair.Weapons.Length} weapons"
        : Modified is DateTime date ? $"{Status} · {date:g}" : Status;
}

internal sealed class SaveRepair(string gameFolder, string saveDirectory, string archiveRoot,
    Func<bool, RegistryKey?> openPreferences, Action ensureClosed)
{
    private const string RegistryPath = @"Software\ColdBrew Entertainment\Disfigure";
    private const int MaxSaveBytes = 8 * 1024 * 1024;
    private const int MaxArchivedFileBytes = 64 * 1024 * 1024;
    private const int MaxPreferenceBytes = 8 * 1024 * 1024;
    private const int ValueNotFound = 2;
    private const int MoreData = 234;
    private static readonly string[] ProfileFiles = [SavePreset.SaveFileName, .. SavePreset.SidecarSuffixes.Select(suffix => SavePreset.SaveFileName + suffix)];
    private static readonly string[] LoaderRoots = ["BepInEx", "BepInExMono"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private string LatestPath => Path.Combine(archiveRoot, "latest");

    internal static readonly (string Id, string Name)[] Weapons =
    [
        ("pistol", "Pistol"), ("shotgun", "Shotgun"), ("sniper", "Sniper Rifle"), ("knife", "Knife"),
        ("katanas", "Twin Katanas"), ("greatsword", "Greatsword"), ("boomerang", "Boomerang Scythe"),
        ("repeater", "Repeater"), ("burstrifle", "Burst Rifle"), ("minigun", "Minigun"),
        ("handcannon", "Hand Cannon"), ("sawlauncher", "Saw Launcher"), ("railgun", "Railgun"),
        ("akimbosmgs", "Akimbo SMGs"), ("battleaxe", "Battle Axe"), ("pulserifle", "Pulse Rifle"),
        ("guasssword", "Gauss Katana"), ("lasergun", "Laser Catalyst"), ("grenadelauncher", "Grenade Launcher"),
        ("flamethrower", "Flamethrower")
    ];

    internal static SaveRepair ForCurrentUser(string gameFolder) => new(
        gameFolder, SavePreset.GetSaveDirectory(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisfigureModManager", "SaveBackups"),
        writable => writable ? Registry.CurrentUser.CreateSubKey(RegistryPath) : Registry.CurrentUser.OpenSubKey(RegistryPath),
        () =>
        {
            if (PluginService.IsGameRunning() || Process.GetProcessesByName("steam").Length > 0)
                throw new InvalidOperationException("Exit Disfigure and Steam completely before changing the save.");
        });

    internal string SaveDirectory => saveDirectory;
    internal SaveInspection Live => Inspect(Path.Combine(saveDirectory, ProfileFiles[0]));
    internal SaveInspection Backup => Inspect(Path.Combine(saveDirectory, ProfileFiles[1]));
    internal bool HasQueuedPreset => Enumerable.Range(0, LoaderRoots.Length).Any(index => File.Exists(QueuePath(index)));
    internal string? LatestArchive => ReadLatest() is string id ? Path.Combine(archiveRoot, id) : null;

    internal static bool RecommendBackup(SaveInspection live, SaveInspection backup) =>
        backup.Bytes is not null && (live.Bytes is null || !live.Bytes.AsSpan().SequenceEqual(backup.Bytes));

    internal static byte[] CreateProfile(float credits, IEnumerable<string> weapons, bool map2, bool map3)
    {
        if (!float.IsFinite(credits) || credits < 0)
            throw new ArgumentOutOfRangeException(nameof(credits), "Credits must be a finite nonnegative number.");

        var ints = new Dictionary<string, int>(StringComparer.Ordinal) { ["pistolUnlocked"] = 1 };
        foreach (var weapon in weapons)
        {
            if (!Weapons.Any(entry => entry.Id == weapon))
                throw new InvalidDataException("Unknown weapon: " + weapon);
            ints[weapon + "Unlocked"] = 1;
        }
        if (map2 || map3) ints["map2unlocked"] = 1;
        if (map3) ints["map3unlocked"] = 1;
        return SavePreset.CreateProfile(ints, credits);
    }

    internal string Apply(byte[] bytes, bool fromBackup, bool archiveQueued)
    {
        ReadSummary(bytes);
        return Change(archiveQueued, (snapshot, before) =>
        {
            if (fromBackup && !FileMatches(Path.Combine(snapshot, ProfileFiles[1]), bytes))
                throw new IOException("The game backup changed. Reopen Repair Save and preview it again.");
            SetFile(Path.Combine(saveDirectory, ProfileFiles[0]), bytes);
            foreach (var name in ProfileFiles.Skip(1))
                SetFile(Path.Combine(saveDirectory, name), null);
            SetPreferences(new(), before);
            ReadSummary(File.ReadAllBytes(Path.Combine(saveDirectory, ProfileFiles[0])));
        });
    }

    internal string Undo(string expectedArchive, bool archiveQueued) => Change(archiveQueued, (snapshot, before) =>
    {
        var previous = File.ReadAllText(Path.Combine(snapshot, "previous"));
        var target = previous.Length == 0 ? null : Path.Combine(archiveRoot, previous);
        if (!string.Equals(target, expectedArchive, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The latest repair changed. Reopen Repair Save and preview it again.");
        RestoreProfile(target!, before);
        File.Delete(LatestPath);
    });

    private string Change(bool archiveQueued, Action<string, Dictionary<string, Preference>> apply)
    {
        ensureClosed();
        Directory.CreateDirectory(archiveRoot);
        using var operation = new FileStream(Path.Combine(archiveRoot, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = ReadLatest();
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        var archive = Directory.CreateDirectory(Path.Combine(archiveRoot, id)).FullName;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in ProfileFiles)
        {
            CopyIfPresent(Path.Combine(saveDirectory, name), Path.Combine(archive, name));
            if (ReadOptional(Path.Combine(archive, name)) is byte[] bytes)
                files.Add(name, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        for (var i = 0; i < LoaderRoots.Length; i++)
            CopyIfPresent(QueuePath(i), ArchivedQueue(archive, i));
        var before = ReadPreferences();
        File.WriteAllText(Path.Combine(archive, "previous"), previous ?? "");
        SavePreset.ReplaceFile(Path.Combine(archive, "snapshot.json"), JsonSerializer.SerializeToUtf8Bytes(new Snapshot(saveDirectory, files, before)));

        ensureClosed();
        if (ProfileFiles.Any(name => !FileMatches(Path.Combine(saveDirectory, name), ReadOptional(Path.Combine(archive, name)))))
            throw new IOException("The save changed while it was being archived. Try again after closing the game.");
        if (!archiveQueued && HasQueuedPreset)
            throw new InvalidOperationException("A save preset was queued. Reopen Repair Save to review it.");

        // Publish the undo point before touching the profile so an interrupted process can be recovered.
        SavePreset.ReplaceFile(LatestPath, Encoding.UTF8.GetBytes(id));
        var removed = new List<int>();
        try
        {
            for (var i = 0; i < LoaderRoots.Length; i++)
            {
                var pending = ReadOptional(ArchivedQueue(archive, i));
                if (!FileMatches(QueuePath(i), pending))
                    throw new IOException("A queued preset changed. Reopen Repair Save to review it.");
                if (pending is null) continue;
                File.Delete(QueuePath(i));
                removed.Add(i);
            }
            apply(archive, before);
        }
        catch (Exception error)
        {
            try
            {
                RestoreProfile(archive, ReadPreferences());
                foreach (var index in removed)
                    SetFile(QueuePath(index), File.ReadAllBytes(ArchivedQueue(archive, index)));
                SetFile(LatestPath, previous is null ? null : Encoding.UTF8.GetBytes(previous));
            }
            catch (Exception rollback)
            {
                throw new AggregateException($"Repair could not be rolled back. The original is archived in {archive}.", error, rollback);
            }
            throw new IOException($"No repair was applied. The original is archived in {archive}. {error.Message}", error);
        }
        return archive;
    }

    private void RestoreProfile(string archive, Dictionary<string, Preference> current)
    {
        var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllBytes(Path.Combine(archive, "snapshot.json")));
        if (snapshot is null || !string.Equals(snapshot.Profile, saveDirectory, StringComparison.OrdinalIgnoreCase) ||
            snapshot.Files is null || snapshot.Files.Keys.Any(name => !ProfileFiles.Contains(name)) ||
            snapshot.Preferences is null || snapshot.Preferences.Keys.Any(name => !IsProgression(name)))
            throw new InvalidDataException("The archive does not match this Disfigure profile.");
        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var name in ProfileFiles)
        {
            var bytes = ReadOptional(Path.Combine(archive, name));
            var hash = bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
            if (hash != snapshot.Files.GetValueOrDefault(name))
                throw new InvalidDataException("Archived file is missing or changed: " + name);
            files.Add(name, bytes);
        }
        var preferences = new Dictionary<string, Preference>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in snapshot.Preferences)
            preferences.Add(name, new(value.Kind, value.GetBytes()));
        foreach (var (name, bytes) in files)
            SetFile(Path.Combine(saveDirectory, name), bytes);
        SetPreferences(preferences, current);
    }

    private string? ReadLatest()
    {
        if (!File.Exists(LatestPath)) return null;
        var id = File.ReadAllText(LatestPath);
        if (!Regex.IsMatch(id, @"\A[0-9]{8}-[0-9]{6}-[a-f0-9]{32}\z", RegexOptions.CultureInvariant) ||
            !File.Exists(Path.Combine(archiveRoot, id, "snapshot.json")))
            throw new InvalidDataException("The repair archive index is invalid.");
        return id;
    }

    private static SaveInspection Inspect(string path)
    {
        if (!File.Exists(path)) return new(null, null, null, 0, "Missing");
        var date = File.GetLastWriteTime(path);
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > MaxSaveBytes) throw new InvalidDataException("Save is too large.");
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            var (credits, weapons) = ReadSummary(bytes);
            return new(date, bytes, credits, weapons, "Readable");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            return new(date, null, null, 0, "Unreadable");
        }
    }

    private static (float Credits, int Weapons) ReadSummary(byte[] bytes)
    {
        if (bytes.Length > MaxSaveBytes) throw new InvalidDataException("Save is too large.");
        using var document = JsonDocument.Parse(SavePreset.Scramble(StrictUtf8.GetString(bytes).TrimStart('\uFEFF')));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid FBPP save.");
        var groups = new HashSet<string>(StringComparer.Ordinal);
        var owned = new HashSet<string>(StringComparer.Ordinal) { "pistol" };
        float credits = 0;
        foreach (var group in root.EnumerateObject())
        {
            if (!groups.Add(group.Name) || group.Value.ValueKind != JsonValueKind.Array ||
                group.Name is not ("IntData" or "FloatData" or "StringData" or "BoolData"))
                throw new InvalidDataException("Invalid FBPP save group.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in group.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || entry.EnumerateObject().Count() != 2 ||
                    !entry.TryGetProperty("Key", out var keyValue) || keyValue.ValueKind != JsonValueKind.String ||
                    !entry.TryGetProperty("Value", out var value) || string.IsNullOrWhiteSpace(keyValue.GetString()) ||
                    !keys.Add(keyValue.GetString()!))
                    throw new InvalidDataException("Invalid FBPP save entry.");
                var key = keyValue.GetString()!;
                switch (group.Name)
                {
                    case "IntData" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number):
                        if (number == 1 && key.EndsWith("Unlocked", StringComparison.Ordinal))
                            owned.Add(key[..^"Unlocked".Length]);
                        break;
                    case "FloatData" when value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var amount) && float.IsFinite(amount):
                        if (key == "Credits") credits = amount;
                        break;
                    case "StringData" when value.ValueKind == JsonValueKind.String:
                    case "BoolData" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        break;
                    default:
                        throw new InvalidDataException("Invalid FBPP value type.");
                }
            }
        }
        if (!groups.Contains("IntData") || !groups.Contains("StringData") || credits < 0)
            throw new InvalidDataException("Invalid FBPP model or credit balance.");
        return (credits, Weapons.Count(weapon => owned.Contains(weapon.Id)));
    }

    internal static bool IsProgression(string registryName)
    {
        var name = Regex.Replace(registryName, @"_h[0-9]+$", "", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return name is "Credits" or "selectedWeapon" or "WeaponAscensionTier" or "highscoree" or "currentRunScore" or
            "mutationMenuUnlocked" or "map2unlocked" or "map3unlocked" or "map1completehard" or "map2completehard" or
            "map3completehard" or "map3boss4cleared" or "SkipSteamUnlockPullAfterReset" ||
            name.StartsWith("profile_", StringComparison.Ordinal) ||
            name.StartsWith("ach_unlock_", StringComparison.Ordinal) ||
            name.StartsWith("progress_metric_", StringComparison.Ordinal) ||
            name.StartsWith("PermanentStatStage_", StringComparison.Ordinal) ||
            name.StartsWith("weaponUnlockFromSteamOnly_", StringComparison.Ordinal) ||
            Regex.IsMatch(name, @"\A(Mutation[0-9]+Unlocked|EquippedMutationSlot[1-5])\z", RegexOptions.CultureInvariant) ||
            Weapons.Any(weapon => new[] { "Unlocked", "RunWon", "AscensionGoldenUnlocked", "AscensionRubyUnlocked",
                "AscensionDiamondUnlocked", "AscensionSelectedTier" }.Any(suffix => name == weapon.Id + suffix));
    }

    private Dictionary<string, Preference> ReadPreferences()
    {
        using var registry = openPreferences(false);
        var values = new Dictionary<string, Preference>(StringComparer.OrdinalIgnoreCase);
        if (registry is null) return values;
        foreach (var name in registry.GetValueNames().Where(IsProgression))
        {
            var value = ReadRawPreference(registry.Handle, name);
            if (value is not null)
                values.Add(name, value);
        }
        return values;
    }

    private static Preference? ReadRawPreference(SafeRegistryHandle key, string name)
    {
        // Unity can store a value whose byte count differs from the size implied by its registry type.
        var size = 0;
        var status = RegQueryValueExW(key, name, IntPtr.Zero, out _, null, ref size);
        if (status == ValueNotFound) return null;
        if (status != 0) throw new Win32Exception(status, "Could not read progression value " + name);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (size < 0 || size > MaxPreferenceBytes)
                throw new InvalidDataException("Progression value is too large to archive: " + name);
            var bytes = new byte[size];
            status = RegQueryValueExW(key, name, IntPtr.Zero, out var kind, bytes, ref size);
            if (status == ValueNotFound) return null;
            if (status == MoreData || (status == 0 && size > bytes.Length)) continue;
            if (status != 0) throw new Win32Exception(status, "Could not read progression value " + name);
            return new(kind, size == bytes.Length ? bytes : bytes[..size]);
        }
        throw new IOException("Progression value changed while it was being read: " + name);
    }

    internal static void SetRawPreference(RegistryKey registry, string name, uint kind, byte[] bytes)
    {
        var status = RegSetValueExW(registry.Handle, name, 0, kind, bytes, bytes.Length);
        if (status != 0) throw new Win32Exception(status, "Could not restore progression value " + name);
    }

    [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueExW(SafeRegistryHandle key, string name, IntPtr reserved, out uint kind,
        [Out] byte[]? bytes, ref int size);

    [DllImport("advapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int RegSetValueExW(SafeRegistryHandle key, string name, int reserved, uint kind, byte[] bytes, int size);

    private void SetPreferences(Dictionary<string, Preference> target, Dictionary<string, Preference> current)
    {
        if (PreferencesMatch(target, current)) return;
        using var registry = openPreferences(true) ?? throw new IOException("Disfigure progression settings are unavailable.");
        foreach (var name in current.Keys.Where(name => !target.ContainsKey(name)))
            registry.DeleteValue(name, throwOnMissingValue: false);
        foreach (var (name, preference) in target)
            if (!current.TryGetValue(name, out var value) || !preference.Matches(value))
                SetRawPreference(registry, name, preference.Kind, preference.GetBytes());
        if (!PreferencesMatch(target, ReadPreferences()))
            throw new IOException("Progression settings did not match the requested profile.");
    }

    private static bool PreferencesMatch(Dictionary<string, Preference> target, Dictionary<string, Preference> actual) =>
        target.Count == actual.Count && target.All(pair => actual.TryGetValue(pair.Key, out var value) && pair.Value.Matches(value));

    private string QueuePath(int index) => Path.Combine(gameFolder, LoaderRoots[index], "config", SavePreset.RequestFileName);
    private static string ArchivedQueue(string archive, int index) => Path.Combine(archive, LoaderRoots[index] + "-queued-preset.json");
    private static byte[]? ReadOptional(string path)
    {
        if (!File.Exists(path)) return null;
        using var file = File.OpenRead(path);
        if (file.Length > MaxArchivedFileBytes) throw new IOException("File is too large to restore safely: " + path);
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }
    private static void CopyIfPresent(string source, string destination)
    {
        if (!File.Exists(source)) return;
        File.Copy(source, destination);
        if (!FileMatches(destination, ReadOptional(source))) throw new IOException("Archive verification failed for " + source);
    }
    private static bool FileMatches(string path, byte[]? bytes)
    {
        var actual = ReadOptional(path);
        return actual is null ? bytes is null : bytes is not null && actual.AsSpan().SequenceEqual(bytes);
    }

    private static void SetFile(string path, byte[]? bytes)
    {
        if (FileMatches(path, bytes)) return;
        if (bytes is null) File.Delete(path);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SavePreset.ReplaceFile(path, bytes);
        }
        if (!FileMatches(path, bytes)) throw new IOException("File verification failed for " + path);
    }

    private sealed record Snapshot(string Profile, Dictionary<string, string> Files, Dictionary<string, Preference> Preferences);
    private sealed record Preference(uint Kind, byte[]? Raw,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Value = null)
    {
        internal bool Matches(Preference other) => Kind == other.Kind && GetBytes().AsSpan().SequenceEqual(other.GetBytes());

        internal byte[] GetBytes()
        {
            if (Raw is not null)
            {
                if (Raw.Length > MaxPreferenceBytes)
                    throw new InvalidDataException("Archived progression value is too large.");
                return Raw;
            }

            // Version 1.1.0 archives contain a JSON value rather than raw registry bytes.
            var value = Value ?? throw new InvalidDataException("Archived progression value is missing.");
            return (RegistryValueKind)Kind switch
            {
                RegistryValueKind.DWord => BitConverter.GetBytes(value.GetInt32()),
                RegistryValueKind.QWord => BitConverter.GetBytes(value.GetInt64()),
                RegistryValueKind.Binary => value.GetBytesFromBase64(),
                RegistryValueKind.MultiString => Encoding.Unicode.GetBytes(string.Concat(value.Deserialize<string[]>()!
                    .Select(item => item + "\0")) + "\0"),
                RegistryValueKind.String or RegistryValueKind.ExpandString => Encoding.Unicode.GetBytes(value.GetString() + "\0"),
                _ => throw new InvalidDataException("Unsupported legacy progression value type.")
            };
        }
    }
}
