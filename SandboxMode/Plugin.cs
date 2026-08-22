using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using FSG.MeshAnimator;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SandboxMode;

[BepInPlugin("casto.disfigure.sandbox-mode", "Disfigure Sandbox Mode", "0.3.70")]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        if (SandboxOverlay.FormatTime(125f) != "2:05" || SandboxOverlay.LeadingNumber("9. TEST") != 9
            || SandboxOverlay.LeadingNumber("TEST") != int.MaxValue
            || SandboxOverlay.DisplayWeaponName("katanas", new List<string>()) != "TWIN KATANAS"
            || SandboxOverlay.DisplayWeaponName("repeater", new List<string>()) != "LEVER-ACTION RIFLE"
            || SandboxOverlay.DisplayWeaponName("lasergun", new List<string>()) != "LASER CATALYST"
            || SandboxOverlay.DeselectMaxHealthChange("5. TANK", true, false, string.Empty) != -2
            || SandboxOverlay.DeselectMaxHealthChange("31. PACT", true, false, string.Empty) != -1
            || SandboxOverlay.DeselectMaxHealthChange("RADIANCE", false, false, "Max Health -1") != 1
            || Math.Abs(SandboxOverlay.WithoutAppliedChange(0.8f, -0.2f) - 1f) > 0.0001f
            || Math.Abs(SandboxOverlay.RecordedRedlineChange(0f, 1f) - 1f) > 0.0001f
            || Math.Abs(SandboxOverlay.RecordedRedlineChange(0.25f, 1f) - 0.25f) > 0.0001f
            || Math.Abs(SandboxOverlay.RedlineOwnExpChange(0.35f, 0.25f) - 0.1f) > 0.0001f
            || Math.Abs(SandboxOverlay.MissingRedlineChange(0.5f, 1f) - 0.5f) > 0.0001f
            || !SandboxOverlay.MutationStatSelfCheck()
            || !SandboxOverlay.EntryNameEquals("PERK", "perk")
            || !SandboxOverlay.EntryNameMatches("TWIN APEX", "28. TWIN APEX", "TWIN APEX")
            || SandboxOverlay.MutationKey("28. TWIN APEX") != "mutation:28. TWIN APEX"
            || SandboxOverlay.EnemySpawnDistance(0f) != 30f
            || SandboxOverlay.QueuedLabel(5) != "Queued: 5"
            || SandboxOverlay.EnemySpawnIntervalSeconds != 0.25f
            || !SandboxOverlay.SpawnDidNotIncreaseCount(2, 2)
            || SandboxOverlay.SpawnDidNotIncreaseCount(2, 3)
            || !SandboxOverlay.CanOpenSandbox(false)
            || SandboxOverlay.CanOpenSandbox(true)
            || !SandboxOverlay.ShouldSelectAll(2, 3)
            || SandboxOverlay.ShouldSelectAll(3, 3)
            || SandboxOverlay.EnemyGridColumn(9, 8) != 1
            || SandboxOverlay.EnemyGridRow(9, 8) != 1
            || SandboxOverlay.EnemyCatalogNumber("Enemy: 0enemyv2") != 0
            || SandboxOverlay.EnemyCatalogNumber("Enemy: 10enemy") != 10
            || SandboxOverlay.EnemyCatalogNumber("Enemy: map2enemy10") != 10
            || SandboxOverlay.EnemyCatalogNumber("Enemy: map3enemy2") != 2
            || Math.Abs(SandboxOverlay.EnemyPreviewScale(120f, 80f, 240f, 40f) - 0.5f) > 0.0001f
            || SandboxOverlay.SafeFileName("1. A:B") != "1. A_B"
            || !SandboxOverlay.NeedsLevelActivationNote("39. OVERARMED"))
            throw new InvalidOperationException("Sandbox self-check failed.");
        SandboxOverlay.Logger = Log;
        new Harmony("casto.disfigure.sandbox-mode").PatchAll();
        AddComponent<SandboxOverlay>();
        Log.LogInfo("Disfigure Sandbox Mode loaded. Press F5 during a run; F8 exports loaded artwork.");
    }
}

public sealed class SandboxOverlay : MonoBehaviour
{
    private enum Tab { Upgrades, Perks, Mutations, Time, Spawn }

    internal static ManualLogSource? Logger { get; set; }
    internal const float EnemySpawnIntervalSeconds = 0.25f;
    private static SandboxOverlay? Instance { get; set; }

    private readonly List<Entry> upgrades = new();
    private readonly List<Entry> perks = new();
    private readonly List<Entry> mutations = new();
    private readonly List<EnemyEntry> enemies = new();
    private readonly Queue<EnemyEntry> pendingEnemySpawns = new();
    private readonly Dictionary<string, EnemyPreview> enemyPreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedEnemyPreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Texture2D> catalogTextures = new();
    private readonly HashSet<string> selectedBySandbox = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> deselectedBySandbox = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> mutationSpeedChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> mutationSpeedBuffChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> mutationMovementMultiplierChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float[]> mutationCustomStatChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> redlineBulletDamageChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> redlineFireRateChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> redlineExpGainChanges = new(StringComparer.OrdinalIgnoreCase);
    private bool sandboxRedlineSelected;
    private bool redlineSupplementApplied;
    private float redlineBulletDamageSupplement;
    private float redlineFireRateSupplement;
    private int page;
    private Tab tab;
    private bool open;
    private float previousTimeScale = 1f;
    private float nextEnemySpawnTime;
    private bool pausedByOverlay;
    private bool cursorWasVisible;
    private CursorLockMode cursorWasLocked;
    private EventSystem? suspendedEventSystem;
    private Camera? enemyPreviewCamera;
    private Camera? previewBaseCamera;
    private UniversalAdditionalCameraData? previewBaseCameraData;
    private Material? enemyPreviewBackgroundMaterial;
    private int previewBaseCullingMask;
    private bool eventSystemWasEnabled;
    private int enemyPreviewWarmupFrames;
    private bool freezeEnemyPreviewsAfterWarmup;
    private IntPtr catalogGame;
    private cheatmanager? sandboxCheats;
    private bool previousTestMode;
    private bool previousDisableAllUnlocks;
    private string message = "F5 closes the sandbox.";
    private GUIStyle? groupStyle;
    private GUIStyle? detailTitleStyle;
    private GUIStyle? detailDescriptionStyle;
    private GUIStyle? badgeStyle;
    private GUIStyle? enemyLabelStyle;

    private void Awake() => Instance = this;

    private void Update()
    {
        var game = GameManager.instance;
        var stats = GameRefs.Instance?.playerStats;
        var upgradeScreenOpen = IsGameUpgradeScreenOpen(stats);
        var keyboard = Keyboard.current;
        if (keyboard?.f8Key?.wasPressedThisFrame == true) ExportLoadedArt();
        else if (open && upgradeScreenOpen)
        {
            previousTimeScale = Time.timeScale;
            SetOpen(false);
            message = "Sandbox closed because the game upgrade screen opened.";
            Logger?.LogInfo(message);
        }
        else if (keyboard?.f5Key?.wasPressedThisFrame == true)
        {
            if (open) SetOpen(false);
            else if (CanOpenSandbox(upgradeScreenOpen)) SetOpen(true);
            else message = "Finish the game upgrade screen before opening the sandbox.";
        }
        else if (open && keyboard?.escapeKey?.wasPressedThisFrame == true) SetOpen(false);

        MaintainGameTestMode();
        ZeroScore(game);
        MaintainRedlineSupplement(stats);

        if (!open)
        {
            if (!upgradeScreenOpen) SpawnPendingEnemy(game);
            return;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (enemyPreviewWarmupFrames > 0)
        {
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
            enemyPreviewWarmupFrames--;
            if (enemyPreviewWarmupFrames == 0) freezeEnemyPreviewsAfterWarmup = true;
        }
        else if (!freezeEnemyPreviewsAfterWarmup) PauseRun();
        if (tab != Tab.Spawn) HideEnemyPreviews();

        var pointer = game?.Pointer ?? IntPtr.Zero;
        if (pointer != catalogGame) RefreshCatalog();
    }

    private static bool IsGameUpgradeScreenOpen(PlayerStats? stats)
    {
        if (stats == null) return false;
        try { return stats.IsUpgradeChoiceUiVisible(); }
        catch
        {
            return stats.weaponUpgradeScreenObj?.activeInHierarchy == true
                || stats.mutationUpgradeScreenObj?.activeInHierarchy == true;
        }
    }

    internal static bool CanOpenSandbox(bool upgradeScreenOpen) => !upgradeScreenOpen;

    private void OnDestroy()
    {
        if (open) SetOpen(false);
        RestoreGameTestMode();
        DestroyEnemyPreviewCamera();
        DestroyEnemyPreviews();
        if (enemyPreviewBackgroundMaterial != null) Object.Destroy(enemyPreviewBackgroundMaterial);
        ClearCatalogTextures();
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        var game = GameManager.instance;
        ZeroScore(game);
        if (freezeEnemyPreviewsAfterWarmup)
        {
            FreezeWarmedEnemyPreviews();
            freezeEnemyPreviewsAfterWarmup = false;
            PauseRun();
        }
        if (open && tab == Tab.Spawn) EnsureEnemyPreviewCamera();
        else DestroyEnemyPreviewCamera();
        if (open && tab == Tab.Spawn)
        {
            var animationTime = Time.unscaledTime;
            foreach (var preview in enemyPreviews.Values)
            {
                if (!preview.Root.activeInHierarchy) continue;
                foreach (var animator in preview.MeshAnimators)
                {
                    try { animator.UpdateTick(animationTime); }
                    catch { }
                }
            }
        }
        if (open && game != null && enemyPreviewWarmupFrames == 0 && !freezeEnemyPreviewsAfterWarmup)
            Time.timeScale = 0f;
    }

    private void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
        if (open)
        {
            cursorWasVisible = Cursor.visible;
            cursorWasLocked = Cursor.lockState;
            suspendedEventSystem = EventSystem.current;
            eventSystemWasEnabled = suspendedEventSystem?.enabled == true;
            if (suspendedEventSystem != null) suspendedEventSystem.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshCatalog();
            PauseRun();
        }
        else
        {
            enemyPreviewWarmupFrames = 0;
            freezeEnemyPreviewsAfterWarmup = false;
            DestroyEnemyPreviewCamera();
            DestroyEnemyPreviews();
            ResumeRun();
            if (suspendedEventSystem != null) suspendedEventSystem.enabled = eventSystemWasEnabled;
            suspendedEventSystem = null;
            Cursor.lockState = cursorWasLocked;
            Cursor.visible = cursorWasVisible;
        }
    }

    private void PauseRun()
    {
        if (GameManager.instance == null) return;
        if (!pausedByOverlay) previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        pausedByOverlay = true;
    }

    private void ResumeRun()
    {
        if (!pausedByOverlay) return;
        Time.timeScale = previousTimeScale;
        pausedByOverlay = false;
    }

    private void OnGUI()
    {
        if (!open) return;

        EnsureStyles();
        var width = Math.Min(1720f, Screen.width - 20f);
        var height = Math.Min(1040f, Screen.height - 20f);
        var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        GUI.Box(area, string.Empty);
        var x = area.x + 16f;
        var y = area.y + 12f;
        var innerWidth = area.width - 32f;
        GUI.Label(new Rect(x, y, innerWidth - 170f, 26f), "DISFIGURE SANDBOX");
        if (GUI.Button(new Rect(x + innerWidth - 160f, y, 80f, 26f), "Reset"))
        {
            var resetStats = GameRefs.Instance?.playerStats;
            if (resetStats != null) ResetSelections(resetStats);
            else RefreshCatalog();
        }
        if (GUI.Button(new Rect(x + innerWidth - 70f, y, 70f, 26f), "Close")) SetOpen(false);
        y += 30f;
        GUI.Label(new Rect(x, y, innerWidth, 24f), message);
        y += 28f;
        DrawTabs(new Rect(x, y, innerWidth, 30f));
        y += 38f;

        var game = GameManager.instance;
        var stats = GameRefs.Instance?.playerStats;
        if (game == null || stats == null)
        {
            GUI.Label(new Rect(x, y, innerWidth, 48f), "Start a run to use sandbox controls.");
        }
        else if (tab == Tab.Time)
        {
            DrawTime(game, x, y, innerWidth);
        }
        else if (tab == Tab.Spawn)
        {
            DrawEnemies(game, x, y, innerWidth, area.yMax - y - 16f);
        }
        else
        {
            var detailWidth = Math.Min(380f, innerWidth * 0.28f);
            var listWidth = innerWidth - detailWidth - 16f;
            var hovered = DrawUpgrades(stats, x, y, listWidth, area.yMax - y - 16f);
            DrawDetails(new Rect(x + listWidth + 16f, y, detailWidth, area.yMax - y - 16f), hovered);
        }
    }

