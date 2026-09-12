using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

partial class Dashboard
{
    int appSort = 1;
    bool appAscending;
    void SetAppSort(int column, bool ascending)
    {
        appSort = column;
        appAscending = ascending;
        processSort.Text = new[]
        {
            "App",
            "CPU",
            "RAM"
        }[column] + " · " + (column == 0 ? (ascending ? "A–Z" : "Z–A") : (ascending ? "Low to high" : "High to low")) + " ▾";
        UpdateList();
    }

    void ShowAppSort()
    {
        var menu = new ContextMenuStrip()
        {
            BackColor = panel,
            ForeColor = white
        };
        string[] names =
        {
            "App: A–Z",
            "App: Z–A",
            "CPU: High to low",
            "CPU: Low to high",
            "RAM: High to low",
            "RAM: Low to high"
        };
        for (int i = 0; i < 6; i++)
        {
            int column = i / 2;
            bool ascending = column == 0 ? i % 2 == 0 : i % 2 == 1;
            var item = new ToolStripMenuItem(names[i])
            {
                Checked = appSort == column && appAscending == ascending
            };
            item.Click += (s, e) => SetAppSort(column, ascending);
            menu.Items.Add(item);
        }

        DisposeMenuAfterClick(menu);
        menu.Show(processSort, new Point(0, processSort.Height));
    }

    IEnumerable<IGrouping<string, ProcessReading>> SortAppGroups(IEnumerable<IGrouping<string, ProcessReading>> groups)
    {
        if (appSort == 0)
            return appAscending ? groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase) : groups.OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase);
        Func<IGrouping<string, ProcessReading>, double> value = g => g.Sum(p => appSort == 1 ? (double.IsNaN(p.Cpu) ? 0 : p.Cpu) : p.Memory);
        var available = groups.OrderBy(g => appSort == 1 && g.All(p => double.IsNaN(p.Cpu)));
        return (appAscending ? available.ThenBy(value) : available.ThenByDescending(value)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
    }

    IEnumerable<ProcessReading> SortAppMembers(IEnumerable<ProcessReading> members)
    {
        if (appSort == 0)
            return appAscending ? members.OrderBy(p => p.Id) : members.OrderByDescending(p => p.Id);
        var available = members.OrderBy(p => appSort == 1 && double.IsNaN(p.Cpu));
        Func<ProcessReading, double> value = p => appSort == 1 ? (double.IsNaN(p.Cpu) ? 0 : p.Cpu) : p.Memory;
        return (appAscending ? available.ThenBy(value) : available.ThenByDescending(value)).ThenBy(p => p.Id);
    }

    void VerifyAppSorting()
    {
        page = 5;
        UpdateList();
        if (list.HeaderStyle != ColumnHeaderStyle.Clickable)
            throw new Exception("App headers are not clickable");
        var click = typeof(ListView).GetMethod("OnColumnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        click.Invoke(list, new object[] { new ColumnClickEventArgs(0) });
        if (appSort != 0 || !appAscending || !list.Columns[0].Text.Contains("↑"))
            throw new Exception("Header ascending click failed");
        click.Invoke(list, new object[] { new ColumnClickEventArgs(0) });
        if (appAscending || !list.Columns[0].Text.Contains("↓"))
            throw new Exception("Header descending click failed");
        SetAppSort(1, false);
        int savedSort = appSort;
        bool savedDirection = appAscending;
        try
        {
            var rows = new[]
            {
                new ProcessReading
                {
                    Name = "Alpha",
                    Id = 1,
                    Cpu = 3,
                    Memory = 5
                },
                new ProcessReading
                {
                    Name = "Alpha",
                    Id = 2,
                    Cpu = 4,
                    Memory = 5
                },
                new ProcessReading
                {
                    Name = "Beta",
                    Id = 3,
                    Cpu = 6,
                    Memory = 20
                },
                new ProcessReading
                {
                    Name = "Unknown",
                    Id = 4,
                    Memory = 1
                }
            };
            for (int c = 0; c < 3; c++)
                for (int d = 0; d < 2; d++)
                {
                    appSort = c;
                    appAscending = d == 0;
                    var ordered = SortAppGroups(rows.GroupBy(p => p.Name)).ToList();
                    string expected = c == 0 ? (appAscending ? "Alpha" : "Unknown") : c == 1 ? (appAscending ? "Beta" : "Alpha") : (appAscending ? "Unknown" : "Beta");
                    if (ordered[0].Key != expected)
                        throw new Exception("App sort order failed");
                    if (c == 1 && ordered.Last().Key != "Unknown")
                        throw new Exception("Unknown CPU sort failed");
                }

            appSort = 1;
            appAscending = false;
            if (SortAppMembers(rows.Take(2)).First().Id != 2)
                throw new Exception("Child process sorting failed");
        }
        finally
        {
            SetAppSort(savedSort, savedDirection);
        }
    }
}
