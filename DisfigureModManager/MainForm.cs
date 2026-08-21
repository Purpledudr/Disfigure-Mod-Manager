using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace DisfigureModManager;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(28, 31, 40);
    private static readonly Color Muted = Color.FromArgb(101, 109, 124);
    private static readonly Color Accent = Color.FromArgb(107, 84, 255);
    private static readonly Color Canvas = Color.FromArgb(245, 246, 250);
    private readonly PluginService service = new();
    private readonly UserSettings settings;
    private readonly TextBox gameFolderBox = new();
    private readonly TextBox catalogUrlBox = new();
    private readonly Label locationState = new();
    private readonly Panel bepinExStatePanel = new();
    private readonly Button installBepInExButton = CreateButton("Install BepInEx", true);
    private readonly Label gameState = new();
    private readonly Label restartNotice = new();
    private readonly Label statusText = new();
    private readonly TabControl pluginTabs = new();
    private readonly TabPage installedTab = new("Installed");
    private readonly TabPage availableTab = new("Available");
    private readonly DataGridView installedGrid = new();
    private readonly DataGridView availableGrid = new();
    private readonly Button refreshButton = CreateButton("Refresh", false);
    private readonly Button updateAllButton = CreateButton("Update all", true);
    private readonly System.Windows.Forms.Timer processTimer = new() { Interval = 1500 };
    private IReadOnlyList<CatalogPlugin> catalog = [];
    private IReadOnlyList<PluginRow> rows = [];
    private bool busy;
    private bool gameRunning;

    public MainForm()
    {
        settings = SettingsStore.Load();
        settings.GameFolder = GameLocator.Detect(settings.GameFolder) ?? "";
        if (!string.IsNullOrWhiteSpace(settings.GameFolder)) SettingsStore.Save(settings);

        Text = "Disfigure Mod Manager";
        MinimumSize = new Size(900, 620);
        Size = new Size(1120, 730);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas;
        Font = new Font("Segoe UI", 9F);

        Controls.Add(BuildLayout());
        ConfigureGrid(installedGrid, available: false);
        ConfigureGrid(availableGrid, available: true);
        WireEvents();

        gameFolderBox.Text = settings.GameFolder;
        catalogUrlBox.Text = settings.CatalogUrl;
        UpdateGameState();
        processTimer.Start();
        Shown += async (_, _) => await RefreshAllAsync(fetchCatalog: true);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSettingsCard(), 0, 1);
        root.Controls.Add(BuildPluginArea(), 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);
        return root;
    }

    private static Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Padding = new Padding(28, 18, 28, 12) };
        var title = new Label
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 20F),
            Text = "Disfigure Mod Manager",
            Location = new Point(24, 14)
        };
        var subtitle = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(184, 190, 204),
            Font = new Font("Segoe UI", 9.5F),
            Text = "Install, update, and control your BepInEx plugins",
            Location = new Point(28, 56)
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private Control BuildSettingsCard()
    {
        var outer = new Panel { Dock = DockStyle.Top, Height = 145, Padding = new Padding(24, 16, 24, 8) };
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(16, 12, 16, 12),
            ColumnCount = 4,
            RowCount = 2
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        gameFolderBox.Dock = DockStyle.Fill;
        gameFolderBox.ReadOnly = true;
        gameFolderBox.BackColor = Color.White;
        gameFolderBox.BorderStyle = BorderStyle.FixedSingle;
        gameFolderBox.Margin = new Padding(0, 7, 10, 7);

        catalogUrlBox.Dock = DockStyle.Fill;
        catalogUrlBox.PlaceholderText = "https://raw.githubusercontent.com/.../plugins.json";
        catalogUrlBox.BorderStyle = BorderStyle.FixedSingle;
        catalogUrlBox.Margin = new Padding(0, 7, 10, 7);

        var browse = CreateButton("Browse…", false);
        browse.Name = "BrowseButton";
        browse.Margin = new Padding(0, 5, 10, 5);
        browse.Click += BrowseClicked;

        var saveCatalog = CreateButton("Save & refresh", false);
        saveCatalog.Margin = new Padding(0, 5, 0, 5);
        saveCatalog.Click += SaveCatalogClicked;

        locationState.Dock = DockStyle.Fill;
        locationState.TextAlign = ContentAlignment.MiddleLeft;
        locationState.Font = new Font("Segoe UI Semibold", 8.5F);
        installBepInExButton.Dock = DockStyle.Fill;
        installBepInExButton.Margin = Padding.Empty;
        installBepInExButton.Click += InstallBepInExClicked;
        bepinExStatePanel.Dock = DockStyle.Fill;
        bepinExStatePanel.Margin = new Padding(0, 5, 0, 5);
        bepinExStatePanel.Controls.Add(locationState);
        bepinExStatePanel.Controls.Add(installBepInExButton);

        card.Controls.Add(FieldLabel("Game folder"), 0, 0);
        card.Controls.Add(gameFolderBox, 1, 0);
        card.Controls.Add(browse, 2, 0);
        card.Controls.Add(bepinExStatePanel, 3, 0);
        card.Controls.Add(FieldLabel("Plugin catalog"), 0, 1);
        card.Controls.Add(catalogUrlBox, 1, 1);
        card.Controls.Add(saveCatalog, 2, 1);
        card.SetColumnSpan(saveCatalog, 2);
        outer.Controls.Add(card);
        return outer;
    }

    private Control BuildPluginArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 4, 24, 12),
            ColumnCount = 1,
            RowCount = 3
        };
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Plugins",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = Ink
        };
        refreshButton.Margin = new Padding(0, 7, 8, 5);
        updateAllButton.Margin = new Padding(0, 7, 0, 5);
        toolbar.Controls.Add(heading, 0, 0);
        toolbar.Controls.Add(refreshButton, 1, 0);
        toolbar.Controls.Add(updateAllButton, 2, 0);

        var noticePanel = new Panel { Dock = DockStyle.Fill };
        gameState.AutoSize = true;
        gameState.Location = new Point(3, 7);
        gameState.Font = new Font("Segoe UI Semibold", 9F);
        restartNotice.AutoSize = true;
        restartNotice.Text = "Restart Disfigure for plugin changes to take effect.";
        restartNotice.ForeColor = Color.FromArgb(146, 92, 12);
        restartNotice.Font = new Font("Segoe UI Semibold", 9F);
        restartNotice.Location = new Point(300, 7);
        restartNotice.Visible = false;
        noticePanel.Controls.Add(gameState);
        noticePanel.Controls.Add(restartNotice);

        pluginTabs.Dock = DockStyle.Fill;
        pluginTabs.Font = new Font("Segoe UI Semibold", 9F);
        pluginTabs.Padding = new Point(14, 6);
        installedTab.BackColor = Color.White;
        availableTab.BackColor = Color.White;
        installedTab.Controls.Add(installedGrid);
        availableTab.Controls.Add(availableGrid);
        pluginTabs.TabPages.Add(installedTab);
        pluginTabs.TabPages.Add(availableTab);
        area.Controls.Add(toolbar, 0, 0);
        area.Controls.Add(noticePanel, 0, 1);
        area.Controls.Add(pluginTabs, 0, 2);
        return area;
    }

    private Control BuildStatusBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 234, 240) };
        statusText.Dock = DockStyle.Fill;
        statusText.Padding = new Padding(25, 0, 0, 0);
        statusText.TextAlign = ContentAlignment.MiddleLeft;
        statusText.ForeColor = Muted;
        statusText.Text = "Ready";
        panel.Controls.Add(statusText);
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid, bool available)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeight = 38;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = Color.FromArgb(232, 234, 240);
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 54;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(238, 235, 255);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 241, 246);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5F);

        grid.Columns.Add(TextColumn("Name", "Name", 210));
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", Name = "Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220, SortMode = DataGridViewColumnSortMode.NotSortable });
        if (available)
        {
            grid.Columns.Add(TextColumn("Version", "Available", 90));
            grid.Columns.Add(TextColumn("Status", "CatalogStatus", 90));
            grid.Columns.Add(ButtonColumn("Install", "InstallAction", 90));
        }
        else
        {
            grid.Columns.Add(TextColumn("Installed", "Installed", 80));
            grid.Columns.Add(TextColumn("Latest", "Available", 70));
            grid.Columns.Add(TextColumn("Status", "Status", 74));
            grid.Columns.Add(ButtonColumn("Toggle", "ToggleAction", 70));
            grid.Columns.Add(ButtonColumn("Update", "UpdateAction", 70));
            grid.Columns.Add(ButtonColumn("Remove", "RemoveAction", 70));
        }
    }

    private void WireEvents()
    {
        refreshButton.Click += async (_, _) => await RefreshAllAsync(fetchCatalog: true);
        updateAllButton.Click += UpdateAllClicked;
        installedGrid.CellContentClick += GridCellContentClicked;
        availableGrid.CellContentClick += GridCellContentClicked;
        processTimer.Tick += (_, _) => UpdateGameState();
    }

    private async void BrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder containing Disfigure.exe",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(settings.GameFolder) ? settings.GameFolder : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!GameLocator.IsGameFolder(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder does not contain Disfigure.exe.", "Choose the Disfigure folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        settings.GameFolder = Path.GetFullPath(dialog.SelectedPath);
        SettingsStore.Save(settings);
        gameFolderBox.Text = settings.GameFolder;
        restartNotice.Visible = false;
        await RefreshAllAsync(fetchCatalog: false);
    }

    private async void SaveCatalogClicked(object? sender, EventArgs e)
    {
        settings.CatalogUrl = catalogUrlBox.Text.Trim();
        SettingsStore.Save(settings);
        await RefreshAllAsync(fetchCatalog: true);
    }

    private async void InstallBepInExClicked(object? sender, EventArgs e)
    {
        UpdateGameState();
        if (gameRunning)
        {
            MessageBox.Show(this, "Close Disfigure before installing BepInEx.", "Disfigure is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!GameLocator.IsGameFolder(settings.GameFolder))
        {
            MessageBox.Show(this, "Choose the folder containing Disfigure.exe first.", "Game folder needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new BepInExInstallDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedPackage is null) return;

        SetBusy(true);
        try
        {
            statusText.Text = $"Downloading BepInEx {dialog.SelectedPackage.Version}…";
            await service.InstallBepInExAsync(settings.GameFolder, dialog.SelectedPackage);
            await RefreshAllAsync(fetchCatalog: false, manageBusy: false);
            statusText.Text = $"BepInEx {dialog.SelectedPackage.Version} installed successfully.";
            MessageBox.Show(
                this,
                "BepInEx is installed. Start Disfigure once to let BepInEx finish its first-time setup; this can take a few minutes.",
                "BepInEx installed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void GridCellContentClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || busy) return;
        if (grid.Rows[e.RowIndex].Tag is not PluginRow item) return;
        var action = grid.Columns[e.ColumnIndex].Name;

        if (action == "ToggleAction" && item.Installed is not null)
        {
            await ChangePluginsAsync(() =>
            {
                PluginService.Toggle(settings.GameFolder, item.Installed);
                return Task.CompletedTask;
            }, $"{item.Name} is now {(item.Installed.Enabled ? "disabled" : "enabled")}.");
        }
        else if ((action == "InstallAction" || action == "UpdateAction") && item.Catalog is { Available: true } && (item.Installed is null || item.HasUpdate))
        {
            await ChangePluginsAsync(
                () => service.InstallOrUpdateAsync(settings.GameFolder, item.Catalog, item.Installed),
                item.Installed is null ? $"Installed {item.Name}." : $"Updated {item.Name}.");
        }
        else if (action == "RemoveAction" && item.Installed is not null)
        {
            var answer = MessageBox.Show(this, $"Uninstall {item.Name}?\n\nThe DLL will be permanently deleted.", "Uninstall plugin", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            await ChangePluginsAsync(() =>
            {
                PluginService.Uninstall(item.Installed);
                return Task.CompletedTask;
            }, $"Uninstalled {item.Name}.");
        }
    }

    private async void UpdateAllClicked(object? sender, EventArgs e)
    {
        var updates = rows.Where(row => row.HasUpdate && row.Catalog is not null && row.Installed is not null).ToArray();
        if (updates.Length == 0) return;
        await ChangePluginsAsync(async () =>
        {
            for (var index = 0; index < updates.Length; index++)
            {
                statusText.Text = $"Updating {updates[index].Name} ({index + 1} of {updates.Length})…";
                await service.InstallOrUpdateAsync(settings.GameFolder, updates[index].Catalog!, updates[index].Installed);
            }
        }, $"Updated {updates.Length} plugin{(updates.Length == 1 ? "" : "s")}.");
    }

    private async Task ChangePluginsAsync(Func<Task> action, string success)
    {
        if (!CanChangePlugins()) return;
        SetBusy(true);
        try
        {
            await action();
            restartNotice.Visible = PluginService.IsGameRunning();
            await RefreshAllAsync(fetchCatalog: false, manageBusy: false);
            statusText.Text = success;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAllAsync(bool fetchCatalog, bool manageBusy = true)
    {
        if (manageBusy) SetBusy(true);
        try
        {
            UpdateLocationState();
            if (fetchCatalog)
            {
                if (string.IsNullOrWhiteSpace(settings.CatalogUrl))
                {
                    catalog = [];
                    statusText.Text = "Set the public plugins.json URL to see available plugins.";
                }
                else
                {
                    statusText.Text = "Loading plugin catalog…";
                    catalog = await service.FetchCatalogAsync(settings.CatalogUrl);
                    statusText.Text = $"Catalog loaded: {catalog.Count} plugin{(catalog.Count == 1 ? "" : "s")}.";
                }
            }

            var installed = IsReady() ? PluginService.Scan(settings.GameFolder) : [];
            rows = PluginService.Merge(catalog, installed);
            PopulateGrid();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            var installed = IsReady() ? PluginService.Scan(settings.GameFolder) : [];
            rows = PluginService.Merge(catalog, installed);
            PopulateGrid();
        }
        finally
        {
            if (manageBusy) SetBusy(false);
        }
    }

    private void PopulateGrid()
    {
        installedGrid.Rows.Clear();
        availableGrid.Rows.Clear();

        var installedRows = rows.Where(item => item.Installed is not null).ToArray();
        var availableRows = rows.Where(item => item.Installed is null && item.Catalog is not null).ToArray();
        foreach (var item in installedRows)
        {
            var toggle = item.Installed is null ? "" : item.Installed.Enabled ? "Disable" : "Enable";
            var update = item.HasUpdate ? "Update" : "";
            var remove = item.Installed is null ? "" : "Uninstall";
            var index = installedGrid.Rows.Add(item.Name, item.Description, item.InstalledVersion, item.AvailableVersion, item.Status, toggle, update, remove);
            var row = installedGrid.Rows[index];
            row.Tag = item;
            row.Cells[4].Style.ForeColor = item.Status switch
            {
                "Enabled" => Color.FromArgb(29, 133, 83),
                "Disabled" => Color.FromArgb(176, 107, 24),
                _ => Muted
            };
            row.Cells[4].Style.Font = new Font("Segoe UI Semibold", 8.5F);
        }

        foreach (var item in availableRows)
        {
            var isAvailable = item.Catalog!.Available;
            var index = availableGrid.Rows.Add(item.Name, item.Description, item.AvailableVersion, isAvailable ? "Available" : "Coming soon", isAvailable ? "Install" : "");
            var row = availableGrid.Rows[index];
            row.Tag = item;
            row.Cells[3].Style.ForeColor = isAvailable ? Color.FromArgb(29, 133, 83) : Color.FromArgb(176, 107, 24);
            row.Cells[3].Style.Font = new Font("Segoe UI Semibold", 8.5F);
        }

        installedTab.Text = $"Installed ({installedRows.Length})";
        availableTab.Text = $"Available ({availableRows.Length})";
        installedGrid.ClearSelection();
        installedGrid.CurrentCell = null;
        availableGrid.ClearSelection();
        availableGrid.CurrentCell = null;

        if (rows.Count == 0 && IsReady()) statusText.Text = "No plugins found. Add entries to your catalog or place DLLs in BepInEx/plugins.";
        UpdateActionState();
    }

    private void UpdateGameState()
    {
        gameRunning = PluginService.IsGameRunning();
        if (!gameRunning) restartNotice.Visible = false;
        gameState.Text = gameRunning ? "●  Disfigure is running — changes are locked" : "●  Disfigure is closed — ready for changes";
        gameState.ForeColor = gameRunning ? Color.FromArgb(190, 67, 67) : Color.FromArgb(29, 133, 83);
        UpdateActionState();
    }

    private void UpdateLocationState()
    {
        if (!GameLocator.IsGameFolder(settings.GameFolder))
        {
            locationState.Text = "Not found";
            locationState.ForeColor = Color.FromArgb(190, 67, 67);
            locationState.Visible = true;
            installBepInExButton.Visible = false;
        }
        else if (!PluginService.HasBepInEx(settings.GameFolder))
        {
            locationState.Visible = false;
            installBepInExButton.Visible = true;
        }
        else
        {
            locationState.Text = "BepInEx detected";
            locationState.ForeColor = Color.FromArgb(29, 133, 83);
            locationState.Visible = true;
            installBepInExButton.Visible = false;
        }
    }

    private bool CanChangePlugins()
    {
        UpdateGameState();
        if (gameRunning)
        {
            MessageBox.Show(this, "Close Disfigure before changing plugins.", "Disfigure is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (!GameLocator.IsGameFolder(settings.GameFolder))
        {
            MessageBox.Show(this, "Choose the folder containing Disfigure.exe first.", "Game folder needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (!PluginService.HasBepInEx(settings.GameFolder))
        {
            MessageBox.Show(this, "BepInEx is not installed in the selected Disfigure folder.", "BepInEx not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private bool IsReady() => GameLocator.IsGameFolder(settings.GameFolder) && PluginService.HasBepInEx(settings.GameFolder);

    private void SetBusy(bool value)
    {
        busy = value;
        UseWaitCursor = value;
        refreshButton.Enabled = !value;
        catalogUrlBox.Enabled = !value;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var canModify = !busy && !gameRunning && IsReady();
        installedGrid.Enabled = canModify;
        availableGrid.Enabled = canModify;
        updateAllButton.Enabled = canModify && rows.Any(row => row.HasUpdate);
        installBepInExButton.Enabled = !busy && !gameRunning && GameLocator.IsGameFolder(settings.GameFolder) && !PluginService.HasBepInEx(settings.GameFolder);
    }

    private void ShowError(string message)
    {
        statusText.Text = message;
        MessageBox.Show(this, message, "Disfigure Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static Label FieldLabel(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Muted,
        Font = new Font("Segoe UI Semibold", 9F)
    };

    private static DataGridViewTextBoxColumn TextColumn(string title, string name, int width) => new()
    {
        HeaderText = title,
        Name = name,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private static DataGridViewButtonColumn ButtonColumn(string title, string name, int width) => new()
    {
        HeaderText = title,
        Name = name,
        Width = width,
        FlatStyle = FlatStyle.Flat,
        SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F),
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(205, 208, 217);
        return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            processTimer.Dispose();
            service.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class BepInExInstallDialog : Form
{
    private readonly ComboBox packageBox = new();

    public BepInExPackage? SelectedPackage => packageBox.SelectedItem as BepInExPackage;

    public BepInExInstallDialog()
    {
        Text = "Install BepInEx";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 250);
        BackColor = Color.FromArgb(245, 246, 250);
        Font = new Font("Segoe UI", 9F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Choose a BepInEx version",
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = Color.FromArgb(28, 31, 40),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Disfigure uses its Windows build on Linux through Proton, so both platforms use the Windows x64 IL2CPP package.",
            ForeColor = Color.FromArgb(101, 109, 124),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Version",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(101, 109, 124),
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 2);

        packageBox.Dock = DockStyle.Fill;
        packageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        packageBox.DataSource = BepInExPackages.All.ToArray();
        layout.Controls.Add(packageBox, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 9, 0, 0) };
        var install = new Button
        {
            Text = "Install",
            UseMnemonic = false,
            DialogResult = DialogResult.OK,
            Size = new Size(96, 32),
            BackColor = Color.FromArgb(107, 84, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9F)
        };
        install.FlatAppearance.BorderColor = install.BackColor;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(88, 32), FlatStyle = FlatStyle.Flat };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(205, 208, 217);
        buttons.Controls.Add(install);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);

        AcceptButton = install;
        CancelButton = cancel;
        Controls.Add(layout);
    }
}