    private void DrawTabs(Rect area)
    {
        var width = area.width / 5f;
        DrawTab(Tab.Upgrades, $"Upgrades ({upgrades.Count})", new Rect(area.x, area.y, width - 4f, area.height));
        DrawTab(Tab.Perks, $"Perks ({perks.Count})", new Rect(area.x + width, area.y, width - 4f, area.height));
        DrawTab(Tab.Mutations, $"Mutations ({mutations.Count})", new Rect(area.x + width * 2f, area.y, width - 4f, area.height));
        DrawTab(Tab.Time, "Time", new Rect(area.x + width * 3f, area.y, width - 4f, area.height));
        DrawTab(Tab.Spawn, $"Spawn ({enemies.Count})", new Rect(area.x + width * 4f, area.y, width, area.height));
    }

    private void DrawTab(Tab value, string label, Rect area)
    {
        var old = GUI.enabled;
        GUI.enabled = tab != value;
        if (GUI.Button(area, label))
        {
            tab = value;
            page = 0;
        }
        GUI.enabled = old;
    }

    [HideFromIl2Cpp]
    private Entry? DrawUpgrades(PlayerStats stats, float x, float y, float width, float height)
    {
        var entries = tab == Tab.Upgrades ? upgrades : tab == Tab.Perks ? perks : mutations;
        if (entries.Count == 0)
        {
            GUI.Label(new Rect(x, y, width, 48f), tab == Tab.Mutations
                ? "No mutation components are loaded in this scene."
                : "No entries of this type are loaded in the current scene.");
            return null;
        }

        return tab == Tab.Mutations
            ? DrawMutationEntries(stats, x, y, width, height)
            : DrawGroupedEntries(stats, entries, x, y, width, height);
    }

    [HideFromIl2Cpp]
    private Entry? DrawGroupedEntries(PlayerStats stats, List<Entry> entries, float x, float y, float width, float height)
    {
        var groups = BuildGroups(entries);
        const int groupsPerPage = 6;
        const int groupsPerColumn = 3;
        const float gap = 8f;
        var pageCount = (groups.Count + groupsPerPage - 1) / groupsPerPage;
        page = Math.Clamp(page, 0, pageCount - 1);
        var start = page * groupsPerPage;
        var end = Math.Min(groups.Count, start + groupsPerPage);
        var footerHeight = pageCount > 1 ? 34f : 0f;
        var gridHeight = height - footerHeight;
        var cellWidth = (width - gap) / 2f;
        var cellHeight = (gridHeight - gap * 2f) / 3f;
        Entry? hovered = null;
        for (var i = start; i < end; i++)
        {
            var local = i - start;
            var column = local / groupsPerColumn;
            var row = local % groupsPerColumn;
            var area = new Rect(x + column * (cellWidth + gap), y + row * (cellHeight + gap), cellWidth, cellHeight);
            DrawEntryGroup(stats, groups[i], area, ref hovered);
        }
        DrawPageButtons(x, y + height - 28f, width, pageCount);
        return hovered;
    }

    [HideFromIl2Cpp]
    private void DrawEntryGroup(PlayerStats stats, EntryGroup group, Rect area, ref Entry? hovered)
    {
        GUI.Box(area, string.Empty);
        var buttonWidth = tab == Tab.Upgrades ? 96f : 0f;
        GUI.Label(new Rect(area.x + 8f, area.y + 4f, area.width - 16f - buttonWidth, 24f), group.Name, groupStyle);
        var selectedCount = 0;
        foreach (var entry in group.Entries)
            if (IsSelected(stats, entry)) selectedCount++;
        var selectAll = ShouldSelectAll(selectedCount, group.Entries.Count);
        if (tab == Tab.Upgrades && GUI.Button(new Rect(area.xMax - 102f, area.y + 3f, 94f, 23f),
                selectAll ? "Select all" : "Deselect all"))
            SetAll(stats, group, selectAll);
        if (group.Entries.Count == 0) return;

        var rowHeight = Math.Min(31f, (area.height - 32f) / group.Entries.Count);
        var iconSize = Math.Max(18f, Math.Min(27f, rowHeight - 2f));
        var rowY = area.y + 29f;
        foreach (var entry in group.Entries)
        {
            var rowArea = new Rect(area.x + 7f, rowY, area.width - 14f, rowHeight);
            if (rowArea.Contains(Event.current.mousePosition)) hovered = entry;
            DrawEntryIcon(new Rect(rowArea.x, rowArea.y + 1f, iconSize, iconSize), entry);
            var selected = IsSelected(stats, entry);
            var label = entry.IsPerk && entry.Order < int.MaxValue ? $"{entry.Order}. {entry.Name}" : entry.Name;
            var next = GUI.Toggle(new Rect(rowArea.x + iconSize + 5f, rowArea.y, rowArea.width - iconSize - 5f, rowHeight), selected, label);
            if (next != selected) SetUpgrade(stats, entry, next);
            rowY += rowHeight;
        }
    }

    [HideFromIl2Cpp]
    private void SetAll(PlayerStats stats, EntryGroup group, bool selected)
    {
        var changed = 0;
        foreach (var entry in group.Entries)
        {
            if (IsSelected(stats, entry) == selected) continue;
            SetUpgrade(stats, entry, selected);
            changed++;
        }
        message = $"{(selected ? "Enabled" : "Disabled")} {changed} upgrades in {group.Name}.";
    }

    internal static bool ShouldSelectAll(int selectedCount, int totalCount) => selectedCount < totalCount;

    [HideFromIl2Cpp]
    private void ResetSelections(PlayerStats stats)
    {
        var reset = 0;
        foreach (var entries in new[] { upgrades, perks, mutations })
            foreach (var entry in entries)
            {
                if (!IsSelected(stats, entry)) continue;
                SetUpgrade(stats, entry, false);
                reset++;
            }
        RefreshCatalog();
        message = reset == 0 ? "Refreshed; nothing was selected." : $"Reset {reset} selected upgrades, perks, and mutations.";
    }

    [HideFromIl2Cpp]
    private Entry? DrawMutationEntries(PlayerStats stats, float x, float y, float width, float height)
    {
        const float rowHeight = 38f;
        const float gap = 8f;
        const float footerHeight = 34f;
        var perColumn = Math.Max(1, (int)((height - footerHeight) / rowHeight));
        var perPage = perColumn * 2;
        var pageCount = (mutations.Count + perPage - 1) / perPage;
        page = Math.Clamp(page, 0, pageCount - 1);
        var start = page * perPage;
        var end = Math.Min(mutations.Count, start + perPage);
        var columnWidth = (width - gap) / 2f;
        Entry? hovered = null;
        for (var i = start; i < end; i++)
        {
            var local = i - start;
            var column = local / perColumn;
            var row = local % perColumn;
            var rowArea = new Rect(x + column * (columnWidth + gap), y + row * rowHeight, columnWidth, rowHeight - 2f);
            if (rowArea.Contains(Event.current.mousePosition)) hovered = mutations[i];
            DrawEntryIcon(new Rect(rowArea.x, rowArea.y, 34f, 34f), mutations[i]);
            var selected = IsSelected(stats, mutations[i]);
            var next = GUI.Toggle(new Rect(rowArea.x + 40f, rowArea.y + 3f, rowArea.width - 40f, 29f), selected, mutations[i].Name);
            if (next != selected) SetUpgrade(stats, mutations[i], next);
        }
        DrawPageButtons(x, y + height - 28f, width, pageCount);
        return hovered;
    }

    [HideFromIl2Cpp]
    private static List<EntryGroup> BuildGroups(List<Entry> entries)
    {
        var groups = new List<EntryGroup>();
        foreach (var entry in entries)
        {
            var name = string.IsNullOrWhiteSpace(entry.Group) ? "OTHER" : entry.Group;
            var group = groups.Find(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new EntryGroup(name, new List<Entry>());
                groups.Add(group);
            }
            group.Entries.Add(entry);
        }
        return groups;
    }

    [HideFromIl2Cpp]
    private void DrawDetails(Rect area, Entry? entry)
    {
        GUI.Box(area, string.Empty);
        var x = area.x + 14f;
        var y = area.y + 12f;
        var width = area.width - 28f;
        if (entry == null)
        {
            GUI.Label(new Rect(x, y, width, 48f), "Hover over an upgrade, perk, or mutation to see its details.", detailDescriptionStyle);
            return;
        }

        DrawEntryIcon(new Rect(x, y, 96f, 96f), entry);
        y += 106f;
        GUI.Label(new Rect(x, y, width, 52f), entry.Name, detailTitleStyle);
        y += 54f;
        if (!string.IsNullOrWhiteSpace(entry.Group))
        {
            GUI.Label(new Rect(x, y, width, 26f), entry.Group, groupStyle);
            y += 30f;
        }
        GUI.Label(new Rect(x, y, width, Math.Max(40f, area.yMax - y - 12f)),
            string.IsNullOrWhiteSpace(entry.Description) ? "No description available." : entry.Description,
            detailDescriptionStyle);
    }

    [HideFromIl2Cpp]
    private void DrawEntryIcon(Rect area, Entry entry)
    {
        if (entry.Icon != null)
        {
            var iconArea = entry.Mutation != null
                ? new Rect(area.x + area.width * 0.08f, area.y + area.height * 0.08f, area.width * 0.84f, area.height * 0.84f)
                : area;
            GUI.DrawTexture(iconArea, entry.Icon, ScaleMode.ScaleToFit, true);
        }
        if (entry.Border != null) GUI.DrawTexture(area, entry.Border, ScaleMode.ScaleToFit, true);
        if (!string.IsNullOrWhiteSpace(entry.Badge)) GUI.Label(area, entry.Badge, badgeStyle);
    }

    private void EnsureStyles()
    {
        groupStyle ??= WhiteStyle(15, FontStyle.Bold, TextAnchor.UpperLeft, false);
        detailTitleStyle ??= WhiteStyle(20, FontStyle.Bold, TextAnchor.UpperLeft, true);
        detailDescriptionStyle ??= WhiteStyle(15, FontStyle.Normal, TextAnchor.UpperLeft, true);
        badgeStyle ??= WhiteStyle(15, FontStyle.Bold, TextAnchor.MiddleCenter, false);
        enemyLabelStyle ??= WhiteStyle(13, FontStyle.Bold, TextAnchor.MiddleCenter, true);
    }

    private static GUIStyle WhiteStyle(int size, FontStyle fontStyle, TextAnchor alignment, bool wordWrap)
    {
        var style = new GUIStyle { fontSize = size, fontStyle = fontStyle, alignment = alignment, wordWrap = wordWrap, richText = true };
        style.normal.textColor = Color.white;
        style.hover.textColor = Color.white;
        style.active.textColor = Color.white;
        style.focused.textColor = Color.white;
        return style;
    }

