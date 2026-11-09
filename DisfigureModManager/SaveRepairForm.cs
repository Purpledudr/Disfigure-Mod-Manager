namespace DisfigureModManager;

internal sealed class SaveRepairForm : Form
{
    private const string CloudHelp = "Turn off Steam Cloud in Disfigure’s Steam Properties → General before applying. Steam Stats may still merge progression online.";
    private readonly SaveRepair repair;
    private readonly Label liveStatus = new() { AutoSize = true };
    private readonly Label backupStatus = new() { AutoSize = true };
    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Profile source" };
    private readonly FlowLayoutPanel custom = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly NumericUpDown credits = new() { Maximum = decimal.MaxValue, ThousandsSeparator = true, Width = 190, AccessibleName = "Credits" };
    private readonly CheckedListBox weapons = new() { CheckOnClick = true, Width = 320, Height = 151, AccessibleName = "Weapons to unlock" };
    private readonly CheckBox map2 = new() { AutoSize = true, Text = "Unlock Map 2" };
    private readonly CheckBox map3 = new() { AutoSize = true, Text = "Unlock Map 3" };
    private readonly Button undo = new() { AutoSize = true, Text = "Undo last repair" };
    private SaveInspection backup = null!;

    private enum ProfileSource { Backup, Starter, Custom }
    private sealed record Choice(ProfileSource Source, string Name)
    {
        public override string ToString() => Name;
    }

    internal SaveRepairForm(SaveRepair repair)
    {
        this.repair = repair;
        Text = "Repair Disfigure Save";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 635);
        MinimumSize = new Size(540, 485);

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16)
        };
        layout.Controls.Add(Note("Save folder\n" + repair.SaveDirectory));
        layout.Controls.Add(liveStatus);
        layout.Controls.Add(backupStatus);
        layout.Controls.Add(Note("Choose a profile"));
        layout.Controls.Add(source);

        var creditRow = Row(Note("Credits"), credits);
        var selectAll = new Button { Text = "Select all weapons", AutoSize = true };
        selectAll.Click += (_, _) =>
        {
            for (var i = 0; i < weapons.Items.Count; i++)
                weapons.SetItemChecked(i, true);
        };
        foreach (var weapon in SaveRepair.Weapons.Skip(1)) weapons.Items.Add(weapon.Name);
        map3.CheckedChanged += (_, _) =>
        {
            if (map3.Checked) map2.Checked = true;
            map2.Enabled = !map3.Checked;
        };
        custom.Controls.Add(creditRow);
        custom.Controls.Add(Note("Pistol and Map 1 are always available."));
        custom.Controls.Add(weapons);
        custom.Controls.Add(Row(selectAll, map2, map3));
        layout.Controls.Add(custom);
        layout.Controls.Add(Note(CloudHelp));
        layout.Controls.Add(Note("Then exit Disfigure and Steam completely before applying."));

        var apply = new Button { Text = "Preview and apply…", AutoSize = true };
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        layout.Controls.Add(Row(apply, undo, close));
        CancelButton = close;
        Controls.Add(layout);
        source.SelectedIndexChanged += (_, _) => custom.Visible = SelectedSource == ProfileSource.Custom;
        apply.Click += (_, _) => Run(Apply);
        undo.Click += (_, _) => Run(Undo);
        RefreshProfiles();
    }

    private ProfileSource SelectedSource => (source.SelectedItem as Choice)?.Source ?? ProfileSource.Starter;

    private void RefreshProfiles()
    {
        var live = repair.Live;
        backup = repair.Backup;
        var recommend = SaveRepair.RecommendBackup(live, backup);
        var prior = SelectedSource;
        liveStatus.Text = "Live save   " + live.Describe();
        backupStatus.Text = "Game backup   " + backup.Describe();
        source.Items.Clear();
        if (backup.Bytes is not null)
            source.Items.Add(new Choice(ProfileSource.Backup, recommend ? "Restore game backup (recommended)" : "Restore game backup"));
        source.Items.Add(new Choice(ProfileSource.Starter, "Create starter profile"));
        source.Items.Add(new Choice(ProfileSource.Custom, "Create custom profile"));
        source.SelectedIndex = recommend && prior == ProfileSource.Starter ? 0 :
            source.Items.Cast<Choice>().ToList().FindIndex(choice => choice.Source == prior);
        if (source.SelectedIndex < 0) source.SelectedIndex = 0;
        undo.Enabled = repair.LatestArchive is not null;
    }

    private void Apply()
    {
        var selected = SelectedSource;
        var useBackup = selected == ProfileSource.Backup;
        var weaponIds = selected == ProfileSource.Custom
            ? weapons.CheckedIndices.Cast<int>().Select(index => SaveRepair.Weapons[index + 1].Id).ToArray()
            : [];
        var customCredits = selected == ProfileSource.Custom ? (float)credits.Value : 0f;
        var addMap2 = selected == ProfileSource.Custom && map2.Checked;
        var addMap3 = selected == ProfileSource.Custom && map3.Checked;
        var bytes = useBackup ? backup.Bytes! : SaveRepair.CreateProfile(customCredits, weaponIds, addMap2, addMap3);
        var preview = useBackup ? "Restore game backup\n" + backup.Describe()
            : $"{(selected == ProfileSource.Starter ? "Starter" : "Custom")} profile\n" +
              $"{customCredits:N0} credits · {weaponIds.Length + 1} weapons · {(addMap3 ? "Maps 1–3" : addMap2 ? "Maps 1–2" : "Map 1")}\n" +
              "Pistol" + (weaponIds.Length == 0 ? "" : ", " + string.Join(", ", SaveRepair.Weapons.Where(w => weaponIds.Contains(w.Id)).Select(w => w.Name)));
        var queued = repair.HasQueuedPreset;
        if (!Confirm(preview, queued)) return;
        ShowArchive("Save repaired.", repair.Apply(bytes, useBackup, queued));
    }

    private void Undo()
    {
        if (repair.LatestArchive is not string archive) return;
        var queued = repair.HasQueuedPreset;
        if (!Confirm("Restore the files and progression settings from before the last repair. Any original save problem may return.", queued)) return;
        ShowArchive("Original profile restored.", repair.Undo(archive, queued));
    }

    private bool Confirm(string preview, bool queued) => MessageBox.Show(this,
        preview + "\n\nYour current profile will be archived before replacement." +
        (queued ? "\nA queued standalone preset will also be archived and canceled." : ""),
        "Confirm save replacement", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK;

    private void ShowArchive(string result, string archive)
    {
        RefreshProfiles();
        MessageBox.Show(this, $"{result}\n\nPrevious files are in\n{archive}\n\nKeep Steam Cloud off when you first relaunch. Steam Stats may still merge progression online.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(570, 0), Margin = new Padding(3, 6, 3, 6)
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange(controls);
        return row;
    }
}
