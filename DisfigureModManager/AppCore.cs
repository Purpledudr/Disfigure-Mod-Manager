using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DisfigureModManager;

internal sealed record CatalogPlugin(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("dllFilename")] string DllFilename,
    [property: JsonPropertyName("packageType")] string PackageType = "dll",
    [property: JsonPropertyName("archivePath")] string ArchivePath = "",
    [property: JsonPropertyName("available")] bool Available = true);

internal sealed record InstalledPlugin(string DllFilename, string Path, string Version, bool Enabled);

internal sealed record BepInExPackage(string Version, string Note, string DownloadUrl)
{
    public override string ToString() => $"{Version} — {Note}";
}

internal static class BepInExPackages
{
    public static readonly IReadOnlyList<BepInExPackage> All =
    [
        new(
            "6.0.0-be.785",
            "Recommended for these mods · Windows / Linux via Proton",
            "https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip"),
        new(
            "6.0.0-pre.2",
            "Official prerelease · Windows / Linux via Proton",
            "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip"),
        new(
            "6.0.0-pre.1",
            "Legacy compatibility · Windows / Linux via Proton",
            "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.1/BepInEx_UnityIL2CPP_x64_6.0.0-pre.1.zip")
    ];
}

internal sealed record PluginRow(CatalogPlugin? Catalog, InstalledPlugin? Installed)
{
    public string Key => Catalog?.Id ?? Installed!.DllFilename;
    public string Name => Catalog?.Name ?? Path.GetFileNameWithoutExtension(Installed!.DllFilename);
    public string Description => Catalog?.Description ?? "Installed manually (not present in the catalog).";
    public string InstalledVersion => Installed?.Version ?? "—";
    public string AvailableVersion => Catalog?.Version ?? "—";
    public string Status => Installed is null ? "Not installed" : Installed.Enabled ? "Enabled" : "Disabled";
    public bool HasUpdate => Catalog is { Available: true } && Installed is not null && VersionTools.Compare(Installed.Version, Catalog.Version) < 0;
}

internal sealed class UserSettings
{
    public string GameFolder { get; set; } = "";
    public string CatalogUrl { get; set; } = "";
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisfigureModManager");
    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");

    public static UserSettings Load()
    {
        UserSettings settings = new();
        try
        {
            if (File.Exists(SettingsPath))
                settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
        }
        catch (JsonException) { }

        if (string.IsNullOrWhiteSpace(settings.CatalogUrl))
        {
            try
            {
                var defaultsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (File.Exists(defaultsPath))
                    settings.CatalogUrl = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(defaultsPath), JsonOptions)?.CatalogUrl ?? "";
            }
            catch (JsonException) { }
        }

        return settings;
    }

    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

internal static class GameLocator
{
    public static bool IsGameFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "Disfigure.exe"));

    public static string? Detect(string? savedFolder)
    {
        if (IsGameFolder(savedFolder)) return Path.GetFullPath(savedFolder!);

        foreach (var steamRoot in SteamRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var library in SteamLibraries(steamRoot).Prepend(steamRoot).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(library, "steamapps", "common", "Disfigure");
                if (IsGameFolder(candidate)) return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var registryPaths = new[]
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        };

        foreach (var key in registryPaths)
        {
            var value = Registry.GetValue(key, "SteamPath", null) as string
                ?? Registry.GetValue(key, "InstallPath", null) as string;
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) yield return value;
        }

        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(standard)) yield return standard;
    }

    private static IEnumerable<string> SteamLibraries(string steamRoot)
    {
        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) yield break;

        string text;
        try { text = File.ReadAllText(vdfPath); }
        catch (IOException) { yield break; }

        foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups["path"].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path)) yield return path;
        }
    }
}

internal static class VersionTools
{
    public static int Compare(string left, string right)
    {
        var a = Parts(left);
        var b = Parts(right);
        if (a.Length == 0 || b.Length == 0)
            return a.Length.CompareTo(b.Length);
        var length = Math.Max(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var comparison = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (comparison != 0) return comparison;
        }

        var leftPre = left.Contains('-');
        var rightPre = right.Contains('-');
        return leftPre == rightPre ? 0 : leftPre ? -1 : 1;
    }

    private static int[] Parts(string value) => Regex.Matches(value.TrimStart('v', 'V').Split('-', 2)[0], @"\d+")
        .Select(match => int.TryParse(match.Value, out var part) ? part : 0)
        .ToArray();
}