    [HideFromIl2Cpp]
    private void SetUpgrade(PlayerStats stats, Entry entry, bool selected)
    {
        try
        {
            MaintainGameTestMode();
            entry.Upgrade.pS = stats;
            var beforeStatsMax = stats.maxHealth;
            var beforeIconMax = stats.healthicons?.maxHealth ?? beforeStatsMax;
            var beforeCurrentHealth = stats.healthicons?.currentHealth ?? 0;
            var beforeSpeed = stats.speed;
            var beforeSpeedBuff = stats.speedBuff;
            var playerMove = GameRefs.Instance?.playerMove;
            var beforeMovementMultiplier = playerMove?.movementMultiplier ?? 0f;
            var beforeBulletDamageBuff = stats.bulletDamageBuff;
            var beforeFireRateBuff = stats.fireRateBuff;
            var beforeExpGainBuff = stats.expGainBuff;
            var beforeProtocolExpGain = stats.protocolExpGainDeltaApplied;
            var beforeCustomStats = CaptureMutationStats(stats, GameManager.instance);
            var beforeMutationCount = stats.currentMutationCount;
            var isRedline = entry.Mutation != null && entry.Name.Contains("REDLINE", StringComparison.OrdinalIgnoreCase);
            var redlineWasActive = isRedline && stats.redlineActive;
            if (selected)
            {
                if (entry.Perk != null)
                {
                    entry.Perk.weaponUpgradeStat();
                    SyncEntryName(stats.unlockedWeaponPerksList, entry);
                }
                else if (entry.Mutation != null) entry.Mutation.mutationUpgradeStat();
                else entry.Upgrade.UpgradeStat(true);
                if (entry.Perk == null) SyncEntryName(stats.chosenUpgrades, entry);
                if (DeselectMaxHealthChange(entry.Name, entry.Mutation != null, entry.Perk != null, entry.Description) == -2)
                {
                    SetMaxHealth(stats, beforeStatsMax + 2, beforeIconMax + 2,
                        Math.Min(beforeIconMax + 2, beforeCurrentHealth + 2));
                }
                if (entry.Mutation != null)
                {
                    mutationSpeedChanges[entry.Key] = stats.speed - beforeSpeed;
                    mutationSpeedBuffChanges[entry.Key] = stats.speedBuff - beforeSpeedBuff;
                    if (playerMove != null)
                        mutationMovementMultiplierChanges[entry.Key] = playerMove.movementMultiplier - beforeMovementMultiplier;
                    LogMovement("applied", entry.Name, stats, playerMove, beforeSpeed, beforeSpeedBuff, beforeMovementMultiplier);
                    if (!isRedline)
                    {
                        var changes = StatDifferences(CaptureMutationStats(stats, GameManager.instance), beforeCustomStats);
                        changes[6] -= stats.protocolExpGainDeltaApplied - beforeProtocolExpGain;
                        mutationCustomStatChanges[entry.Key] = changes;
                        LogMutationStats("applied", entry.Name, changes);
                    }
                    if (isRedline)
                    {
                        var bulletDamageChange = RecordedRedlineChange(stats.bulletDamageBuff - beforeBulletDamageBuff, 0.5f);
                        var fireRateChange = RecordedRedlineChange(stats.fireRateBuff - beforeFireRateBuff, 0.5f);
                        redlineBulletDamageChanges[entry.Key] = bulletDamageChange;
                        redlineFireRateChanges[entry.Key] = fireRateChange;
                        redlineExpGainChanges[entry.Key] = RedlineOwnExpChange(
                            stats.expGainBuff - beforeExpGainBuff,
                            stats.protocolExpGainDeltaApplied - beforeProtocolExpGain);
                        redlineBulletDamageSupplement = MissingRedlineChange(bulletDamageChange, 1f);
                        redlineFireRateSupplement = MissingRedlineChange(fireRateChange, 0.8f);
                        sandboxRedlineSelected = true;
                        MaintainRedlineSupplement(stats);
                        LogRedline("selected", stats, beforeBulletDamageBuff, beforeFireRateBuff, beforeExpGainBuff);
                    }
                }
                selectedBySandbox.Add(entry.Key);
                deselectedBySandbox.Remove(entry.Key);
            }
            else
            {
                if (!isRedline) UnapplyUpgradeStats(stats, entry.Upgrade);
                if (entry.Mutation != null)
                {
                    if (mutationSpeedChanges.Remove(entry.Key, out var appliedSpeedChange))
                        stats.speed = WithoutAppliedChange(beforeSpeed, appliedSpeedChange);
                    if (mutationSpeedBuffChanges.Remove(entry.Key, out var appliedSpeedBuffChange))
                        stats.speedBuff = WithoutAppliedChange(beforeSpeedBuff, appliedSpeedBuffChange);
                    if (playerMove != null && mutationMovementMultiplierChanges.Remove(entry.Key, out var appliedMovementMultiplierChange))
                        playerMove.movementMultiplier = WithoutAppliedChange(beforeMovementMultiplier, appliedMovementMultiplierChange);
                    LogMovement("removed", entry.Name, stats, playerMove, beforeSpeed, beforeSpeedBuff, beforeMovementMultiplier);
                    if (!isRedline && mutationCustomStatChanges.Remove(entry.Key, out var customChanges))
                    {
                        RestoreMutationStats(stats, GameManager.instance, beforeCustomStats, customChanges);
                        LogMutationStats("removed", entry.Name, customChanges);
                    }
                }
                if (isRedline)
                {
                    if (redlineBulletDamageChanges.Remove(entry.Key, out var bulletDamageChange) && redlineWasActive)
                        stats.bulletDamageBuff = WithoutAppliedChange(beforeBulletDamageBuff, bulletDamageChange);
                    if (redlineFireRateChanges.Remove(entry.Key, out var fireRateChange) && redlineWasActive)
                        stats.fireRateBuff = WithoutAppliedChange(beforeFireRateBuff, fireRateChange);
                    if (redlineExpGainChanges.Remove(entry.Key, out var expGainChange) && redlineWasActive)
                        stats.expGainBuff = WithoutAppliedChange(beforeExpGainBuff, expGainChange);
                    sandboxRedlineSelected = false;
                    MaintainRedlineSupplement(stats);
                    LogRedline(redlineWasActive ? "active buff removed" : "inactive; no buff removed",
                        stats, beforeBulletDamageBuff, beforeFireRateBuff, beforeExpGainBuff);
                }
                stats.removeUpgrade(entry.Upgrade);
                RemoveEntryNames(entry.Perk != null ? stats.unlockedWeaponPerksList : stats.chosenUpgrades, entry);
                if (entry.Mutation != null)
                {
                    stats.currentMutationCount = Math.Max(0, beforeMutationCount - 1);
                    DisableMutationEffect(stats, entry.Name);
                }
                var maxHealthChange = DeselectMaxHealthChange(entry.Name, entry.Mutation != null, entry.Perk != null, entry.Description);
                if (maxHealthChange == -2) SetMaxHealth(stats, beforeStatsMax - 2, beforeIconMax - 2,
                    Math.Min(beforeCurrentHealth, beforeIconMax - 2));
                else if (maxHealthChange == -1) SetMaxHealth(stats, beforeStatsMax - 1, beforeIconMax - 1,
                    Math.Min(beforeCurrentHealth, beforeIconMax - 1));
                else if (maxHealthChange == 1) SetMaxHealth(stats, beforeStatsMax + 1, beforeIconMax + 1,
                    Math.Min(beforeIconMax + 1, beforeCurrentHealth + 1));
                if (entry.Name.Contains("LAST STAND", StringComparison.OrdinalIgnoreCase)) HealToFull(stats);
                selectedBySandbox.Remove(entry.Key);
                deselectedBySandbox.Add(entry.Key);
            }
            if (open) Time.timeScale = 0f;
            message = $"{(selected ? "Enabled" : "Disabled")} {entry.Name}.";
            Logger?.LogInfo(message);
        }
        catch (Exception exception)
        {
            message = $"Could not change {entry.Name}; see LogOutput.log.";
            Logger?.LogError($"Sandbox upgrade change failed for {entry.Name}: {exception}");
            RefreshCatalog();
        }
    }

    private static void UnapplyUpgradeStats(PlayerStats stats, Upgrade upgrade)
    {
        UnapplyStat(stats, upgrade, upgrade.statName, upgrade.change);
        UnapplyStat(stats, upgrade, upgrade.statName2, upgrade.change2);
        UnapplyStat(stats, upgrade, upgrade.statName3, upgrade.change3);
        UnapplyStat(stats, upgrade, upgrade.statName4, upgrade.change4);
        UnapplyStat(stats, upgrade, upgrade.statName5, upgrade.change5);
    }

    internal static float WithoutAppliedChange(float current, float appliedChange) => current - appliedChange;

    internal static float RecordedRedlineChange(float actualChange, float fallback) =>
        Math.Abs(actualChange) > 0.0001f ? actualChange : fallback;

    internal static float RedlineOwnExpChange(float totalExpChange, float protocolExpChange) =>
        RecordedRedlineChange(totalExpChange - protocolExpChange, 0.1f);

    internal static float MissingRedlineChange(float appliedChange, float describedChange) =>
        Math.Max(0f, describedChange - appliedChange);

    [HideFromIl2Cpp]
    private static float[] CaptureMutationStats(PlayerStats stats, GameManager? game) =>
    new[]
    {
        stats.bulletDamageBuff, stats.fireRateBuff, stats.bulletSizeBuff, stats.bulletSpeedBuff,
        stats.circleVisionBuff, stats.coneVisionBuff, stats.expGainBuff, stats.bossHealthBuff,
        game?.enemySpawnRateBuff ?? 0f, game?.enemySpeedBuff ?? 0f
    };

    [HideFromIl2Cpp]
    internal static float[] StatDifferences(float[] after, float[] before)
    {
        var changes = new float[after.Length];
        for (var i = 0; i < changes.Length; i++) changes[i] = after[i] - before[i];
        return changes;
    }

    [HideFromIl2Cpp]
    internal static bool MutationStatSelfCheck()
    {
        var changes = StatDifferences(new[] { 1.5f, 0.8f }, new[] { 1f, 1f });
        return Math.Abs(changes[0] - 0.5f) < 0.0001f && Math.Abs(changes[1] + 0.2f) < 0.0001f;
    }

    [HideFromIl2Cpp]
    private static void RestoreMutationStats(PlayerStats stats, GameManager? game, float[] beforeUndo, float[] applied)
    {
        stats.bulletDamageBuff = WithoutAppliedChange(beforeUndo[0], applied[0]);
        stats.fireRateBuff = WithoutAppliedChange(beforeUndo[1], applied[1]);
        stats.bulletSizeBuff = WithoutAppliedChange(beforeUndo[2], applied[2]);
        stats.bulletSpeedBuff = WithoutAppliedChange(beforeUndo[3], applied[3]);
        stats.circleVisionBuff = WithoutAppliedChange(beforeUndo[4], applied[4]);
        stats.coneVisionBuff = WithoutAppliedChange(beforeUndo[5], applied[5]);
        stats.expGainBuff = WithoutAppliedChange(beforeUndo[6], applied[6]);
        stats.bossHealthBuff = WithoutAppliedChange(beforeUndo[7], applied[7]);
        if (game == null) return;
        game.enemySpawnRateBuff = WithoutAppliedChange(beforeUndo[8], applied[8]);
        game.enemySpeedBuff = WithoutAppliedChange(beforeUndo[9], applied[9]);
    }

    [HideFromIl2Cpp]
    private static void LogMutationStats(string action, string name, float[] changes)
    {
        Logger?.LogInfo($"Mutation custom stats {action} {name}: damage={changes[0]:+0.###;-0.###;0}, " +
            $"fireRate={changes[1]:+0.###;-0.###;0}, size={changes[2]:+0.###;-0.###;0}, " +
            $"speed={changes[3]:+0.###;-0.###;0}, circle={changes[4]:+0.###;-0.###;0}, " +
            $"cone={changes[5]:+0.###;-0.###;0}, exp={changes[6]:+0.###;-0.###;0}, " +
            $"bossHealth={changes[7]:+0.###;-0.###;0}, spawnRate={changes[8]:+0.###;-0.###;0}, " +
            $"enemySpeed={changes[9]:+0.###;-0.###;0}.");
    }

    private void MaintainRedlineSupplement(PlayerStats? stats)
    {
        if (stats == null || GameManager.instance?.Pointer != catalogGame) return;
        var shouldApply = sandboxRedlineSelected && stats.redlineActive;
        if (shouldApply == redlineSupplementApplied) return;
        var direction = shouldApply ? 1f : -1f;
        stats.bulletDamageBuff += direction * redlineBulletDamageSupplement;
        stats.fireRateBuff += direction * redlineFireRateSupplement;
        redlineSupplementApplied = shouldApply;
        Logger?.LogInfo($"Redline supplement {(shouldApply ? "applied" : "removed")}: " +
            $"Bullet Damage {redlineBulletDamageSupplement:+0%;-0%;0%}, Fire Rate {redlineFireRateSupplement:+0%;-0%;0%}.");
    }

    private static void LogMovement(string action, string name, PlayerStats stats, PlayerMove? playerMove,
        float oldSpeed, float oldSpeedBuff, float oldMovementMultiplier)
    {
        Logger?.LogInfo($"Mutation movement {action} {name}: speed {oldSpeed:0.###}->{stats.speed:0.###}, " +
            $"speedBuff {oldSpeedBuff:0.###}->{stats.speedBuff:0.###}, movementMultiplier " +
            $"{oldMovementMultiplier:0.###}->{(playerMove?.movementMultiplier ?? 0f):0.###}, effective {stats.getSpeed():0.###}");
    }

    private static void LogRedline(string action, PlayerStats stats, float oldBulletDamageBuff, float oldFireRateBuff,
        float oldExpGainBuff)
    {
        Logger?.LogInfo($"Redline {action}: active={stats.redlineActive}, mutations={stats.currentMutationCount}, " +
            $"bulletDamageBuff {oldBulletDamageBuff:0.###}->{stats.bulletDamageBuff:0.###}, " +
            $"fireRateBuff {oldFireRateBuff:0.###}->{stats.fireRateBuff:0.###}, " +
            $"expGainBuff {oldExpGainBuff:0.###}->{stats.expGainBuff:0.###}, " +
            $"protocolExp={stats.protocolExpGainDeltaApplied:0.###}");
    }

    private static void UnapplyStat(PlayerStats stats, Upgrade upgrade, string stat, float change)
    {
        if (!string.IsNullOrWhiteSpace(stat) && Math.Abs(change) > float.Epsilon)
            stats.upgradeStat(stat, -change, upgrade.gameObject, false);
    }

