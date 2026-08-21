namespace DisfigureModManager;

internal static class SelfChecks
{
    public static void Run()
    {
        Check(VersionTools.Compare("1.2.0", "1.10.0") < 0, "numeric version comparison");
        Check(VersionTools.Compare("v2.0", "2.0.0") == 0, "version normalization");
        Check(VersionTools.Compare("2.0-beta", "2.0") < 0, "prerelease comparison");
        Check(VersionTools.Compare("Unknown", "1.0") < 0, "unknown installed version can update");

        const string json = """
            {"plugins":[{"id":"demo","name":"Demo","description":"Test","version":"1.0.0","downloadUrl":"https://example.com/Demo.dll","dllFilename":"Demo.dll"}]}
            """;
        var catalog = PluginService.ParseCatalog(json);
        Check(catalog.Count == 1 && catalog[0].DllFilename == "Demo.dll", "catalog parsing");
        Check(PluginService.Merge(catalog, []).Single().Status == "Not installed", "catalog merge");
        var catalogWithApp = PluginService.ParseCatalog("""
            {"applications":[{"id":"manager"}],"plugins":[{"id":"later","name":"Later","description":"Soon","version":"1.0.0","downloadUrl":"","dllFilename":"Later.dll","available":false}]}
            """);
        Check(catalogWithApp.Count == 1 && !catalogWithApp[0].Available, "application entries are separate from plugins");

        var root = Path.Combine(Path.GetTempPath(), $"DisfigureModManager-check-{Guid.NewGuid():N}");
        try
        {
            var plugins = PluginService.PluginsFolder(root);
            Directory.CreateDirectory(Path.Combine(plugins, "Nested"));
            File.WriteAllBytes(Path.Combine(root, "Disfigure.exe"), []);
            var filename = "CheckPlugin.dll";
            File.Copy(Path.Combine(AppContext.BaseDirectory, "DisfigureModManager.dll"), Path.Combine(plugins, "Nested", filename));
            var installed = PluginService.Scan(root).Single();
            Check(installed.Enabled && installed.DllFilename == filename, "recursive enabled plugin scan");
            if (PluginService.IsGameRunning())
            {
                try
                {
                    PluginService.Toggle(root, installed);
                    Check(false, "running-game protection");
                }
                catch (InvalidOperationException)
                {
                    Check(true, "running-game protection");
                }
            }
            else
            {
                PluginService.Toggle(root, installed);
                var disabled = PluginService.Scan(root).Single();
                Check(!disabled.Enabled && disabled.Path.Contains("Nested"), "disable plugin preserves subfolder");
            }

            var archivePath = Path.Combine(root, "bepinex.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                AddZipEntry(archive, "winhttp.dll");
                AddZipEntry(archive, "doorstop_config.ini");
                AddZipEntry(archive, "BepInEx/core/BepInEx.Core.dll");
                AddZipEntry(archive, "BepInEx/core/BepInEx.Unity.IL2CPP.dll");
            }

            var installFolder = Path.Combine(root, "install-target");
            Directory.CreateDirectory(installFolder);
            PluginService.ExtractBepInExArchive(archivePath, installFolder);
            Check(File.Exists(Path.Combine(installFolder, "winhttp.dll")), "BepInEx archive extraction");
            Check(BepInExPackages.All.Count >= 2 && BepInExPackages.All[0].Note.Contains("Proton"), "BepInEx package choices");

            var translationDll = Path.Combine(Directory.GetCurrentDirectory(), "DisfigureTranslationMod", "bin", "Release", "net6.0", "DisfigureTranslationMod.dll");
            if (File.Exists(translationDll))
                Check(PluginService.ReadVersion(translationDll) == "0.7.8", "BepInPlugin version detection");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        Console.WriteLine("All self-checks passed.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Self-check failed: {name}");
    }

    private static void AddZipEntry(System.IO.Compression.ZipArchive archive, string name)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.WriteByte(1);
    }
}