internal sealed class PluginService : IDisposable
{
    private const long MaximumBepInExDownloadBytes = 250L * 1024 * 1024;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public PluginService() => http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DisfigureModManager", "1.0"));

    public static string PluginsFolder(string gameFolder) => Path.Combine(gameFolder, "BepInEx", "plugins");
    public static string DisabledFolder(string gameFolder) => Path.Combine(gameFolder, "BepInEx", "disabled_plugins");
    public static bool HasBepInEx(string gameFolder) => Directory.Exists(Path.Combine(gameFolder, "BepInEx"));

    public static bool IsGameRunning()
    {
        try { return Process.GetProcessesByName("Disfigure").Any(); }
        catch { return true; }
    }

    public static IReadOnlyList<InstalledPlugin> Scan(string gameFolder)
    {
        var found = new Dictionary<string, InstalledPlugin>(StringComparer.OrdinalIgnoreCase);
        ScanFolder(PluginsFolder(gameFolder), true, found);
        ScanFolder(DisabledFolder(gameFolder), false, found);
        return found.Values.OrderBy(plugin => plugin.DllFilename, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ScanFolder(string folder, bool enabled, IDictionary<string, InstalledPlugin> found)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
        {
            var filename = Path.GetFileName(path);
            if (!found.ContainsKey(filename))
                found[filename] = new(filename, path, ReadVersion(path), enabled);
        }
    }

    internal static string ReadVersion(string path)
    {
        try
        {
            using (var stream = File.OpenRead(path))
            using (var pe = new PEReader(stream))
            {
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.CustomAttributes)
                {
                    var attribute = metadata.GetCustomAttribute(handle);
                    if (!IsAttribute(metadata, attribute.Constructor, "BepInPlugin")) continue;
                    var value = metadata.GetBlobReader(attribute.Value);
                    if (value.ReadUInt16() != 1) continue;
                    value.ReadSerializedString();
                    value.ReadSerializedString();
                    var pluginVersion = value.ReadSerializedString();
                    if (!string.IsNullOrWhiteSpace(pluginVersion)) return pluginVersion;
                }
            }

            var fileVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            if (!string.IsNullOrWhiteSpace(fileVersion)) return fileVersion.Split('+')[0];
            return AssemblyName.GetAssemblyName(path).Version?.ToString() ?? "Unknown";
        }
        catch { return "Unknown"; }
    }