    private static void DisableMutationEffect(PlayerStats stats, string name)
    {
        if (name.Contains("VITALITY", StringComparison.OrdinalIgnoreCase)) stats.vitality = false;
        else if (name.Contains("LOOSE CANNON", StringComparison.OrdinalIgnoreCase)) stats.loosecannon = false;
        else if (name.Contains("BEHEMOTH", StringComparison.OrdinalIgnoreCase)) stats.behemoth = false;
        else if (name.Contains("ROBUST", StringComparison.OrdinalIgnoreCase)) stats.robust = false;
        else if (name.Contains("TANK", StringComparison.OrdinalIgnoreCase)) stats.tank = false;
        else if (name.Contains("AGILITY", StringComparison.OrdinalIgnoreCase)) stats.agility = false;
        else if (name.Contains("ELEMENTAL", StringComparison.OrdinalIgnoreCase)) stats.elemental = false;
        else if (name.Contains("REFINE", StringComparison.OrdinalIgnoreCase)) stats.refine = false;
        else if (name.Contains("CRACK SHOTS", StringComparison.OrdinalIgnoreCase)) stats.crackshots = false;
        else if (name.Contains("COLOSSAL", StringComparison.OrdinalIgnoreCase)) stats.colossal = false;
        else if (name.Contains("VISION BATTERY", StringComparison.OrdinalIgnoreCase)) stats.visionbattery = false;
        else if (name.Contains("CRYOGENIC", StringComparison.OrdinalIgnoreCase)) stats.cryogenic = false;
        else if (name.Contains("DEADZONE", StringComparison.OrdinalIgnoreCase)) stats.anchored = false;
        else if (name.Contains("HELL SHOT", StringComparison.OrdinalIgnoreCase)) stats.hellshot = false;
        else if (name.Contains("IRON LUNG", StringComparison.OrdinalIgnoreCase)) stats.ironlung = false;
        else if (name.Contains("CRYO SHOT", StringComparison.OrdinalIgnoreCase)) stats.cryoshot = false;
        else if (name.Contains("WITHER", StringComparison.OrdinalIgnoreCase)) stats.wither = false;
        else if (name.Contains("LAST STAND", StringComparison.OrdinalIgnoreCase)) stats.laststand = false;
        else if (name.Contains("FLASHPOINT", StringComparison.OrdinalIgnoreCase)) stats.flashpoint = false;
        else if (name.Contains("TRIPLE TAKE", StringComparison.OrdinalIgnoreCase)) stats.tripletake = false;
        else if (name.Contains("MORPHIC ORBIT", StringComparison.OrdinalIgnoreCase)) stats.morphicorbit = false;
        else if (name.Contains("SOLO VISION", StringComparison.OrdinalIgnoreCase)) stats.solovision = false;
        else if (name.Contains("VISION ECHO", StringComparison.OrdinalIgnoreCase)) stats.visionecho = false;
        else if (name.Contains("TWIN APEX", StringComparison.OrdinalIgnoreCase)) stats.apex = false;
        else if (name.Contains("REDLINE", StringComparison.OrdinalIgnoreCase)) { stats.redline = false; stats.redlineActive = false; }
        else if (name.Contains("UNITY", StringComparison.OrdinalIgnoreCase)) stats.dualVision = false;
        else if (name.Contains("PACT", StringComparison.OrdinalIgnoreCase)) stats.pact = false;
        else if (name.Contains("FORGE", StringComparison.OrdinalIgnoreCase)) stats.forge = false;
        else if (name.Contains("REGEN", StringComparison.OrdinalIgnoreCase)) stats.regen = false;
    }

