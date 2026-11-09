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
            CheckSaveRepair(root);
            CheckProgressionRollback(root);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        Console.WriteLine("All self-checks passed.");
    }

    private static void CheckSaveRepair(string root)
    {
        var profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        var archives = Path.Combine(root, "save-backups");
        var live = Path.Combine(profile, "saveData.txt");
        var backup = live + ".bak";
        var marker = live + ".write-in-progress";
        var request = Path.Combine(root, "BepInExMono", "config", DisfigureSavePreset.SavePreset.RequestFileName);
        var repair = new SaveRepair(root, profile, archives, _ => null, () => { });
        var starter = SaveRepair.CreateProfile(0, [], false, false);
        var custom = SaveRepair.CreateProfile(500000, SaveRepair.Weapons.Where(weapon => weapon.Id != "minigun").Select(weapon => weapon.Id),
            false, true);
        Check(repair.Live.Status == "Missing" && repair.Backup.Status == "Missing", "missing profile detection");
        Check(SaveRepair.IsProgression("profile_pref_upgrade_discovered_test_h1923") && SaveRepair.IsProgression("Credits_h42")
            && SaveRepair.IsProgression("minigunUnlocked_h3") && !SaveRepair.IsProgression("Screenmanager Resolution Width_h5")
            && !SaveRepair.IsProgression("sfxVolume_h6"), "progression scope excludes settings");
        using (var decoded = System.Text.Json.JsonDocument.Parse(DisfigureSavePreset.SavePreset.Scramble(System.Text.Encoding.UTF8.GetString(custom))))
        {
            var flags = decoded.RootElement.GetProperty("IntData").EnumerateArray()
                .ToDictionary(entry => entry.GetProperty("Key").GetString()!, entry => entry.GetProperty("Value").GetInt32());
            Check(!flags.ContainsKey("minigunUnlocked") && flags["pistolUnlocked"] == 1 &&
                flags["map2unlocked"] == 1 && flags["map3unlocked"] == 1, "custom weapon exception and map prerequisites");
        }

        File.WriteAllBytes(live, starter);
        File.WriteAllBytes(backup, starter);
        Check(!SaveRepair.RecommendBackup(repair.Live, repair.Backup), "identical backup is not recommended");
        File.WriteAllText(backup, "bad backup");
        Check(repair.Backup.Status == "Unreadable", "corrupt backup is excluded");
        File.WriteAllBytes(backup, custom);
        Check(SaveRepair.RecommendBackup(repair.Live, repair.Backup), "different valid backup is recommended for a readable save");
        File.WriteAllText(live, "damaged live file");
        File.WriteAllText(marker, "interrupted save");
        Directory.CreateDirectory(Path.GetDirectoryName(request)!);
        File.WriteAllText(request, "{}");
        Check(SaveRepair.RecommendBackup(repair.Live, repair.Backup), "backup offered when live file is corrupt");
        ExpectIoFailure(() => repair.Apply(custom, true, false), expectQueueNotice: true);
        Check(File.Exists(request) && File.ReadAllText(live) == "damaged live file", "unconfirmed queued preset leaves files alone");

        var archive = repair.Apply(custom, true, true);
        Check(repair.Live.Credits == 500000 && repair.Live.Weapons == 19, "game backup installed and verified");
        Check(File.ReadAllText(Path.Combine(archive, "saveData.txt")) == "damaged live file" &&
            File.Exists(Path.Combine(archive, "BepInExMono-queued-preset.json")) && !File.Exists(request) && !File.Exists(marker),
            "original files and queued preset archived");
        File.Delete(Path.Combine(archive, "saveData.txt"));
        ExpectIoFailure(() => repair.Undo(archive, false));
        Check(repair.Live.Credits == 500000 && repair.LatestArchive == archive, "missing archive file does not remove the repaired save");
        File.WriteAllText(Path.Combine(archive, "saveData.txt"), "damaged live file");
        File.Delete(live);
        repair.Undo(archive, false);
        Check(File.ReadAllText(live) == "damaged live file" && File.ReadAllText(marker) == "interrupted save" &&
            File.ReadAllBytes(backup).SequenceEqual(custom) && repair.LatestArchive is null && !File.Exists(request),
            "undo recovers original files after interrupted state and never re-arms preset");

        File.WriteAllText(request, "{}");
        using (File.Open(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectIoFailure(() => repair.Apply(starter, false, true));
        Check(File.ReadAllText(live) == "damaged live file" && File.ReadAllBytes(backup).SequenceEqual(custom) &&
            File.Exists(request) && repair.LatestArchive is null, "failed sidecar write rolls back live save and queue");
        using (File.Open(live, FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectIoFailure(() => repair.Apply(starter, false, true));
        Check(File.ReadAllText(live) == "damaged live file" && File.Exists(request), "locked live file is preserved");
        repair.Apply(starter, false, true);
        Check(repair.Live.Credits == 0 && repair.Live.Weapons == 1, "starter profile has pistol and zero credits");
    }

    private static void CheckProgressionRollback(string root)
    {
        const string credits = "Credits_h42";
        const string discovery = "profile_pref_upgrade_discovered_sample_h42";
        const string volume = "sfxVolume_h42";
        var registryPath = @"Software\DisfigureModManager-self-check-" + Guid.NewGuid().ToString("N");
        var currentUser = Microsoft.Win32.Registry.CurrentUser;
        var registry = currentUser.CreateSubKey(registryPath);
        try
        {
            var wallet = BitConverter.GetBytes(100f);
            registry.SetValue(credits, wallet);
            registry.SetValue(discovery, 1);
            registry.SetValue(volume, 75);
            var repair = new SaveRepair(root, Path.Combine(root, "registry-profile"), Path.Combine(root, "registry-archives"),
                writable => currentUser.OpenSubKey(registryPath, writable), () => { });
            var archive = repair.Apply(SaveRepair.CreateProfile(0, [], false, false), false, false);
            Check(registry.GetValue(credits) is null && registry.GetValue(discovery) is null &&
                (int?)registry.GetValue(volume) == 75, "only progression registry values are cleared");
            registry.SetValue(volume, 60);
            repair.Undo(archive, false);
            Check(((byte[]?)registry.GetValue(credits))?.SequenceEqual(wallet) == true &&
                (int?)registry.GetValue(discovery) == 1 && (int?)registry.GetValue(volume) == 60, "undo restores typed progression and preserves current volume");
        }
        finally
        {
            registry.Dispose();
            currentUser.DeleteSubKey(registryPath);
        }
    }

    private static void ExpectIoFailure(Action operation, bool expectQueueNotice = false)
    {
        try { operation(); }
        catch (IOException) when (!expectQueueNotice) { return; }
        catch (InvalidOperationException) when (expectQueueNotice) { return; }
        throw new InvalidOperationException("Expected save repair to fail safely.");
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