    private static bool IsAttribute(MetadataReader metadata, EntityHandle constructor, string name)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)type).Name) == name,
            HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)type).Name) == name,
            _ => false
        };
    }

    public async Task<IReadOnlyList<CatalogPlugin>> FetchCatalogAsync(string catalogUrl)
    {
        if (!Uri.TryCreate(catalogUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Enter a valid HTTPS URL for plugins.json.");

        var json = await http.GetStringAsync(uri);
        return ParseCatalog(json);
    }

    internal static IReadOnlyList<CatalogPlugin> ParseCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("plugins", out var plugins) ? plugins : default;
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The catalog must be a JSON array or an object containing a plugins array.");

        var entries = JsonSerializer.Deserialize<List<CatalogPlugin>>(element.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name) ||
                string.IsNullOrWhiteSpace(entry.Description) || string.IsNullOrWhiteSpace(entry.Version) ||
                string.IsNullOrWhiteSpace(entry.DllFilename))
                throw new InvalidDataException("Every plugin entry needs id, name, description, version, and dllFilename.");
            if (Path.GetFileName(entry.DllFilename) != entry.DllFilename || !entry.DllFilename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid DLL filename: {entry.DllFilename}");
            if (entry.PackageType is not ("dll" or "zip"))
                throw new InvalidDataException($"{entry.Name} has an unsupported packageType.");
            if (entry.Available && (!Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException($"{entry.Name} needs a valid HTTPS downloadUrl.");
            if (entry.PackageType == "zip" && entry.Available && !IsSafePluginArchivePath(entry.ArchivePath, entry.DllFilename))
                throw new InvalidDataException($"{entry.Name} needs a safe archivePath under BepInEx/plugins.");
            if (!ids.Add(entry.Id) || !filenames.Add(entry.DllFilename))
                throw new InvalidDataException($"Duplicate plugin id or DLL filename: {entry.Id}");
        }

        return entries;
    }

    public static IReadOnlyList<PluginRow> Merge(IReadOnlyList<CatalogPlugin> catalog, IReadOnlyList<InstalledPlugin> installed)
    {
        var byFilename = installed.ToDictionary(plugin => plugin.DllFilename, StringComparer.OrdinalIgnoreCase);
        var rows = catalog.Select(entry =>
        {
            byFilename.Remove(entry.DllFilename, out var match);
            return new PluginRow(entry, match);
        }).ToList();
        rows.AddRange(byFilename.Values.Select(plugin => new PluginRow(null, plugin)));
        return rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void Toggle(string gameFolder, InstalledPlugin plugin)
    {
        EnsureStopped();
        var sourceRoot = plugin.Enabled ? PluginsFolder(gameFolder) : DisabledFolder(gameFolder);
        var destinationRoot = plugin.Enabled ? DisabledFolder(gameFolder) : PluginsFolder(gameFolder);
        var relative = Path.GetRelativePath(sourceRoot, plugin.Path);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains(".."))
            throw new IOException("The plugin is outside its expected folder.");
        var destination = Path.Combine(destinationRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) throw new IOException($"A file named {plugin.DllFilename} already exists in the destination folder.");
        File.Move(plugin.Path, destination);
    }

    public static void Uninstall(InstalledPlugin plugin)
    {
        EnsureStopped();
        File.Delete(plugin.Path);
    }

    public async Task InstallOrUpdateAsync(string gameFolder, CatalogPlugin entry, InstalledPlugin? installed)
    {
        EnsureStopped();
        if (!entry.Available) throw new InvalidOperationException($"{entry.Name} is not available yet.");
        var folder = installed is null || installed.Enabled ? PluginsFolder(gameFolder) : DisabledFolder(gameFolder);
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(Path.GetTempPath(), $"Disfigure-plugin-{Guid.NewGuid():N}.{entry.PackageType}");

        try
        {
            await DownloadAsync(entry.DownloadUrl, temporary, MaximumBepInExDownloadBytes);

            if (entry.PackageType == "zip")
            {
                EnsureStopped();
                InstallPluginArchive(temporary, gameFolder, entry, installed);
            }
            else
            {
                ValidateDll(temporary, entry.Name);
                var destination = installed?.Path ?? Path.Combine(folder, entry.DllFilename);
                EnsureStopped();
                File.Move(temporary, destination, true);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task DownloadAsync(string url, string destination, long maximumBytes)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidDataException("The plugin download is unexpectedly large.");

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer)) > 0)
        {
            total += count;
            if (total > maximumBytes) throw new InvalidDataException("The plugin download is unexpectedly large.");
            await output.WriteAsync(buffer.AsMemory(0, count));
        }
    }

    private static void InstallPluginArchive(string archivePath, string gameFolder, CatalogPlugin plugin, InstalledPlugin? installed)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"Disfigure-plugin-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > 1000)
                    throw new InvalidDataException("The plugin package has an unexpected number of files.");
                long expandedBytes = 0;
                foreach (var entry in archive.Entries)
                {
                    var path = entry.FullName.Replace('\\', '/');
                    if (!IsSafeArchivePath(path)) throw new InvalidDataException("The plugin package contains an unsafe path.");
                    expandedBytes += entry.Length;
                    if (expandedBytes > 512L * 1024 * 1024) throw new InvalidDataException("The plugin package expands to an unexpected size.");
                    if (entry.Name.Length == 0 || !IsInstallablePluginPath(path)) continue;
                    var destination = Path.GetFullPath(Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar)));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }

            var stagedDll = Path.Combine(staging, plugin.ArchivePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(stagedDll)) throw new InvalidDataException($"The package does not contain {plugin.ArchivePath}.");
            ValidateDll(stagedDll, plugin.Name);

            var stagedFiles = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToArray();
            var files = stagedFiles.Select(source =>
            {
                var relative = Path.GetRelativePath(staging, source);
                var isMainDll = string.Equals(Path.GetFullPath(source), Path.GetFullPath(stagedDll), StringComparison.OrdinalIgnoreCase);
                var destination = isMainDll && installed is not null ? installed.Path : Path.Combine(gameFolder, relative);
                return (Source: source, Destination: destination);
            }).ToArray();

            var backup = Path.Combine(Path.GetTempPath(), $"Disfigure-plugin-backup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(backup);
            var written = new List<(string Destination, string? Backup)>();
            try
            {
                foreach (var file in files)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
                    string? saved = null;
                    if (File.Exists(file.Destination))
                    {
                        saved = Path.Combine(backup, Guid.NewGuid().ToString("N"));
                        File.Copy(file.Destination, saved);
                    }
                    File.Copy(file.Source, file.Destination, true);
                    written.Add((file.Destination, saved));
                }
            }
            catch
            {
                foreach (var file in written.AsEnumerable().Reverse())
                {
                    if (file.Backup is null) File.Delete(file.Destination);
                    else File.Copy(file.Backup, file.Destination, true);
                }
                throw;
            }
            finally
            {
                Directory.Delete(backup, true);
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static void ValidateDll(string path, string pluginName)
    {
        using var file = File.OpenRead(path);
        if (file.Length < 2 || file.ReadByte() != 'M' || file.ReadByte() != 'Z')
            throw new InvalidDataException($"The download for {pluginName} is not a Windows DLL.");
    }

    private static bool IsSafePluginArchivePath(string path, string filename) =>
        IsSafeArchivePath(path) && path.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith('/' + filename, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeArchivePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split('/').Contains("..");

    private static bool IsInstallablePluginPath(string path) =>
        path.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("BepInEx/translations/", StringComparison.OrdinalIgnoreCase);

    public async Task InstallBepInExAsync(string gameFolder, BepInExPackage package)
    {
        EnsureStopped();
        if (!GameLocator.IsGameFolder(gameFolder))
            throw new InvalidOperationException("Choose the folder containing Disfigure.exe first.");
        if (HasBepInEx(gameFolder))
            throw new InvalidOperationException("BepInEx is already installed.");
        if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The BepInEx package URL is invalid.");

        var archivePath = Path.Combine(Path.GetTempPath(), $"Disfigure-BepInEx-{Guid.NewGuid():N}.zip");
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumBepInExDownloadBytes)
                throw new InvalidDataException("The BepInEx download is unexpectedly large.");

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer)) > 0)
                {
                    total += count;
                    if (total > MaximumBepInExDownloadBytes)
                        throw new InvalidDataException("The BepInEx download is unexpectedly large.");
                    await destination.WriteAsync(buffer.AsMemory(0, count));
                }
            }

            EnsureStopped();
            ExtractBepInExArchive(archivePath, gameFolder);
            Directory.CreateDirectory(PluginsFolder(gameFolder));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    internal static void ExtractBepInExArchive(string archivePath, string gameFolder)
    {
        var stagingFolder = Path.Combine(Path.GetTempPath(), $"Disfigure-BepInEx-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingFolder);
        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                ValidateBepInExArchive(archive);
                archive.ExtractToDirectory(stagingFolder);
            }

            var sourceFiles = Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories).ToArray();
            var destinationRoot = Path.GetFullPath(gameFolder) + Path.DirectorySeparatorChar;
            var destinations = sourceFiles.Select(source =>
            {
                var relative = Path.GetRelativePath(stagingFolder, source);
                var destination = Path.GetFullPath(Path.Combine(gameFolder, relative));
                if (!destination.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The BepInEx archive contains an unsafe path.");
                return (Source: source, Destination: destination);
            }).ToArray();

            var existing = destinations.FirstOrDefault(file => File.Exists(file.Destination));
            if (existing != default)
                throw new IOException($"Cannot install because {Path.GetFileName(existing.Destination)} already exists. The BepInEx installation may be incomplete.");

            var createdFiles = new List<string>();
            try
            {
                foreach (var file in destinations)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
                    File.Copy(file.Source, file.Destination, overwrite: false);
                    createdFiles.Add(file.Destination);
                }
            }
            catch
            {
                foreach (var file in createdFiles.AsEnumerable().Reverse())
                    if (File.Exists(file)) File.Delete(file);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, recursive: true);
        }
    }

    internal static void ValidateBepInExArchive(ZipArchive archive)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > 5000)
            throw new InvalidDataException("The BepInEx archive has an unexpected number of files.");

        var names = new HashSet<string>(archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        var required = new[] { "winhttp.dll", "doorstop_config.ini", "BepInEx/core/BepInEx.Core.dll", "BepInEx/core/BepInEx.Unity.IL2CPP.dll" };
        if (required.Any(file => !names.Contains(file)))
            throw new InvalidDataException("The download is not a complete Windows x64 BepInEx IL2CPP package.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Contains(".."))
                throw new InvalidDataException("The BepInEx archive contains an unsafe path.");
            expandedBytes += entry.Length;
            if (expandedBytes > 1024L * 1024 * 1024)
                throw new InvalidDataException("The BepInEx archive expands to an unexpected size.");
        }
    }

    private static void EnsureStopped()
    {
        if (IsGameRunning()) throw new InvalidOperationException("Close Disfigure before changing plugins.");
    }

    public void Dispose() => http.Dispose();
}