    internal static int DeselectMaxHealthChange(string name, bool isMutation, bool isPerk, string description)
    {
        if (isMutation && name.Contains("TANK", StringComparison.OrdinalIgnoreCase)) return -2;
        if (isMutation && name.Contains("PACT", StringComparison.OrdinalIgnoreCase)) return -1;
        if (isMutation || isPerk) return 0;
        var costsMaxHealth = description.Contains("MAX HEALTH", StringComparison.OrdinalIgnoreCase)
            && (description.Contains("-1", StringComparison.Ordinal) || description.Contains("- 1", StringComparison.Ordinal)
                || description.Contains("−1", StringComparison.Ordinal));
        return costsMaxHealth || name.Equals("RADIANCE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SUPERNOVA", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static void SetMaxHealth(PlayerStats stats, int statsMax, int iconMax, int currentHealth)
    {
        stats.maxHealth = Math.Max(1, statsMax);
        var icons = stats.healthicons;
        if (icons == null) return;
        icons.maxHealth = Math.Max(1, iconMax);
        icons.currentHealth = Math.Clamp(currentHealth, 0, icons.maxHealth);
        RefreshHealthImages(icons);
    }

    private static void RefreshHealthImages(healthicons icons)
    {
        icons.updateMaxHealthImages();
        SetHeartVisibility(icons.hearts, icons.currentHealth);
        SetHeartVisibility(icons.bgHearts, icons.maxHealth);
        Logger?.LogInfo($"Health UI synced: current={icons.currentHealth}, max={icons.maxHealth}, "
            + $"full images={icons.hearts?.Count ?? 0}, background images={icons.bgHearts?.Count ?? 0}.");
    }

    private static void SetHeartVisibility(Il2CppSystem.Collections.Generic.List<Image>? images, int visibleCount)
    {
        if (images == null) return;
        for (var i = 0; i < images.Count; i++)
            if (images[i] != null) images[i].gameObject.SetActive(i < visibleCount);
    }

    private static void HealToFull(PlayerStats stats)
    {
        var maxHealth = stats.getMaxHealth();
        (stats.wM ?? GameRefs.Instance?.weaponManager)?.setCurrentHealth(maxHealth);
        var icons = stats.healthicons;
        if (icons == null) return;
        icons.currentHealth = maxHealth;
        RefreshHealthImages(icons);
    }

    private static void ZeroScore(GameManager? game)
    {
        var score = game?.sT;
        if (score == null || score.score == 0 && score.creditEligibleScore == 0 && score.killScore == 0
            && score.minibossKillScore == 0 && score.bossKillScore == 0 && score.levelupScore == 0) return;
        score.score = 0;
        score.creditEligibleScore = 0;
        score.killScore = 0;
        score.minibossKillScore = 0;
        score.bossKillScore = 0;
        score.levelupScore = 0;
        score.SnapDisplayToScore();
        score.MirrorCurrentRunScoreForCrashProtection();
    }

    private void DrawTime(GameManager game, float x, float y, float width)
    {
        GUI.Label(new Rect(x, y, width, 24f), $"Run time: {FormatTime(game.timePassed)} ({game.timePassed:0}s)");
        y += 30f;
        var changed = GUI.HorizontalSlider(new Rect(x, y, width, 22f), game.timePassed, 0f, 3600f);
        if (Math.Abs(changed - game.timePassed) >= 1f) SetTime(game, (float)Math.Round(changed));
        y += 32f;
        var buttonWidth = (width - 12f) / 4f;
        if (GUI.Button(new Rect(x, y, buttonWidth, 28f), "-60s")) SetTime(game, game.timePassed - 60f);
        if (GUI.Button(new Rect(x + buttonWidth + 4f, y, buttonWidth, 28f), "-10s")) SetTime(game, game.timePassed - 10f);
        if (GUI.Button(new Rect(x + (buttonWidth + 4f) * 2f, y, buttonWidth, 28f), "+10s")) SetTime(game, game.timePassed + 10f);
        if (GUI.Button(new Rect(x + (buttonWidth + 4f) * 3f, y, buttonWidth, 28f), "+60s")) SetTime(game, game.timePassed + 60f);
        y += 36f;
        var times = new[] { 0, 5, 10, 15, 20, 30 };
        buttonWidth = (width - 20f) / times.Length;
        for (var i = 0; i < times.Length; i++)
            if (GUI.Button(new Rect(x + i * (buttonWidth + 4f), y, buttonWidth, 28f), $"{times[i]}:00")) SetTime(game, times[i] * 60f);
        y += 40f;
        GUI.Label(new Rect(x, y, width, 48f), "Time jumps use GameManager.timePassed, so the game will process its normal scheduled events after the sandbox closes.");
    }

    private void SetTime(GameManager game, float seconds)
    {
        game.timePassed = Math.Clamp(seconds, 0f, 3600f);
        message = $"Run time set to {FormatTime(game.timePassed)}.";
    }

    private void DrawEnemies(GameManager game, float x, float y, float width, float height)
    {
        if (enemies.Count == 0)
        {
            GUI.Label(new Rect(x, y, width, 24f), "The current map did not expose any regular enemy prefabs.");
            return;
        }

        GUI.Label(new Rect(x, y, width - 130f, 24f), "Queue regular enemies or minibosses; they spawn when the menu closes.");
        GUI.Label(new Rect(x + width - 120f, y, 120f, 24f), QueuedLabel(pendingEnemySpawns.Count));
        const float headerHeight = 30f;
        const float footerHeight = 34f;
        const float cardHeight = 270f;
        const float minCardWidth = 250f;
        const float gap = 8f;
        var columns = Math.Max(1, Math.Min(6, (int)((width + gap) / (minCardWidth + gap))));
        var rows = Math.Max(1, Math.Min(2, (int)((height - headerHeight - footerHeight + gap) / (cardHeight + gap))));
        var perPage = columns * rows;
        var pageCount = (enemies.Count + perPage - 1) / perPage;
        page = Math.Clamp(page, 0, pageCount - 1);
        var start = page * perPage;
        var end = Math.Min(enemies.Count, start + perPage);
        var cardWidth = (width - gap * (columns - 1)) / columns;
        var visiblePreviews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = start; i < end; i++)
        {
            var entry = enemies[i];
            visiblePreviews.Add(entry.Name);
            var local = i - start;
            var area = new Rect(
                x + EnemyGridColumn(local, columns) * (cardWidth + gap),
                y + headerHeight + EnemyGridRow(local, columns) * (cardHeight + gap),
                cardWidth, cardHeight);
            GUI.Box(area, string.Empty);
            GUI.Label(new Rect(area.x + 6f, area.y + 3f, area.width - 12f, 32f), entry.Name, enemyLabelStyle);
            var imageArea = new Rect(area.x + 8f, area.y + 38f, area.width - 16f, area.height - 76f);
            ShowEnemyPreview(entry, imageArea);
            var buttonWidth = (area.width - 20f) / 2f;
            if (GUI.Button(new Rect(area.x + 8f, area.yMax - 31f, buttonWidth, 25f), "+1")) QueueEnemy(entry, 1);
            if (GUI.Button(new Rect(area.x + 12f + buttonWidth, area.yMax - 31f, buttonWidth, 25f), "+5")) QueueEnemy(entry, 5);
        }
        foreach (var preview in enemyPreviews)
            if (!visiblePreviews.Contains(preview.Key))
            {
                if (preview.Value.Root.activeSelf) preview.Value.Root.SetActive(false);
                if (preview.Value.Background.activeSelf) preview.Value.Background.SetActive(false);
            }
        DrawPageButtons(x, y + height - 28f, width, pageCount);
    }

    [HideFromIl2Cpp]
    private void QueueEnemy(EnemyEntry entry, int count)
    {
        for (var i = 0; i < count; i++) pendingEnemySpawns.Enqueue(entry);
        message = $"Queued {count} {entry.Name} ({pendingEnemySpawns.Count} total); close the menu to spawn them.";
    }

    [HideFromIl2Cpp]
    private void SpawnPendingEnemy(GameManager? game)
    {
        if (game == null || pendingEnemySpawns.Count == 0 || Time.unscaledTime < nextEnemySpawnTime) return;
        var entry = pendingEnemySpawns.Dequeue();
        nextEnemySpawnTime = Time.unscaledTime + EnemySpawnIntervalSeconds;
        try
        {
            var before = game.enemiesOnScreen;
            var spawned = entry.MiniBoss == null
                ? SpawnRegularEnemy(game, entry, before)
                : SpawnMiniBoss(game, entry);
            message = spawned ? $"Spawned {entry.Name}." : $"The game could not spawn {entry.Name}.";
            Logger?.LogInfo($"{message} Enemies on screen: {before}->{game.enemiesOnScreen}. Queue: {pendingEnemySpawns.Count}.");
        }
        catch (Exception exception)
        {
            message = $"Could not spawn {entry.Name}; see LogOutput.log.";
            Logger?.LogError($"Sandbox enemy spawn failed for {entry.Name}: {exception}");
        }
    }

    private static bool SpawnRegularEnemy(GameManager game, EnemyEntry entry, int before)
    {
        game.SpawnThing(entry.Prefab, EnemySpawnDistance(game.spawnDistance), true);
        if (!SpawnDidNotIncreaseCount(before, game.enemiesOnScreen)) return true;
        var player = game.player;
        var position = player == null || GameManager.IsSpawnPointFarEnoughFromPlayer(game.spawnPoint, player.transform.position, 5f)
            ? game.spawnPoint
            : player.transform.position + Vector3.right * EnemySpawnDistance(game.spawnDistance);
        var spawned = Object.Instantiate(entry.Prefab, position, Quaternion.identity);
        if (spawned != null && !spawned.activeSelf) spawned.SetActive(true);
        Logger?.LogInfo($"Expanded regular enemy beyond its fixed pool: {entry.Name}.");
        return spawned != null;
    }

    private static bool SpawnMiniBoss(GameManager game, EnemyEntry entry)
    {
        var emptyPosition = new Il2CppSystem.Nullable<Vector3>();
        if (game.SpawnMiniBoss(entry.MiniBoss!, EnemySpawnDistance(game.spawnDistance), entry.MiniBossNumber,
                true, true, false, emptyPosition)) return true;
        var expanded = ObjectPool.instance?.getPooledMiniBoss(entry.MiniBoss!, entry.MiniBossNumber, true);
        if (expanded == null) return false;
        Logger?.LogInfo($"Expanded miniboss pool for {entry.Name}.");
        return game.SpawnMiniBoss(entry.MiniBoss!, EnemySpawnDistance(game.spawnDistance), entry.MiniBossNumber,
            true, true, false, new Il2CppSystem.Nullable<Vector3>());
    }

    internal static float EnemySpawnDistance(float gameDistance) => gameDistance > 0f ? gameDistance : 30f;
    internal static string QueuedLabel(int count) => $"Queued: {count}";
    internal static bool SpawnDidNotIncreaseCount(int before, int after) => after <= before;
    internal static int EnemyGridColumn(int index, int columns) => index % columns;
    internal static int EnemyGridRow(int index, int columns) => index / columns;
    internal static int EnemyCatalogNumber(string name)
    {
        var index = name.IndexOf(':') + 1;
        while (index < name.Length && char.IsWhiteSpace(name[index])) index++;
        var leading = NumberAt(name, index);
        if (leading != int.MaxValue) return leading;
        var marker = name.IndexOf("enemy", index, StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? int.MaxValue : NumberAt(name, marker + 5);
    }

    private static int NumberAt(string name, int index)
    {
        var value = 0;
        var found = false;
        while (index < name.Length && char.IsDigit(name[index]))
        {
            found = true;
            value = value * 10 + name[index++] - '0';
        }
        return found ? value : int.MaxValue;
    }

    private void DrawPageButtons(float x, float y, float width, int pageCount)
    {
        if (pageCount <= 1) return;
        if (GUI.Button(new Rect(x, y, 90f, 26f), "Previous")) page = Math.Max(0, page - 1);
        GUI.Label(new Rect(x + 100f, y, width - 200f, 26f), $"Page {page + 1}/{pageCount}");
        if (GUI.Button(new Rect(x + width - 90f, y, 90f, 26f), "Next")) page = Math.Min(pageCount - 1, page + 1);
    }

    private void RefreshCatalog()
    {
        DestroyEnemyPreviews();
        ClearCatalogTextures();
        upgrades.Clear();
        perks.Clear();
        mutations.Clear();
        enemies.Clear();

        var game = GameManager.instance;
        var gamePointer = game?.Pointer ?? IntPtr.Zero;
        if (gamePointer != catalogGame)
        {
            pendingEnemySpawns.Clear();
            nextEnemySpawnTime = 0f;
            selectedBySandbox.Clear();
            deselectedBySandbox.Clear();
            mutationSpeedChanges.Clear();
            mutationSpeedBuffChanges.Clear();
            mutationMovementMultiplierChanges.Clear();
            mutationCustomStatChanges.Clear();
            redlineBulletDamageChanges.Clear();
            redlineFireRateChanges.Clear();
            redlineExpGainChanges.Clear();
            sandboxRedlineSelected = false;
            redlineSupplementApplied = false;
            redlineBulletDamageSupplement = 0f;
            redlineFireRateSupplement = 0f;
        }

        var specialized = new HashSet<IntPtr>();
        var iconCache = new Dictionary<int, Texture2D>();
        var weaponNames = GetWeaponNames();
        foreach (var perk in Resources.FindObjectsOfTypeAll<weaponupgrade>())
        {
            specialized.Add(perk.Pointer);
            AddUnique(perks, CreateEntry(perk, perk, null, FindWeaponSafe(perk, weaponNames), iconCache));
        }
        foreach (var mutation in Resources.FindObjectsOfTypeAll<mutationUpgrade>())
        {
            specialized.Add(mutation.Pointer);
            if (game != null && mutation.isMainMenu) continue;
            AddUnique(mutations, CreateEntry(mutation, null, mutation, string.Empty, iconCache));
        }
        foreach (var upgrade in Resources.FindObjectsOfTypeAll<Upgrade>())
        {
            if (!specialized.Contains(upgrade.Pointer)) AddUnique(upgrades, CreateEntry(upgrade, null, null, string.Empty, iconCache));
        }

        OrderUpgradeTrees();
        OrderPerksFromCatalog(weaponNames);
        perks.Sort(Entry.ComparePerks);
        mutations.Sort(Entry.CompareNumbered);

        var perkGroups = BuildGroups(perks);
        Logger?.LogInfo($"Sandbox perk groups: {string.Join(", ", perkGroups.ConvertAll(group => $"{group.Name}={group.Entries.Count}"))}");

        catalogGame = gamePointer;
        if (game != null)
        {
            AddEnemies(game.enemyList, "Enemy");
            AddEnemies(game.adEnemyList, "Elite");
            AddMiniBosses(game.miniBossList);
            enemies.Sort(EnemyEntry.Compare);
        }
        message = $"Found {upgrades.Count} upgrades, {perks.Count} perks, {mutations.Count} mutations, and {enemies.Count} enemies.";
    }

    [HideFromIl2Cpp]
    private void ExportLoadedArt()
    {
        try
        {
            RefreshCatalog();
            var root = Path.Combine(Paths.GameRootPath, "DisfigureArtExport");
            var count = 0;
            foreach (var entry in perks)
                if (entry.Icon != null && SavePng(entry.Icon, Path.Combine(root, "perks", SafeFileName(entry.Group),
                        $"{entry.Order:00} - {SafeFileName(entry.Name)}.png"))) count++;
            foreach (var entry in mutations)
                if (entry.Icon != null && SavePng(entry.Icon, Path.Combine(root, "mutations",
                        $"{entry.Order:00} - {SafeFileName(entry.Name)}.png"))) count++;

            var enemyCache = new Dictionary<int, Texture2D>();
            foreach (var entry in enemies)
            {
                var icon = GetEnemyThumbnail(entry.Prefab, enemyCache, entry.MiniBoss, entry.MiniBossNumber);
                if (icon != null && SavePng(icon, Path.Combine(root, "enemies", $"{SafeFileName(entry.Name)}.png"))) count++;
            }

            message = $"Exported {count} loaded images to {root}.";
            Logger?.LogInfo(message);
        }
        catch (Exception exception)
        {
            message = "Artwork export failed; see LogOutput.log.";
            Logger?.LogError($"Artwork export failed: {exception}");
        }
    }

    private static bool SavePng(Texture2D texture, string path)
    {
        var encoded = texture.EncodeToPNG();
        if (encoded == null || encoded.Length == 0) return false;
        var bytes = new byte[encoded.Length];
        for (var i = 0; i < encoded.Length; i++) bytes[i] = encoded[i];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return true;
    }

    internal static string SafeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return value.Trim(' ', '.');
    }

    [HideFromIl2Cpp]
    private Entry CreateEntry(Upgrade upgrade, weaponupgrade? perk, mutationUpgrade? mutation, string weapon,
        Dictionary<int, Texture2D> iconCache)
    {
        var name = NameOf(upgrade);
        var description = new List<string>();
        try
        {
            var main = upgrade.getDescription();
            if (!string.IsNullOrWhiteSpace(main)) description.Add(main);
            var lines = upgrade.getDesclines();
            for (var i = 0; i < lines?.Length; i++)
                if (!string.IsNullOrWhiteSpace(lines[i])) description.Add(lines[i]);
        }
        catch { }

        if (mutation != null && NeedsLevelActivationNote(name))
            description.Add("Sandbox note: Gain a level after selecting Overarmed to activate it.");

        Texture2D? icon = null;
        Texture2D? border = null;
        try
        {
            icon = GetCopiedSprite(upgrade.getImage()?.sprite ?? FindAnySprite(upgrade.gameObject), iconCache);
            border = GetCopiedSprite(GetBorderSprite(upgrade), iconCache);
        }
        catch (Exception exception)
        {
            Logger?.LogDebug($"Could not load sandbox art for '{name}': {exception.Message}");
        }

        var badge = string.Empty;
        if (mutation != null)
        {
            try { badge = mutation.text?.text ?? string.Empty; }
            catch { }
            if (string.IsNullOrWhiteSpace(badge))
            {
                var number = LeadingNumber(name);
                if (number < int.MaxValue) badge = number.ToString();
                else if (mutation.mutationInteger > 0) badge = mutation.mutationInteger.ToString();
            }
        }

        return new Entry(name, upgrade.upgradeName ?? name, string.Join("\n\n", description), upgrade, perk, mutation,
            weapon, icon, border, badge, perk == null ? LeadingNumber(name) : FindNativePerkOrder(perk))
        {
            Group = perk == null ? string.Empty : weapon.Equals("Other", StringComparison.OrdinalIgnoreCase)
                ? "OTHER PERKS"
                : weapon.ToUpperInvariant()
        };
    }

    [HideFromIl2Cpp]
    private void OrderPerksFromCatalog(List<string> weaponNames)
    {
        try
        {
            var catalog = achievementManager.s_sharedWeaponPerkKitCatalog ?? achievementManager.instance?.weaponPerkKitCatalog;
            if (catalog == null) return;
            var weaponIds = achievementManager.PlayableWeaponIdsOrdered ?? achievementManager.BuildPlayableWeaponIdsOrdered();
            var candidates = new List<string>();
            for (var weaponIndex = 0; weaponIndex < weaponIds?.Length; weaponIndex++) AddWeaponCandidate(candidates, weaponIds[weaponIndex]);
            foreach (var weaponName in weaponNames) AddWeaponCandidate(candidates, weaponName);
            AddWeaponCandidate(candidates, "Lever-Action Rifle");
            AddWeaponCandidate(candidates, "Laser Catalyst");
            AddWeaponCandidate(candidates, "katanas");

            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                var weaponId = candidate;
                if (!catalog.TryGetByWeaponId(weaponId, out var kit) || kit == null)
                {
                    weaponId = NormalizeWeapon(candidate);
                    if (!catalog.TryGetByWeaponId(weaponId, out kit) || kit == null) continue;
                }
                var displayName = DisplayWeaponName(weaponId, weaponNames);
                for (var perkIndex = 0; perkIndex < kit.upgradeNamesRaw?.Length; perkIndex++)
                {
                    var rawName = kit.upgradeNamesRaw[perkIndex];
                    foreach (var entry in perks.FindAll(candidate => string.Equals(candidate.RawName, rawName, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(candidate.Name, rawName, StringComparison.OrdinalIgnoreCase)))
                    {
                        entry.Weapon = weaponId;
                        entry.Group = displayName;
                        entry.WeaponOrder = Math.Min(entry.WeaponOrder, candidateIndex);
                        entry.Order = perkIndex + 1;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Logger?.LogDebug($"Could not read canonical perk order: {exception.Message}");
        }

        foreach (var entry in perks) entry.Group = DisplayWeaponName(entry.Weapon, weaponNames);

        var unique = new List<Entry>();
        foreach (var entry in perks)
        {
            var index = unique.FindIndex(candidate => string.Equals(candidate.RawName, entry.RawName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Group, entry.Group, StringComparison.OrdinalIgnoreCase));
            if (index < 0) unique.Add(entry);
            else if (unique[index].Perk?.isMainMenu == true && entry.Perk?.isMainMenu == false) unique[index] = entry;
        }
        perks.Clear();
        perks.AddRange(unique);
    }

    private static void AddWeaponCandidate(List<string> candidates, string weapon)
    {
        if (!string.IsNullOrWhiteSpace(weapon)
            && !candidates.Exists(candidate => string.Equals(candidate, weapon, StringComparison.OrdinalIgnoreCase)))
            candidates.Add(weapon);
    }

    private static int FindNativePerkOrder(weaponupgrade perk)
    {
        try
        {
            var child = perk.transform;
            for (var parent = child.parent; parent != null; child = parent, parent = parent.parent)
                if (parent.GetComponent<disableGameObjectIfWrongWeapon>() != null) return child.GetSiblingIndex() + 1;
            return perk.transform.GetSiblingIndex() + 1;
        }
        catch { return int.MaxValue; }
    }

    private void OrderUpgradeTrees()
    {
        var trees = new List<List<Entry>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var panel in Resources.FindObjectsOfTypeAll<upgradepathspanel>())
        {
            var items = new List<Entry>();
            for (var i = 0; i < panel.upgradesList?.Length; i++)
            {
                var upgrade = panel.upgradesList[i]?.GetComponent<Upgrade>();
                var name = upgrade == null ? string.Empty : NameOf(upgrade);
                var entry = upgrades.Find(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (entry != null && !items.Contains(entry)) items.Add(entry);
            }
            var key = string.Join("|", items.ConvertAll(item => item.Name));
            if (items.Count > 0 && seen.Add(key)) trees.Add(items);
        }

        trees.Sort((left, right) => string.Compare(left[0].Name, right[0].Name, StringComparison.OrdinalIgnoreCase));
        var ordered = new List<Entry>();
        foreach (var tree in trees)
        {
            var group = $"UPGRADE TREE — {tree[0].Name}";
            foreach (var entry in tree)
            {
                if (ordered.Contains(entry)) continue;
                entry.Group = group;
                ordered.Add(entry);
            }
        }

        upgrades.Clear();
        upgrades.AddRange(ordered);
    }

    private static List<string> GetWeaponNames()
    {
        var names = new List<string>();
        foreach (var selector in Resources.FindObjectsOfTypeAll<weaponselect>())
            if (!string.IsNullOrWhiteSpace(selector.weaponname) && !names.Exists(name => string.Equals(name, selector.weaponname, StringComparison.OrdinalIgnoreCase)))
                names.Add(selector.weaponname);
        foreach (var marker in Resources.FindObjectsOfTypeAll<disableGameObjectIfWrongWeapon>())
            if (!string.IsNullOrWhiteSpace(marker.weaponName) && !names.Exists(name => string.Equals(name, marker.weaponName, StringComparison.OrdinalIgnoreCase)))
                names.Add(marker.weaponName);
        return names;
    }

    private static string FindWeaponSafe(weaponupgrade perk, List<string> weaponNames)
    {
        try
        {
            for (var parent = perk.transform; parent != null; parent = parent.parent)
            {
                var normalizedParent = NormalizeWeapon(parent.name);
                var marker = parent.GetComponent<disableGameObjectIfWrongWeapon>();
                if (!string.IsNullOrWhiteSpace(marker?.weaponName)) return marker.weaponName;
                foreach (var weapon in weaponNames)
                    if (normalizedParent.Contains(NormalizeWeapon(weapon))) return weapon;
            }
        }
        catch (Exception exception)
        {
            Logger?.LogDebug($"Could not group perk '{NameOf(perk)}': {exception.Message}");
        }
        return "Other";
    }

    private static string NormalizeWeapon(string value)
    {
        var characters = new char[value.Length];
        var count = 0;
        foreach (var character in value)
            if (char.IsLetterOrDigit(character)) characters[count++] = char.ToLowerInvariant(character);
        return new string(characters, 0, count);
    }

    [HideFromIl2Cpp]
    internal static string DisplayWeaponName(string weaponId, List<string> weaponNames)
    {
        var id = NormalizeWeapon(weaponId);
        if (id == "katanas") return "TWIN KATANAS";
        if (id == "repeater") return "LEVER-ACTION RIFLE";
        if (id == "lasergun") return "LASER CATALYST";
        foreach (var name in weaponNames)
            if (NormalizeWeapon(name) == id) return name.ToUpperInvariant();
        foreach (var name in weaponNames)
        {
            var candidate = NormalizeWeapon(name);
            if (candidate.Contains(id) || id.Contains(candidate)) return name.ToUpperInvariant();
        }
        return weaponId.ToUpperInvariant();
    }

    [HideFromIl2Cpp]
    private Texture2D? GetCopiedSprite(Sprite? sprite, Dictionary<int, Texture2D> cache)
    {
        if (sprite == null) return null;
        var key = sprite.GetInstanceID();
        if (cache.TryGetValue(key, out var existing)) return existing;
        var copy = CopyIcon(sprite);
        if (copy == null) return null;
        cache[key] = copy;
        catalogTextures.Add(copy);
        return copy;
    }

    private static Sprite? GetBorderSprite(Upgrade upgrade)
    {
        if (upgrade is mutationUpgrade mutation && mutation.border?.sprite != null) return mutation.border.sprite;
        if (upgrade is weaponupgrade)
        {
            var perkBorder = FindNamedSprite(upgrade.gameObject, "background");
            if (perkBorder != null) return perkBorder;
        }
        var border = upgrade.selectedBorder;
        return border?.GetComponent<Image>()?.sprite
            ?? border?.GetComponent<SpriteRenderer>()?.sprite
            ?? border?.GetComponentInChildren<Image>(true)?.sprite
            ?? FindNamedSprite(upgrade.gameObject, "border");
    }

    private static Sprite? FindNamedSprite(GameObject? root, string namePart)
    {
        if (root == null) return null;
        foreach (var image in root.GetComponentsInChildren<Image>(true))
            if (image.sprite != null && image.gameObject.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return image.sprite;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (renderer.sprite != null && renderer.gameObject.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer.sprite;
        return null;
    }

    private static Sprite? FindAnySprite(GameObject? root)
    {
        if (root == null) return null;
        foreach (var image in root.GetComponentsInChildren<Image>(true)) if (image.sprite != null) return image.sprite;
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true)) if (renderer.sprite != null) return renderer.sprite;
        return null;
    }

    private static Texture2D? CopyIcon(Sprite sprite)
    {
        RenderTexture? temporary = null;
        RenderTexture? previous = null;
        try
        {
            var source = sprite.texture;
            if (source == null) return null;
            var rect = sprite.textureRect;
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            if (source.isReadable)
            {
                copy.SetPixels(source.GetPixels(Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), width, height));
            }
            else
            {
                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporary, new Vector2(rect.width / source.width, rect.height / source.height),
                    new Vector2(rect.x / source.width, rect.y / source.height));
                previous = RenderTexture.active;
                RenderTexture.active = temporary;
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            }
            copy.Apply(false, true);
            copy.filterMode = source.filterMode;
            copy.hideFlags = HideFlags.HideAndDontSave;
            return copy;
        }
        catch (Exception exception)
        {
            Logger?.LogDebug($"Could not copy sandbox icon: {exception.Message}");
            return null;
        }
        finally
        {
            if (temporary != null)
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    [HideFromIl2Cpp]
    private void ShowEnemyPreview(EnemyEntry entry, Rect area)
    {
        if (failedEnemyPreviews.Contains(entry.Name)) return;
        if (!enemyPreviews.TryGetValue(entry.Name, out var preview))
        {
            preview = CreateEnemyPreview(entry);
            if (preview == null)
            {
                failedEnemyPreviews.Add(entry.Name);
                return;
            }
            enemyPreviews[entry.Name] = preview;
        }

        if (!preview.Root.activeSelf) preview.Root.SetActive(true);
        if (!preview.Background.activeSelf) preview.Background.SetActive(true);
        if (preview.Positioned && preview.Area == area) return;
        var camera = Camera.main;
        if (camera == null) return;
        preview.Root.transform.localScale = preview.BaseScale;
        if (!TryGetPreviewBounds(preview.Root, out var bounds)) return;
        var stats = GameRefs.Instance?.playerStats;
        var depth = stats == null ? 10f : Vector3.Dot(stats.transform.position - camera.transform.position,
            camera.transform.forward);
        if (depth <= camera.nearClipPlane) depth = 10f;
        var target = camera.ScreenToWorldPoint(new Vector3(area.center.x, Screen.height - area.center.y, depth));
        preview.Root.transform.position += target - bounds.center;
        if (!TryGetPreviewBounds(preview.Root, out bounds)) return;
        var screenSize = PreviewScreenSize(camera, bounds);
        var scale = EnemyPreviewScale(area.width * 0.92f, area.height * 0.92f, screenSize.x, screenSize.y);
        preview.Root.transform.localScale = preview.BaseScale * scale;
        if (TryGetPreviewBounds(preview.Root, out bounds)) preview.Root.transform.position += target - bounds.center;
        PositionEnemyPreviewBackground(preview.Background, area, camera, depth + 1f);
        preview.Area = area;
        preview.Positioned = true;
    }

    [HideFromIl2Cpp]
    private EnemyPreview? CreateEnemyPreview(EnemyEntry entry)
    {
        try
        {
            var model = entry.Prefab;
            try
            {
                var pool = ObjectPool.instance;
                var pooledModel = entry.MiniBoss == null
                    ? pool?.getPooledEnemy(entry.Prefab)
                    : pool?.getPooledMiniBoss(entry.MiniBoss, entry.MiniBossNumber, false)?.gameObject;
                if (pooledModel != null) model = pooledModel;
            }
            catch (Exception exception)
            {
                Logger?.LogWarning($"Could not resolve pooled preview model for '{entry.Name}': {exception.Message}");
            }
            var clone = Object.Instantiate(model);
            clone.name = $"Sandbox Preview {entry.Name}";
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.SetActive(false);
            BuildSnakePreview(clone);
            PrepareEnemyPreviewVisuals(clone);
            PrepareSnakeSegmentVisuals(clone);
            var showMap2Enemy6Legs = entry.Name.EndsWith("map2enemy6", StringComparison.OrdinalIgnoreCase);
            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
            {
                var animator = behaviour.gameObject.GetComponent<Animator>();
                if (animator != null && animator.Pointer == behaviour.Pointer)
                {
                    animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                    animator.enabled = true;
                }
                else
                {
                    var meshAnimator = behaviour.gameObject.GetComponent<MeshAnimatorBase>();
                    if (meshAnimator != null && meshAnimator.Pointer == behaviour.Pointer)
                    {
                        meshAnimator.updateWhenOffscreen = true;
                        meshAnimator.playAutomatically = true;
                        meshAnimator.enabled = true;
                    }
                    else if (showMap2Enemy6Legs && IsMap2Enemy6PoseBehaviour(behaviour))
                    {
                        behaviour.enabled = true;
                        Logger?.LogDebug($"Keeping Enemy 6 pose behaviour active: {behaviour.GetType().FullName}");
                    }
                    else behaviour.enabled = false;
                }
            }
            clone.SetActive(true);
            if (showMap2Enemy6Legs)
            {
                enemyPreviewWarmupFrames = Math.Max(enemyPreviewWarmupFrames, 3);
                freezeEnemyPreviewsAfterWarmup = false;
            }
            var meshAnimators = new List<MeshAnimatorBase>();
            foreach (var meshAnimator in clone.GetComponentsInChildren<MeshAnimatorBase>(true))
            {
                try
                {
                    meshAnimator.updateWhenOffscreen = true;
                    meshAnimator.playAutomatically = true;
                    meshAnimator.enabled = true;
                    meshAnimator.Play();
                    meshAnimators.Add(meshAnimator);
                }
                catch { }
            }
            foreach (var transform in clone.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 30;
            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = $"Sandbox Preview Background {entry.Name}";
            background.hideFlags = HideFlags.HideAndDontSave;
            background.layer = 30;
            var backgroundRenderer = background.GetComponent<MeshRenderer>();
            if (backgroundRenderer != null) backgroundRenderer.sharedMaterial = GetEnemyPreviewBackgroundMaterial();
            Logger?.LogInfo($"Created animated enemy preview '{entry.Name}'.");
            return new EnemyPreview(clone, clone.transform.localScale, background, meshAnimators,
                showMap2Enemy6Legs);
        }
        catch (Exception exception)
        {
            Logger?.LogWarning($"Could not create animated enemy preview '{entry.Name}': {exception.Message}");
            return null;
        }
    }

    private static void PrepareEnemyPreviewVisuals(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (IsExcludedPreviewTreeName(transform.gameObject.name))
                transform.gameObject.SetActive(false);
        }
        foreach (var renderer in FindEnemyRenderers(root))
        {
            if (IsInsideExcludedPreviewTree(renderer.transform, root.transform)) continue;
            if (renderer.gameObject.name.Contains("OUTLINE", StringComparison.OrdinalIgnoreCase))
            {
                renderer.enabled = false;
                renderer.gameObject.SetActive(false);
                continue;
            }
            renderer.enabled = true;
            try { Enemy.SetRendererColor(renderer, Color.black); }
            catch { }
            var transform = renderer.transform;
            while (transform != null && transform.Pointer != root.transform.Pointer)
            {
                transform.gameObject.SetActive(true);
                transform = transform.parent;
            }
        }
    }

    private static bool IsMap2Enemy6PoseBehaviour(Behaviour behaviour)
    {
        var type = behaviour.GetType();
        var name = type.Name;
        var fullName = type.FullName ?? name;
        return name.StartsWith("IKSolver", StringComparison.OrdinalIgnoreCase)
            || fullName.Contains("Animations.Rigging", StringComparison.OrdinalIgnoreCase);
    }

    private void FreezeWarmedEnemyPreviews()
    {
        foreach (var preview in enemyPreviews.Values)
        {
            if (!preview.NeedsPoseWarmup || preview.PoseFrozen) continue;
            foreach (var animator in preview.Root.GetComponentsInChildren<Animator>(true))
            {
                try
                {
                    animator.Update(0f);
                    animator.speed = 0f;
                    animator.enabled = false;
                }
                catch { }
            }
            foreach (var behaviour in preview.Root.GetComponentsInChildren<Behaviour>(true))
            {
                if (!IsMap2Enemy6PoseBehaviour(behaviour)) continue;
                try { behaviour.enabled = false; }
                catch { }
            }
            preview.PoseFrozen = true;
            preview.Positioned = false;
            Logger?.LogInfo("Froze Enemy 6 preview after its normal leg pose initialized.");
        }
    }

    private static bool IsInsideExcludedPreviewTree(Transform transform, Transform root)
    {
        while (transform != null && transform.Pointer != root.Pointer)
        {
            if (IsExcludedPreviewTreeName(transform.gameObject.name)) return true;
            transform = transform.parent;
        }
        return false;
    }

    private static bool IsExcludedPreviewTreeName(string name) =>
        name.StartsWith("DeathEffects", StringComparison.OrdinalIgnoreCase)
        || name.Contains("AttackManager", StringComparison.OrdinalIgnoreCase);

    private static void PrepareSnakeSegmentVisuals(GameObject root)
    {
        var snake = root.GetComponentInChildren<SnakeEnemy>(true);
        for (var i = 0; i < (snake?.bodyParts?.Count ?? 0); i++)
        {
            var part = snake!.bodyParts![i];
            if (part == null) continue;
            foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject.name.Equals("Front", StringComparison.OrdinalIgnoreCase))
                    renderer.gameObject.SetActive(i == 0);
        }
    }

    private static void BuildSnakePreview(GameObject clone)
    {
        var snake = clone.GetComponentInChildren<SnakeEnemy>(true);
        if (snake == null || snake.bodyprefabs == null) return;
        snake.bodyParts = new Il2CppSystem.Collections.Generic.List<Transform>();
        snake.isObjPoolFirstSpawn = false;
        snake.addBackIntoPool = false;
        var requested = Math.Max(1, snake.beginSize);
        for (var i = 0; i < requested; i++)
        {
            try { snake.AddBodyPart(i); }
            catch (Exception exception)
            {
                Logger?.LogInfo($"Centipede segment {i} could not be added: {exception.Message}");
                break;
            }
        }
        var count = snake.bodyParts?.Count ?? 0;
        if (count == 0) return;
        for (var i = 0; i < count; i++)
        {
            var part = snake.bodyParts![i];
            if (part == null || part.Pointer == clone.transform.Pointer) continue;
            part.gameObject.SetActive(false);
            part.gameObject.hideFlags = HideFlags.HideAndDontSave;
            if (!part.IsChildOf(clone.transform)) part.SetParent(clone.transform, true);
            part.gameObject.SetActive(true);
        }
        var camera = Camera.main;
        var right = Vector3.ProjectOnPlane(camera?.transform.right ?? Vector3.right, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.5f) right = Vector3.right;
        var up = Vector3.ProjectOnPlane(camera?.transform.up ?? Vector3.forward, Vector3.up).normalized;
        if (up.sqrMagnitude < 0.5f) up = Vector3.forward;
        var direction = (right + up * 0.55f).normalized;
        var spacing = Math.Max(0.25f, snake.minDistance * 0.0196f);
        var firstPosition = clone.transform.position - direction * (spacing * (count - 1) * 0.5f);
        var segmentRotation = Quaternion.LookRotation(-direction, Vector3.up);
        for (var i = 0; i < count; i++)
        {
            var part = snake.bodyParts![i];
            if (part == null || part.Pointer == clone.transform.Pointer) continue;
            part.gameObject.SetActive(true);
            part.localScale *= 0.82f;
            part.position = firstPosition + direction * (spacing * i);
            part.rotation = segmentRotation;
        }
        Logger?.LogInfo($"Built the sandbox centipede from {count} native body segments.");
    }

    private Material? GetEnemyPreviewBackgroundMaterial()
    {
        if (enemyPreviewBackgroundMaterial != null) return enemyPreviewBackgroundMaterial;
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null) return null;
        enemyPreviewBackgroundMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        var color = new Color(0.82f, 0.82f, 0.82f, 1f);
        enemyPreviewBackgroundMaterial.color = color;
        if (enemyPreviewBackgroundMaterial.HasProperty("_BaseColor"))
            enemyPreviewBackgroundMaterial.SetColor("_BaseColor", color);
        if (enemyPreviewBackgroundMaterial.HasProperty("_Cull")) enemyPreviewBackgroundMaterial.SetFloat("_Cull", 0f);
        return enemyPreviewBackgroundMaterial;
    }

    private static void PositionEnemyPreviewBackground(GameObject background, Rect area, Camera camera, float depth)
    {
        Vector3 Point(float screenX, float screenY) =>
            camera.ScreenToWorldPoint(new Vector3(screenX, Screen.height - screenY, depth));
        var center = Point(area.center.x, area.center.y);
        background.transform.position = center;
        background.transform.rotation = camera.transform.rotation;
        background.transform.localScale = new Vector3(
            Vector3.Distance(Point(area.x, area.center.y), Point(area.xMax, area.center.y)),
            Vector3.Distance(Point(area.center.x, area.y), Point(area.center.x, area.yMax)), 1f);
    }

    private void EnsureEnemyPreviewCamera()
    {
        var main = Camera.main;
        if (main == null) return;
        if (enemyPreviewCamera == null || previewBaseCamera == null || previewBaseCamera.Pointer != main.Pointer)
        {
            DestroyEnemyPreviewCamera();
            var mainData = main.gameObject.GetComponent<UniversalAdditionalCameraData>();
            if (mainData == null) return;
            var cameraObject = new GameObject("Sandbox Enemy Preview Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            enemyPreviewCamera = cameraObject.AddComponent<Camera>();
            enemyPreviewCamera.CopyFrom(main);
            enemyPreviewCamera.cullingMask = 1 << 30;
            var previewData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            previewData.renderType = CameraRenderType.Overlay;
            previewData.renderPostProcessing = false;
            previewBaseCamera = main;
            previewBaseCameraData = mainData;
            previewBaseCullingMask = main.cullingMask;
            main.cullingMask &= ~(1 << 30);
            mainData.cameraStack.Add(enemyPreviewCamera);
            Logger?.LogInfo("Created darkness-free animated enemy preview overlay camera.");
        }
        enemyPreviewCamera.transform.position = main.transform.position;
        enemyPreviewCamera.transform.rotation = main.transform.rotation;
    }

    private void DestroyEnemyPreviewCamera()
    {
        if (previewBaseCameraData != null && enemyPreviewCamera != null)
        {
            try { previewBaseCameraData.cameraStack.Remove(enemyPreviewCamera); }
            catch { }
        }
        if (previewBaseCamera != null)
        {
            try { previewBaseCamera.cullingMask = previewBaseCullingMask; }
            catch { }
        }
        if (enemyPreviewCamera != null)
            Object.Destroy(enemyPreviewCamera.gameObject);
        enemyPreviewCamera = null;
        previewBaseCamera = null;
        previewBaseCameraData = null;
    }

    [HideFromIl2Cpp]
    private static bool TryGetPreviewBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var renderer in FindEnemyRenderers(root))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static Vector2 PreviewScreenSize(Camera camera, Bounds bounds)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            var screen = camera.WorldToScreenPoint(corner);
            minX = Math.Min(minX, screen.x);
            minY = Math.Min(minY, screen.y);
            maxX = Math.Max(maxX, screen.x);
            maxY = Math.Max(maxY, screen.y);
        }
        return new Vector2(maxX - minX, maxY - minY);
    }

