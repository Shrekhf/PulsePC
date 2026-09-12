using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

partial class Dashboard
{
    bool memoryPercent;
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        public string Verb, File, Parameters, Directory;
        public int Show;
        public IntPtr Instance, IdList;
        public string Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr Icon, Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref ShellInfo info);
    void MenuAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            notice = ex.Message;
            MessageBox.Show(this, notice, "Pulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Invalidate();
    }

    string MemoryValue(double mb)
    {
        return memoryPercent ? (data.MemTotal > 0 ? (mb / (data.MemTotal * 1024) * 100).ToString("0.0") + " %" : "—") : mb.ToString("0.0") + " MB";
    }

    void AddResources(ToolStripItemCollection items)
    {
        var resource = new ToolStripMenuItem("Resource values");
        var memory = new ToolStripMenuItem("Memory");
        foreach (bool percent in new[]
        {
            false,
            true
        }

        )
        {
            bool chosen = percent;
            var item = new ToolStripMenuItem(percent ? "Percentages" : "Values (MB)")
            {
                Checked = memoryPercent == percent
            };
            item.Click += (s, e) =>
            {
                memoryPercent = chosen;
                UpdateList();
            };
            memory.DropDownItems.Add(item);
        }

        resource.DropDownItems.Add(memory);
        items.Add(resource);
    }

    ContextMenuStrip CreateTaskMenu(object target)
    {
        var menu = new ContextMenuStrip()
        {
            BackColor = panel,
            ForeColor = white,
            ShowImageMargin = false
        };
        string group = target as string;
        if (group != null)
        {
            var members = CaptureAppGroup(group);
            AddGroupActions(menu.Items, group, members);
            menu.Items.Add(new ToolStripSeparator());
            bool expanded = expandedApps.Contains(group);
            menu.Items.Add(expanded ? "Collapse" : "Expand", null, (s, e) =>
            {
                if (!expandedApps.Remove(group))
                    expandedApps.Add(group);
                UpdateList();
            });
            var children = new ToolStripMenuItem("Individual processes");
            foreach (var p in members.OrderBy(p => p.Id))
            {
                var row = new ToolStripMenuItem(p.Name + " · PID " + p.Id);
                PopulateTaskMenu(row.DropDownItems, p);
                children.DropDownItems.Add(row);
            }

            menu.Items.Add(children);
            menu.Items.Add(new ToolStripSeparator());
            AddResources(menu.Items);
        }
        else
        {
            var app = target as ProcessReading;
            if (app != null)
                PopulateTaskMenu(menu.Items, app);
        }

        DisposeMenuAfterClick(menu);
        return menu;
    }

    void PopulateTaskMenu(ToolStripItemCollection items, ProcessReading app)
    {
        bool accessible = false, window = false, mutable = false;
        try
        {
            using (var p = ResolveApp(app))
            {
                accessible = true;
                window = p.MainWindowHandle != IntPtr.Zero;
                bool critical;
                mutable = p.Id > 4 && p.Id != Process.GetCurrentProcess().Id && IsProcessCritical(p.Handle, out critical) && !critical;
            }
        }
        catch
        {
        }

        string unavailable = AppActionReason(app, true);
        if (unavailable != null)
            items.Add(new ToolStripMenuItem(unavailable) { Enabled = false });
        AddTaskItem(items, "Switch to", () => SwitchToApp(app), accessible && window);
        AddTaskItem(items, "Close app", () => CloseSelectedApp(app), mutable && window);
        AddTaskItem(items, "Kill process…", () => RunAppAction(true, app), mutable);
        items.Add(new ToolStripSeparator());
        AddResources(items);
        AddTaskItem(items, "Go to details", () => ShowAppDetails(app), accessible);
        items.Add(new ToolStripSeparator());
        AddTaskItem(items, "Open file location", () => RunAppAction(false, app), accessible);
        AddTaskItem(items, "Search online", () => Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + Uri.EscapeDataString(app.Name + " Windows process")) { UseShellExecute = true }), true);
        AddTaskItem(items, "Properties", () => ShowAppProperties(app), accessible);
        items.Add(new ToolStripSeparator());
        AddTaskItem(items, "Copy process name / PID", () => Clipboard.SetText(app.Name + " / " + app.Id), true);
    }

    void AddTaskItem(ToolStripItemCollection items, string title, Action action, bool enabled)
    {
        var item = new ToolStripMenuItem(title)
        {
            Enabled = enabled
        };
        item.Click += (s, e) => MenuAction(action);
        items.Add(item);
    }

    void ShowTaskMenu(object target, Point point)
    {
        var menu = CreateTaskMenu(target);
        menu.Show(list, point);
    }

    static void SwitchToApp(ProcessReading app)
    {
        using (var p = ResolveApp(app))
        {
            IntPtr window = p.MainWindowHandle;
            if (window == IntPtr.Zero)
                throw new InvalidOperationException("This process has no window to switch to.");
            if (IsIconic(window))
                ShowWindowAsync(window, 9);
            if (!SetForegroundWindow(window))
                throw new InvalidOperationException("Windows could not bring this app forward. Select it from the taskbar.");
        }
    }

    void ShowAppProperties(ProcessReading app)
    {
        string path = AppPath(app);
        var info = new ShellInfo
        {
            Size = Marshal.SizeOf(typeof(ShellInfo)),
            Mask = 12,
            Window = Handle,
            Verb = "properties",
            File = path,
            Show = 1
        };
        if (!ShellExecuteEx(ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    static string ProcessDetails(ProcessReading app)
    {
        using (var p = ResolveApp(app))
        {
            var text = new StringBuilder();
            text.AppendLine(p.ProcessName + " · PID " + p.Id);
            text.AppendLine();
            text.AppendLine("Started: " + p.StartTime.ToString("g"));
            text.AppendLine("Memory (working set): " + (p.WorkingSet64 / 1048576.0).ToString("0.0") + " MB");
            text.AppendLine("CPU time: " + p.TotalProcessorTime);
            text.AppendLine("Threads: " + p.Threads.Count);
            text.AppendLine("Handles: " + p.HandleCount);
            text.AppendLine("Priority: " + p.PriorityClass);
            text.AppendLine();
            try
            {
                string path = p.MainModule.FileName;
                text.AppendLine("Executable: " + path);
                var version = FileVersionInfo.GetVersionInfo(path);
                text.AppendLine("Description: " + version.FileDescription);
                text.AppendLine("Company: " + version.CompanyName);
                text.AppendLine("Version: " + version.FileVersion);
            }
            catch
            {
                text.AppendLine("Executable information is unavailable.");
            }

            return text.ToString();
        }
    }

    void ShowAppDetails(ProcessReading app)
    {
        string details = ProcessDetails(app);
        var dialog = new Form
        {
            Text = "Pulse · Process details",
            ClientSize = new Size(650, 410),
            BackColor = panel,
            ForeColor = white,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Text = details,
            BackColor = panel,
            ForeColor = white,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11)
        };
        dialog.Padding = new Padding(22);
        dialog.Controls.Add(text);
        dialog.Show(this);
    }

    void VerifyTaskMenu()
    {
        var self = Process.GetCurrentProcess();
        var app = new ProcessReading
        {
            Id = self.Id,
            Start = self.StartTime.ToUniversalTime().Ticks,
            Name = self.ProcessName
        };
        using (var menu = CreateTaskMenu(app))
        {
            string[] expected =
            {
                "Switch to",
                "Close app",
                "Kill process…",
                "Resource values",
                "Go to details",
                "Open file location",
                "Search online",
                "Properties",
                "Copy process name / PID"
            };
            foreach (string title in expected)
                if (!menu.Items.Cast<ToolStripItem>().Any(i => i.Text == title))
                    throw new Exception("Missing process menu item: " + title);
            if (menu.Items.Cast<ToolStripItem>().First(i => i.Text == "Kill process…").Enabled)
                throw new Exception("Self termination enabled");
            var resources = (ToolStripMenuItem)menu.Items.Cast<ToolStripItem>().First(i => i.Text == "Resource values");
            var memory = (ToolStripMenuItem)resources.DropDownItems[0];
            ((ToolStripMenuItem)memory.DropDownItems[1]).PerformClick();
            if (!memoryPercent)
                throw new Exception("Resource menu callback failed");
            ((ToolStripMenuItem)memory.DropDownItems[0]).PerformClick();
            if (memoryPercent)
                throw new Exception("Resource menu reset failed");
            menu.Show(this, new Point(260, 320));
            using (var bitmap = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
                bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshot), "process-menu.png"));
            }

            menu.Close();
        }

        if (!ProcessDetails(app).Contains("PID " + self.Id))
            throw new Exception("Process details failed");
        bool saved = memoryPercent;
        memoryPercent = false;
        if (MemoryValue(10) != "10.0 MB")
            throw new Exception("Memory values failed");
        memoryPercent = true;
        if (!MemoryValue(10).EndsWith(" %"))
            throw new Exception("Memory percentages failed");
        memoryPercent = saved;
    }
}
