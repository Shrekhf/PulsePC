using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

partial class Dashboard
{
    readonly HashSet<string> expandedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Button expandApp;
    void InitProcessGroups()
    {
        list.ColumnClick += (s, e) =>
        {
            if (page == 5)
                SetAppSort(e.Column, appSort == e.Column ? !appAscending : e.Column == 0);
        };
        expandApp = Button("Expand group", ToggleAppGroup);
        list.DoubleClick += (s, e) => ToggleAppGroup();
        list.KeyDown += (s, e) =>
        {
            if (page == 5 && e.KeyCode == Keys.Enter && SelectedGroup() != null)
            {
                ToggleAppGroup();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }

    string SelectedGroup()
    {
        return page == 5 && list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as string : null;
    }

    void ToggleAppGroup()
    {
        string name = SelectedGroup();
        if (name == null)
            return;
        if (!expandedApps.Remove(name))
            expandedApps.Add(name);
        UpdateList();
    }

    void UpdateGroupButton()
    {
        if (expandApp == null)
            return;
        expandApp.Visible = page == 5;
        Place(expandApp, 246, 670, 205, 36);
        string name = SelectedGroup();
        expandApp.Enabled = name != null;
        expandApp.Text = name != null && expandedApps.Contains(name) ? "Collapse group" : "Expand group";
    }

    void PopulateApps(string selected)
    {
        list.HeaderStyle = ColumnHeaderStyle.Clickable;
        list.Columns[0].Text = "App";
        list.Columns[1].Text = "CPU";
        list.Columns[2].Text = "RAM";
        list.Columns[appSort].Text += appAscending ? " ↑" : " ↓";
        string filter = processSearch.Text.Trim();
        var groups = data.Processes.Where(p => p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || p.Id.ToString().Contains(filter)).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var ordered = SortAppGroups(groups);
        foreach (var group in ordered)
        {
            var members = SortAppMembers(group).ToList();
            if (members.Count == 1)
            {
                AddAppRow(members[0], selected, false);
                continue;
            }

            bool expanded = expandedApps.Contains(group.Key);
            bool partial = members.Any(p => double.IsNaN(p.Cpu));
            bool unknown = members.All(p => double.IsNaN(p.Cpu));
            string cpu = unknown ? "—" : members.Sum(p => double.IsNaN(p.Cpu) ? 0 : p.Cpu).ToString("0.0") + " %" + (partial ? " (partial)" : "");
            var row = new ListViewItem(new[] { (expanded ? "▾  " : "▸  ") + group.Key + " (" + members.Count + ")", cpu, MemoryValue(members.Sum(p => p.Memory)) });
            row.Name = "group:" + group.Key.ToUpperInvariant();
            row.Tag = group.Key;
            list.Items.Add(row);
            row.Selected = row.Name == selected;
            if (expanded)
                foreach (var p in members)
                    AddAppRow(p, selected, true);
        }
    }

    void AddAppRow(ProcessReading p, string selected, bool child)
    {
        var row = new ListViewItem(new[] { (child ? "      " : "") + p.Name + "  /  " + p.Id, Val(p.Cpu, " %"), MemoryValue(p.Memory) });
        row.Name = p.Id + ":" + p.Start;
        row.Tag = p;
        list.Items.Add(row);
        row.Selected = row.Name == selected;
    }

    void VerifyProcessGroups()
    {
        var saved = data.Processes;
        try
        {
            data.Processes = new List<ProcessReading>
            {
                new ProcessReading
                {
                    Id = 101,
                    Name = "Duplicate",
                    Start = 1,
                    Cpu = 2,
                    Memory = 10
                },
                new ProcessReading
                {
                    Id = 102,
                    Name = "Duplicate",
                    Start = 2,
                    Cpu = 3,
                    Memory = 20
                }
            };
            page = 5;
            processSearch.Text = "";
            expandedApps.Clear();
            UpdateList();
            if (list.Items.Count != 1 || list.Items[0].SubItems[1].Text != "5.0 %" || list.Items[0].SubItems[2].Text != "30.0 MB")
                throw new Exception("Process aggregation failed");
            list.Items[0].Selected = true;
            UpdateAppActions();
            if (forceKill.Enabled)
                throw new Exception("Group allows process termination");
            ToggleAppGroup();
            if (list.Items.Count != 3)
                throw new Exception("Group expansion failed");
            processSearch.Text = "102";
            UpdateList();
            if (list.Items.Count != 1 || ((ProcessReading)list.Items[0].Tag).Id != 102)
                throw new Exception("PID filtering failed");
        }
        finally
        {
            data.Processes = saved;
            expandedApps.Clear();
            processSearch.Text = "";
            Navigate(0);
        }
    }
}