    internal static float EnemyPreviewScale(float targetWidth, float targetHeight, float modelWidth, float modelHeight)
    {
        if (modelWidth <= 0f || modelHeight <= 0f) return 1f;
        return Math.Min(targetWidth / modelWidth, targetHeight / modelHeight);
    }

    private void HideEnemyPreviews()
    {
        foreach (var preview in enemyPreviews.Values)
        {
            if (preview.Root.activeSelf) preview.Root.SetActive(false);
            if (preview.Background.activeSelf) preview.Background.SetActive(false);
        }
    }

    private void DestroyEnemyPreviews()
    {
        foreach (var preview in enemyPreviews.Values)
        {
            Object.Destroy(preview.Root);
            Object.Destroy(preview.Background);
        }
        enemyPreviews.Clear();
        failedEnemyPreviews.Clear();
    }

    [HideFromIl2Cpp]
    private Texture2D? GetEnemyThumbnail(GameObject prefab, Dictionary<int, Texture2D> cache,
        Enemy? miniBoss = null, int miniBossNumber = -1)
    {
        var key = prefab.GetInstanceID();
        if (cache.TryGetValue(key, out var existing)) return existing;
        var model = prefab;
        try
        {
            var pool = ObjectPool.instance;
            var pooledModel = miniBoss == null
                ? pool?.getPooledEnemy(prefab)
                : pool?.getPooledMiniBoss(miniBoss, miniBossNumber, false)?.gameObject;
            if (pooledModel != null) model = pooledModel;
        }
        catch (Exception exception)
        {
            Logger?.LogWarning($"Could not resolve pooled model for '{prefab.name}': {exception.Message}");
        }
        var sprite = FindAnySprite(model);
        var thumbnail = sprite == null ? RenderEnemyThumbnail(model) : CopyIcon(sprite);
        if (thumbnail == null) return null;
        thumbnail.hideFlags = HideFlags.HideAndDontSave;
        cache[key] = thumbnail;
        catalogTextures.Add(thumbnail);
        return thumbnail;
    }

    [HideFromIl2Cpp]
    private static Texture2D? RenderEnemyThumbnail(GameObject prefab)
    {
        GameObject? previewRoot = null;
        var bakedMeshes = new List<Mesh>();
        try
        {
            previewRoot = new GameObject("Sandbox Enemy Thumbnail");
            previewRoot.hideFlags = HideFlags.HideAndDontSave;
            var previewRenderers = new List<MeshRenderer>();
            var previewMeshes = new List<(Mesh Mesh, Transform Transform)>();
            var sourceRenderers = FindEnemyRenderers(prefab);
            foreach (var source in sourceRenderers)
            {
                Mesh? mesh = null;
                var skinned = source.gameObject.GetComponent<SkinnedMeshRenderer>();
                if (skinned != null && skinned.Pointer == source.Pointer)
                {
                    var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    try { skinned.BakeMesh(baked); }
                    catch { }
                    if (baked.vertexCount > 0)
                    {
                        mesh = baked;
                        bakedMeshes.Add(baked);
                    }
                    else
                    {
                        Object.Destroy(baked);
                        mesh = skinned.sharedMesh;
                    }
                }
                else
                {
                    var meshRenderer = source.gameObject.GetComponent<MeshRenderer>();
                    if (meshRenderer != null && meshRenderer.Pointer == source.Pointer)
                        mesh = source.gameObject.GetComponent<MeshFilter>()?.sharedMesh;
                }
                if (mesh == null || mesh.vertexCount == 0) continue;

                var previewObject = new GameObject(source.gameObject.name);
                previewObject.hideFlags = HideFlags.HideAndDontSave;
                previewObject.layer = 31;
                previewObject.transform.SetParent(previewRoot.transform, false);
                previewObject.transform.position = source.transform.position;
                previewObject.transform.rotation = source.transform.rotation;
                previewObject.transform.localScale = source.transform.lossyScale;
                previewObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var previewRenderer = previewObject.AddComponent<MeshRenderer>();
                previewRenderers.Add(previewRenderer);
                previewMeshes.Add((mesh, previewObject.transform));
            }
            if (previewRenderers.Count == 0)
            {
                Logger?.LogWarning($"No renderable mesh found for enemy thumbnail '{prefab.name}' "
                    + $"after checking {sourceRenderers.Count} renderer references.");
                return null;
            }

            var bounds = previewRenderers[0].bounds;
            for (var i = 1; i < previewRenderers.Count; i++) bounds.Encapsulate(previewRenderers[i].bounds);
            var forward = EnemyThumbnailForward(bounds.extents);
            var thumbnail = RasterizeEnemyThumbnail(previewMeshes, bounds, forward, out var visiblePixels);
            Logger?.LogInfo($"Rendered enemy thumbnail '{prefab.name}' from {previewRenderers.Count} meshes "
                + $"with {visiblePixels} visible pixels.");
            return thumbnail;
        }
        catch (Exception exception)
        {
            Logger?.LogWarning($"Could not render enemy thumbnail '{prefab.name}': {exception.Message}");
            return null;
        }
        finally
        {
            foreach (var mesh in bakedMeshes) Object.Destroy(mesh);
            if (previewRoot != null) Object.Destroy(previewRoot);
        }
    }

    [HideFromIl2Cpp]
    private static Texture2D RasterizeEnemyThumbnail(List<(Mesh Mesh, Transform Transform)> meshes, Bounds bounds,
        Vector3 forward, out int visiblePixels)
    {
        const int width = 256;
        const int height = 160;
        const float aspect = (float)width / height;
        var background = new Color32(210, 210, 210, 255);
        var foreground = new Color32(12, 12, 12, 255);
        var pixels = new Color32[width * height];
        Array.Fill(pixels, background);
        var up = forward == Vector3.down ? Vector3.forward : Vector3.up;
        var right = Vector3.Cross(up, forward);
        var orthoSize = EnemyThumbnailOrthoSize(bounds.extents);
        var halfWidth = orthoSize * aspect;

        foreach (var (mesh, transform) in meshes)
        {
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = EnemyThumbnailPoint(transform.TransformPoint(vertices[triangles[i]]), bounds.center,
                        right, up, halfWidth, orthoSize, width, height);
                    var b = EnemyThumbnailPoint(transform.TransformPoint(vertices[triangles[i + 1]]), bounds.center,
                        right, up, halfWidth, orthoSize, width, height);
                    var c = EnemyThumbnailPoint(transform.TransformPoint(vertices[triangles[i + 2]]), bounds.center,
                        right, up, halfWidth, orthoSize, width, height);
                    FillEnemyThumbnailTriangle(pixels, width, height, a, b, c, foreground);
                }
            }
            catch (Exception exception)
            {
                Logger?.LogDebug($"Could not rasterize enemy mesh '{mesh.name}': {exception.Message}");
            }
        }

        visiblePixels = 0;
        foreach (var pixel in pixels) if (pixel.r < 190) visiblePixels++;
        var thumbnail = new Texture2D(width, height, TextureFormat.RGBA32, false);
        thumbnail.SetPixels32(new Il2CppStructArray<Color32>(pixels));
        thumbnail.Apply(false, true);
        return thumbnail;
    }

    internal static Vector2 EnemyThumbnailPoint(Vector3 point, Vector3 center, Vector3 right, Vector3 up,
        float halfWidth, float halfHeight, int width, int height)
    {
        var offset = point - center;
        return new Vector2((Vector3.Dot(offset, right) / (2f * halfWidth) + 0.5f) * (width - 1),
            (Vector3.Dot(offset, up) / (2f * halfHeight) + 0.5f) * (height - 1));
    }

    [HideFromIl2Cpp]
    private static void FillEnemyThumbnailTriangle(Color32[] pixels, int width, int height, Vector2 a, Vector2 b,
        Vector2 c, Color32 color)
    {
        static float Edge(Vector2 p1, Vector2 p2, Vector2 p) =>
            (p.x - p1.x) * (p2.y - p1.y) - (p.y - p1.y) * (p2.x - p1.x);

        var area = Edge(a, b, c);
        if (Math.Abs(area) < 0.01f) return;
        var minX = Mathf.Clamp(Mathf.FloorToInt(Math.Min(a.x, Math.Min(b.x, c.x))), 0, width - 1);
        var maxX = Mathf.Clamp(Mathf.CeilToInt(Math.Max(a.x, Math.Max(b.x, c.x))), 0, width - 1);
        var minY = Mathf.Clamp(Mathf.FloorToInt(Math.Min(a.y, Math.Min(b.y, c.y))), 0, height - 1);
        var maxY = Mathf.Clamp(Mathf.CeilToInt(Math.Max(a.y, Math.Max(b.y, c.y))), 0, height - 1);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var point = new Vector2(x + 0.5f, y + 0.5f);
            var e1 = Edge(a, b, point);
            var e2 = Edge(b, c, point);
            var e3 = Edge(c, a, point);
            if ((e1 >= 0f && e2 >= 0f && e3 >= 0f) || (e1 <= 0f && e2 <= 0f && e3 <= 0f))
                pixels[y * width + x] = color;
        }
    }

    [HideFromIl2Cpp]
    internal static bool EnemyThumbnailRasterSelfCheck()
    {
        var pixels = new Color32[16];
        FillEnemyThumbnailTriangle(pixels, 4, 4, new Vector2(0f, 0f), new Vector2(3f, 0f),
            new Vector2(0f, 3f), new Color32(12, 12, 12, 255));
        foreach (var pixel in pixels) if (pixel.r == 12) return true;
        return false;
    }

    internal static float EnemyThumbnailOrthoSize(Vector3 extents) => Math.Max(0.5f, extents.magnitude * 1.15f);

    internal static Vector3 EnemyThumbnailForward(Vector3 extents)
    {
        if (extents.y <= extents.x && extents.y <= extents.z) return Vector3.down;
        return extents.z <= extents.x ? Vector3.forward : Vector3.right;
    }

    [HideFromIl2Cpp]
    private static List<Renderer> FindEnemyRenderers(GameObject model)
    {
        var renderers = new List<Renderer>();
        void Add(Renderer? renderer)
        {
            if (renderer != null && !renderers.Exists(existing => existing.Pointer == renderer.Pointer)) renderers.Add(renderer);
        }
        void AddChildren(GameObject? root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) Add(renderer);
        }

        AddChildren(model);
        foreach (var enemy in model.GetComponentsInChildren<Enemy>(true))
        {
            Add(enemy.sMR);
            Add(enemy.sMR2);
            Add(enemy.mR);
        }
        if (renderers.Count == 0)
        {
            var parent = model.transform.parent;
            if (parent != null) AddChildren(parent.gameObject);
        }
        Logger?.LogInfo($"Enemy thumbnail renderer search '{model.name}': found {renderers.Count}.");
        return renderers;
    }

    private void ClearCatalogTextures()
    {
        foreach (var texture in catalogTextures) Object.Destroy(texture);
        catalogTextures.Clear();
    }

    private void AddEnemies(Il2CppSystem.Collections.Generic.List<GameObject>? source, string kind)
    {
        for (var i = 0; i < source?.Count; i++)
        {
            var prefab = source[i];
            if (prefab == null || enemies.Exists(item => item.Prefab.Pointer == prefab.Pointer)) continue;
            var name = string.IsNullOrWhiteSpace(prefab.name) ? $"{kind} {i + 1}" : prefab.name.Replace("(Clone)", string.Empty).Trim();
            enemies.Add(new EnemyEntry($"{kind}: {name}", prefab));
        }
    }

    private void AddMiniBosses(Il2CppSystem.Collections.Generic.List<Enemy>? source)
    {
        for (var i = 0; i < source?.Count; i++)
        {
            var miniBoss = source[i];
            var prefab = miniBoss?.gameObject;
            if (prefab == null) continue;
            var name = string.IsNullOrWhiteSpace(prefab.name) ? $"Miniboss {i + 1}" : prefab.name.Replace("(Clone)", string.Empty).Trim();
            enemies.Add(new EnemyEntry($"Miniboss: {name}", prefab, miniBoss, i));
        }
    }

    private static void AddUnique(List<Entry> target, Entry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Name) && !target.Exists(item => string.Equals(item.Key, entry.Key, StringComparison.OrdinalIgnoreCase)))
            target.Add(entry);
    }

    private static string NameOf(Upgrade upgrade)
    {
        try { return upgrade.getName() ?? string.Empty; }
        catch { return upgrade.upgradeName ?? upgrade.name ?? string.Empty; }
    }

    private static bool ContainsEntry(Il2CppSystem.Collections.Generic.List<string>? list, Entry entry)
    {
        for (var i = 0; i < list?.Count; i++)
            if (EntryNameMatches(list[i], entry.Name, entry.RawName)) return true;
        return false;
    }

    internal static bool EntryNameEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static bool EntryNameMatches(string actual, string displayName, string rawName) =>
        EntryNameEquals(actual, displayName) || !string.IsNullOrWhiteSpace(rawName) && EntryNameEquals(actual, rawName);

    internal static string MutationKey(string name) => $"mutation:{name}";

    internal static void RecordMutationSelected(mutationUpgrade mutation)
    {
        var overlay = Instance;
        if (overlay == null) return;
        var name = NameOf(mutation);
        if (string.IsNullOrWhiteSpace(name)) return;
        var rawName = mutation.upgradeName ?? name;
        var key = MutationKey(name);
        overlay.selectedBySandbox.Add(key);
        overlay.deselectedBySandbox.Remove(key);
        SyncEntryName(GameRefs.Instance?.playerStats?.chosenUpgrades, name, rawName);
        Logger?.LogInfo($"Recorded active mutation {name}.");
    }

    internal static bool NeedsLevelActivationNote(string name) =>
        name.Contains("OVERARMED", StringComparison.OrdinalIgnoreCase);

    private static void SyncEntryName(Il2CppSystem.Collections.Generic.List<string>? list, Entry entry)
        => SyncEntryName(list, entry.Name, entry.RawName);

    private static void SyncEntryName(Il2CppSystem.Collections.Generic.List<string>? list, string displayName, string rawName)
    {
        RemoveEntryNames(list, displayName, rawName);
        var name = string.IsNullOrWhiteSpace(rawName) ? displayName : rawName;
        if (!string.IsNullOrWhiteSpace(name)) list?.Add(name);
    }

    private static void RemoveEntryNames(Il2CppSystem.Collections.Generic.List<string>? list, Entry entry)
        => RemoveEntryNames(list, entry.Name, entry.RawName);

    private static void RemoveEntryNames(Il2CppSystem.Collections.Generic.List<string>? list, string displayName, string rawName)
    {
        for (var i = (list?.Count ?? 0) - 1; i >= 0; i--)
            if (EntryNameMatches(list![i], displayName, rawName)) list.RemoveAt(i);
    }

    [HideFromIl2Cpp]
    private bool IsSelected(PlayerStats stats, Entry entry)
    {
        if (entry.IsPerk) return ContainsEntry(stats.unlockedWeaponPerksList, entry);
        if (ContainsEntry(stats.chosenUpgrades, entry))
        {
            deselectedBySandbox.Remove(entry.Key);
            return true;
        }
        if (selectedBySandbox.Contains(entry.Key)) return true;
        if (deselectedBySandbox.Contains(entry.Key)) return false;
        return false;
    }

    internal static string FormatTime(float seconds)
    {
        var whole = Math.Max(0, (int)seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }

    internal static int LeadingNumber(string name)
    {
        var value = 0;
        var found = false;
        foreach (var character in name)
        {
            if (!char.IsDigit(character)) break;
            found = true;
            value = value * 10 + character - '0';
        }
        return found ? value : int.MaxValue;
    }

    private void MaintainGameTestMode()
    {
        var cheats = cheatmanager.Instance;
        if (GameManager.instance == null)
        {
            RestoreGameTestMode();
            return;
        }
        if (cheats == null) return;
        if (sandboxCheats == null || sandboxCheats.Pointer != cheats.Pointer)
        {
            RestoreGameTestMode();
            sandboxCheats = cheats;
            previousTestMode = cheats.isTestMode;
            previousDisableAllUnlocks = cheats.disableAllUnlocksForTesting;
        }
        cheats.isTestMode = true;
        cheats.disableAllUnlocksForTesting = false;
    }

    private void RestoreGameTestMode()
    {
        if (sandboxCheats != null)
        {
            try
            {
                sandboxCheats.isTestMode = previousTestMode;
                sandboxCheats.disableAllUnlocksForTesting = previousDisableAllUnlocks;
            }
            catch { }
        }
        sandboxCheats = null;
    }

    private sealed class Entry
    {
        internal Entry(string name, string rawName, string description, Upgrade upgrade, weaponupgrade? perk,
            mutationUpgrade? mutation, string weapon, Texture2D? icon, Texture2D? border, string badge, int order)
        {
            Name = name;
            RawName = rawName;
            Description = description;
            Upgrade = upgrade;
            Perk = perk;
            Mutation = mutation;
            Weapon = weapon;
            Icon = icon;
            Border = border;
            Badge = badge;
            Order = order;
        }

        internal string Name { get; }
        internal string RawName { get; }
        internal string Description { get; }
        internal Upgrade Upgrade { get; }
        internal weaponupgrade? Perk { get; }
        internal mutationUpgrade? Mutation { get; }
        internal string Weapon { get; set; }
        internal Texture2D? Icon { get; }
        internal Texture2D? Border { get; }
        internal string Badge { get; }
        internal int Order { get; set; }
        internal int WeaponOrder { get; set; } = int.MaxValue;
        internal string Group { get; set; } = string.Empty;
        internal bool IsPerk => Perk != null;
        internal string Key => $"{(Perk != null ? $"perk:{Weapon}" : Mutation != null ? "mutation" : "upgrade")}:{Name}";

        internal static int ComparePerks(Entry left, Entry right)
        {
            var weaponOrder = left.WeaponOrder.CompareTo(right.WeaponOrder);
            if (weaponOrder != 0) return weaponOrder;
            var weapon = string.Compare(left.Group, right.Group, StringComparison.OrdinalIgnoreCase);
            if (weapon != 0) return weapon;
            var order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        internal static int CompareNumbered(Entry left, Entry right)
        {
            var number = left.Order.CompareTo(right.Order);
            return number != 0 ? number : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record EntryGroup(string Name, List<Entry> Entries);

    private sealed class EnemyPreview
    {
        internal EnemyPreview(GameObject root, Vector3 baseScale, GameObject background,
            List<MeshAnimatorBase> meshAnimators, bool needsPoseWarmup)
        {
            Root = root;
            BaseScale = baseScale;
            Background = background;
            MeshAnimators = meshAnimators;
            NeedsPoseWarmup = needsPoseWarmup;
        }
        internal GameObject Root { get; }
        internal Vector3 BaseScale { get; }
        internal GameObject Background { get; }
        internal List<MeshAnimatorBase> MeshAnimators { get; }
        internal bool NeedsPoseWarmup { get; }
        internal bool PoseFrozen { get; set; }
        internal Rect Area { get; set; }
        internal bool Positioned { get; set; }
    }

    private sealed record EnemyEntry(string Name, GameObject Prefab, Enemy? MiniBoss = null, int MiniBossNumber = -1,
        Texture2D? Icon = null)
    {
        internal static int Compare(EnemyEntry left, EnemyEntry right)
        {
            var category = Category(left).CompareTo(Category(right));
            if (category != 0) return category;
            var leftNumber = left.MiniBossNumber >= 0 ? left.MiniBossNumber : EnemyCatalogNumber(left.Name);
            var rightNumber = right.MiniBossNumber >= 0 ? right.MiniBossNumber : EnemyCatalogNumber(right.Name);
            var number = leftNumber.CompareTo(rightNumber);
            return number != 0 ? number : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static int Category(EnemyEntry entry) => entry.MiniBoss != null ? 2
            : entry.Name.StartsWith("Elite:", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}

[HarmonyPatch(typeof(mutationUpgrade), nameof(mutationUpgrade.mutationUpgradeStat))]
internal static class MutationSelectedPatch
{
    private static void Postfix(mutationUpgrade __instance) => SandboxOverlay.RecordMutationSelected(__instance);
}

