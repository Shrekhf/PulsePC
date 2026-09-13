using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

class VolumeReading
{
    public string Name, Label, Format;
    public double Total, Free;
    public double UsedPercent
    {
        get
        {
            return Total > 0 ? 100 * (Total - Free) / Total : 0;
        }
    }
}

class ProcessReading
{
    public int Id;
    public long Start;
    public string Name;
    public double Memory, Cpu = double.NaN;
}

partial class Reading
{
    public double DiskRead = double.NaN, DiskWrite = double.NaN;
    public List<VolumeReading> Volumes = new List<VolumeReading>();
    public List<ProcessReading> Processes = new List<ProcessReading>();
    public TimeSpan Uptime;
    public string DiskError = "";
}

partial class Collector
{
    class ProcessStamp
    {
        public double Cpu;
        public long Start;
        public long At;
    }

    Dictionary<int, ProcessStamp> previousProcesses = new Dictionary<int, ProcessStamp>();
    PerformanceCounter diskRead, diskWrite;
    bool disksInitialized;
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern ulong GetTickCount64();
    public static double ProcessPercent(double delta, double elapsed, int cores)
    {
        return elapsed > 0 && cores > 0 ? Math.Max(0, Math.Min(100, 100 * delta / elapsed / cores)) : double.NaN;
    }

    void CollectExtras(Reading r)
    {
        r.Uptime = TimeSpan.FromMilliseconds(GetTickCount64());
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;
                r.Volumes.Add(new VolumeReading() { Name = drive.Name, Label = drive.VolumeLabel, Format = drive.DriveFormat, Total = drive.TotalSize / 1073741824.0, Free = drive.AvailableFreeSpace / 1073741824.0 });
            }
            catch
            {
            }
        }

        try
        {
            if (!disksInitialized)
            {
                disksInitialized = true;
                diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                diskRead.NextValue();
                diskWrite.NextValue();
            }
            else if (diskRead != null && diskWrite != null)
            {
                r.DiskRead = diskRead.NextValue() / 1048576.0;
                r.DiskWrite = diskWrite.NextValue() / 1048576.0;
            }
        }
        catch
        {
            r.DiskError = "Disk counters unavailable";
        }

        var next = new Dictionary<int, ProcessStamp>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var item = new ProcessReading()
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Memory = p.WorkingSet64 / 1048576.0
                    };
                    if (item.Id == 0)
                        continue;
                    try
                    {
                        double time = p.TotalProcessorTime.TotalSeconds;
                        long start = p.StartTime.ToUniversalTime().Ticks;
                        item.Start = start;
                        long now = Stopwatch.GetTimestamp();
                        ProcessStamp old;
                        if (previousProcesses.TryGetValue(p.Id, out old) && old.Start == start)
                            item.Cpu = ProcessPercent(time - old.Cpu, (now - old.At) / (double)Stopwatch.Frequency, Environment.ProcessorCount);
                        next[p.Id] = new ProcessStamp()
                        {
                            Cpu = time,
                            Start = start,
                            At = now
                        };
                    }
                    catch
                    {
                    }

                    r.Processes.Add(item);
                }
                catch
                {
                }
            }
        }

        previousProcesses = next;
    }
}

// A threshold alerts once per crossing, and rearms only after its recovery margin.
class Threshold
{
    public bool Active;
    int consecutive;
    public int Evaluate(double value, double limit, double margin, bool high)
    {
        if (double.IsNaN(value))
        {
            consecutive = 0;
            return 0;
        }

        bool breached = high ? value >= limit : value <= limit;
        bool recovered = high ? value < limit - margin : value > limit + margin;
        if (!Active)
        {
            consecutive = breached ? consecutive + 1 : 0;
            if (consecutive >= 2)
            {
                Active = true;
                consecutive = 0;
                return 1;
            }
        }
        else if (recovered)
        {
            Active = false;
            consecutive = 0;
            return -1;
        }

        return 0;
    }
}

class Preferences
{
    public bool AlwaysOnTop, MinimizeToTray, Alerts = true;
    public int Interval = 2, GpuLimit = 80, RamLimit = 90, DiskLimit = 10;
    public static Preferences Load(string path)
    {
        var p = new Preferences();
        try
        {
            foreach (string l in File.ReadAllLines(path))
            {
                var x = l.Split('=');
                if (x.Length != 2)
                    continue;
                int n;
                switch (x[0])
                {
                    case "top":
                        p.AlwaysOnTop = x[1] == "true";
                        break;
                    case "tray":
                        p.MinimizeToTray = x[1] == "true";
                        break;
                    case "alerts":
                        p.Alerts = x[1] == "true";
                        break;
                    case "interval":
                        if (int.TryParse(x[1], out n) && new[]
                        {
                            1,
                            2,
                            5
                        }.Contains(n))
                            p.Interval = n;
                        break;
                    case "gpu":
                        if (int.TryParse(x[1], out n) && n >= 50 && n <= 100)
                            p.GpuLimit = n;
                        break;
                    case "ram":
                        if (int.TryParse(x[1], out n) && n >= 50 && n <= 99)
                            p.RamLimit = n;
                        break;
                    case "disk":
                        if (int.TryParse(x[1], out n) && n >= 1 && n <= 30)
                            p.DiskLimit = n;
                        break;
                }
            }
        }
        catch
        {
        }

        return p;
    }

    public void Save(string path)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, "top=" + AlwaysOnTop.ToString().ToLowerInvariant() + "\ntray=" + MinimizeToTray.ToString().ToLowerInvariant() + "\nalerts=" + Alerts.ToString().ToLowerInvariant() + "\ninterval=" + Interval + "\ngpu=" + GpuLimit + "\nram=" + RamLimit + "\ndisk=" + DiskLimit);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }
}

partial class Dashboard
{
    Preferences preferences;
    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
    List<Reading> history = new List<Reading>();
    List<DateTime> sampleTimes = new List<DateTime>();
    Dictionary<string, Threshold> thresholds = new Dictionary<string, Threshold>();
    List<string[]> events = new List<string[]>();
    NotifyIcon tray;
    ContextMenuStrip trayMenu;
    TextBox processSearch;
    Button processSort, rangeButton, volumeButton;
    NumericUpDown gpuLimit, ramLimit, diskLimit;
    CheckBox topToggle, trayToggle, alertToggle;
    Button intervalButton;
    int rangeSeconds = 120, volumeIndex;
    int snapshotStage;
    string[] pageNames =
    {
        "Overview",
        "Hardware",
        "Thermals",
        "System",
        "Storage",
        "Apps",
        "Settings"
    };
    string[] pageTitles =
    {
        "Your PC. In focus.",
        "Inside your PC.",
        "Keep your cool.",
        "Your system.",
        "Storage, at a glance.",
        "Running apps.",
        "Make Pulse yours."
    };
    string[] pageDescriptions =
    {
        "A clear view of performance, temperatures, and activity.",
        "The components that power this computer.",
        "Live temperatures and the details behind every reading.",
        "Power preferences and familiar Windows controls.",
        "Capacity, health, and activity in one place.",
        "Find the apps using your CPU and memory.",
        "Appearance, desktop behavior, and alerts."
    };
    void InitFeatures()
    {
        preferences = snapshot == null ? Preferences.Load(settingsPath) : new Preferences();
        TopMost = preferences.AlwaysOnTop;
        timer.Interval = preferences.Interval * 1000;
        trayMenu = new ContextMenuStrip()
        {
            BackColor = panel,
            ForeColor = white
        };
        trayMenu.Items.Add("Open Pulse", null, (s, e) => RestoreWindow());
        trayMenu.Items.Add("Pause / resume", null, (s, e) => pause.PerformClick());
        trayMenu.Items.Add("Exit", null, (s, e) => Close());
        tray = new NotifyIcon()
        {
            Icon = SystemIcons.Application,
            Text = "Pulse PC",
            ContextMenuStrip = trayMenu,
            Visible = snapshot == null
        };
        tray.DoubleClick += (s, e) => RestoreWindow();
        Resize += (s, e) =>
        {
            if (preferences.MinimizeToTray && WindowState == FormWindowState.Minimized && snapshot == null)
                Hide();
        };
        FormClosed += (s, e) =>
        {
            tray.Dispose();
            trayMenu.Dispose();
        };
        var csv = Button("Export CSV", ExportHistory);
        csv.Tag = "csv";
        rangeButton = Button("History · 2 min", () =>
        {
            rangeSeconds = rangeSeconds == 120 ? 600 : 120;
            rangeButton.Text = "History · " + (rangeSeconds / 60) + " min";
            Invalidate();
        });
        rangeButton.Tag = "range";
        processSearch = new TextBox()
        {
            BackColor = panel,
            ForeColor = white,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Filter processes by name or PID"
        };
        processSearch.TextChanged += (s, e) => UpdateList();
        Controls.Add(processSearch);
        processSort = Button("CPU · High to low ▾", ShowAppSort);
        processSort.Tag = "sort";
        volumeButton = Button("Select drive ▾", () =>
        {
            var menu = new ContextMenuStrip()
            {
                BackColor = panel,
                ForeColor = white
            };
            for (int i = 0; i < data.Volumes.Count; i++)
            {
                int chosen = i;
                menu.Items.Add(data.Volumes[i].Name + " " + data.Volumes[i].Label, null, (s, e) =>
                {
                    volumeIndex = chosen;
                    Invalidate();
                });
            }

            DisposeMenuAfterClick(menu);
            menu.Show(volumeButton, new Point(0, volumeButton.Height));
        });
        volumeButton.Tag = "volume";
        topToggle = Toggle("Always on top", preferences.AlwaysOnTop, () =>
        {
            preferences.AlwaysOnTop = topToggle.Checked;
            TopMost = topToggle.Checked;
            SavePreferences();
        });
        trayToggle = Toggle("Minimize to system tray", preferences.MinimizeToTray, () =>
        {
            preferences.MinimizeToTray = trayToggle.Checked;
            SavePreferences();
        });
        alertToggle = Toggle("Enable threshold alerts", preferences.Alerts, () =>
        {
            preferences.Alerts = alertToggle.Checked;
            thresholds.Clear();
            SavePreferences();
        });
        gpuLimit = Number(50, 100, preferences.GpuLimit);
        ramLimit = Number(50, 99, preferences.RamLimit);
        diskLimit = Number(1, 30, preferences.DiskLimit);
        gpuLimit.ValueChanged += (s, e) =>
        {
            preferences.GpuLimit = (int)gpuLimit.Value;
            thresholds.Clear();
            SavePreferences();
        };
        ramLimit.ValueChanged += (s, e) =>
        {
            preferences.RamLimit = (int)ramLimit.Value;
            thresholds.Clear();
            SavePreferences();
        };
        diskLimit.ValueChanged += (s, e) =>
        {
            preferences.DiskLimit = (int)diskLimit.Value;
            thresholds.Clear();
            SavePreferences();
        };
        intervalButton = Button("Refresh · " + preferences.Interval + " seconds", () =>
        {
            preferences.Interval = preferences.Interval == 1 ? 2 : preferences.Interval == 2 ? 5 : 1;
            timer.Interval = preferences.Interval * 1000;
            intervalButton.Text = "Refresh · " + preferences.Interval + " seconds";
            SavePreferences();
        });
        intervalButton.Tag = "interval";
        var clear = Button("Clear event log", () =>
        {
            events.Clear();
            UpdateList();
        });
        clear.Tag = "clear";
        InitRoadmap();
    }

    CheckBox Toggle(string title, bool value, Action changed)
    {
        var c = new CheckBox()
        {
            Text = title,
            Checked = value,
            BackColor = panel,
            ForeColor = white,
            AutoSize = false
        };
        c.CheckedChanged += (s, e) => changed();
        Controls.Add(c);
        return c;
    }

    NumericUpDown Number(int min, int max, int value)
    {
        var n = new NumericUpDown()
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            BackColor = bg,
            ForeColor = white,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(n);
        return n;
    }

    void SavePreferences()
    {
        if (snapshot != null)
            return;
        try
        {
            preferences.Save(settingsPath);
            notice = "Preferences saved.";
        }
        catch (Exception e)
        {
            notice = "Could not save preferences: " + e.Message;
        }

        Invalidate();
    }

    void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void LayoutFeatures()
    {
        if (preferences == null)
            return;
        foreach (Control c in Controls)
        {
            string tag = c.Tag as string;
            if (tag == "csv")
            {
                c.Visible = true;
                Place(c, 22, 649, 165, 37);
            }
            else if (tag == "range")
            {
                c.Visible = page == 0;
                Place(c, 715, 346, 159, 32);
            }
            else if (tag == "sort")
            {
                c.Visible = false;
            }
            else if (tag == "volume")
            {
                c.Visible = page == 4;
                Place(c, 995, 344, 209, 35);
            }
            else if (tag == "interval")
            {
                c.Visible = page == 6;
                Place(c, 916, 205, 280, 38);
            }
            else if (tag == "clear")
            {
                c.Visible = page == 6;
                Place(c, 1020, 465, 206, 32);
            }
        }

        processSearch.Visible = page == 5;
        Place(processSearch, 246, 259, 980, 33);
        topToggle.Visible = trayToggle.Visible = alertToggle.Visible = gpuLimit.Visible = ramLimit.Visible = diskLimit.Visible = page == 6;
        Place(topToggle, 268, 206, 245, 32);
        Place(trayToggle, 552, 206, 329, 32);
        Place(alertToggle, 268, 306, 390, 30);
        Place(gpuLimit, 270, 390, 230, 32);
        Place(ramLimit, 581, 390, 230, 32);
        Place(diskLimit, 893, 390, 230, 32);
        listHost.Visible = page == 1 || page == 2 || page == 5 || page == 6;
        if (page == 5)
            Place(listHost, 246, 311, 980, 389);
        if (page == 6)
            Place(listHost, 246, 514, 980, 210);
        if (page == 5)
        {
            list.Columns[0].Width = (int)(480 * scale);
            list.Columns[1].Width = (int)(200 * scale);
            list.Columns[2].Width = (int)(270 * scale);
        }

        if (page == 6)
        {
            list.Columns[0].Width = (int)(140 * scale);
            list.Columns[1].Width = (int)(170 * scale);
            list.Columns[2].Width = (int)(640 * scale);
        }

        LayoutRoadmap();
    }

    void ReceiveFeatures(Reading r)
    {
        // Keep only scalar telemetry in history; process lists belong to the current snapshot.
        history.Add(new Reading() { Time = r.Time, Cpu = r.Cpu, Gpu = r.Gpu, Temp = r.Temp, Watts = r.Watts, MemUsed = r.MemUsed, MemTotal = r.MemTotal, Down = r.Down, Up = r.Up, DiskRead = r.DiskRead, DiskWrite = r.DiskWrite });
        sampleTimes.Add(r.Time);
        if (history.Count > 1800)
        {
            history.RemoveAt(0);
            sampleTimes.RemoveAt(0);
        }

        ReceiveRoadmap(r);
        tray.Text = "Pulse · CPU " + r.Cpu.ToString("0") + "% · GPU " + Val(r.Temp, "°C");
        if (preferences.Alerts)
        {
            CheckThreshold("GPU temperature", r.Temp, preferences.GpuLimit, 5, true, "°C");
            CheckThreshold("Memory usage", r.MemTotal > 0 ? 100 * r.MemUsed / r.MemTotal : double.NaN, preferences.RamLimit, 5, true, "%");
            foreach (var v in r.Volumes)
                CheckThreshold(v.Name + " free space", 100 - v.UsedPercent, preferences.DiskLimit, 2, false, "%");
        }
    }

    void CheckThreshold(string name, double value, double limit, double margin, bool high, string unit)
    {
        Threshold t;
        if (!thresholds.TryGetValue(name, out t))
        {
            t = new Threshold();
            thresholds[name] = t;
        }

        int state = t.Evaluate(value, limit, margin, high);
        if (state == 0)
            return;
        string message = name + " · " + value.ToString("0.#") + unit + (state > 0 ? " crossed " + limit + unit : " recovered");
        events.Insert(0, new[] { DateTime.Now.ToString("HH:mm:ss"), state > 0 ? "ALERT" : "RECOVERED", message });
        if (events.Count > 100)
            events.RemoveAt(100);
        if (state > 0 && snapshot == null)
            tray.ShowBalloonTip(5000, "Pulse threshold alert", message, ToolTipIcon.Warning);
    }

    bool UpdateFeatureList()
    {
        if (page != 5 && page != 6)
            return false;
        int scroll = list.TopItem == null ? 0 : list.TopItem.Index;
        string selected = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Name : null;
        list.BeginUpdate();
        list.Items.Clear();
        if (page == 5)
        {
            PopulateApps(selected);
        }
        else
        {
            list.Columns[0].Text = "Time";
            list.Columns[1].Text = "State";
            list.Columns[2].Text = "Event";
            foreach (var e in events)
                list.Items.Add(new ListViewItem(e));
            if (events.Count == 0)
                list.Items.Add(new ListViewItem(new[] { "—", "No events yet", "Threshold crossings and recoveries appear here." }));
        }

        if (list.Items.Count > 0)
            list.TopItem = list.Items[Math.Min(scroll, list.Items.Count - 1)];
        list.EndUpdate();
        UpdateAppActions();
        return true;
    }

    void StoragePage()
    {
        double total = data.Volumes.Sum(v => v.Total), free = data.Volumes.Sum(v => v.Free);
        Metric(246, "TOTAL CAPACITY", total.ToString("0") + " GB", "Across fixed volumes", double.NaN, mint);
        Metric(496, "SPACE AVAILABLE", free.ToString("0") + " GB", "Ready for what’s next", total > 0 ? free / total * 100 : 0, mint);
        Metric(746, "DISK READ", double.IsNaN(data.DiskRead) ? "—" : data.DiskRead.ToString("0.0"), "MB/s · all physical disks", double.NaN, blue);
        Metric(996, "DISK WRITE", double.IsNaN(data.DiskWrite) ? "—" : data.DiskWrite.ToString("0.0"), "MB/s · all physical disks", double.NaN, amber);
        Box(246, 333, 980, 173, panel);
        T("VOLUME DETAIL", 269, 351, 10, mint, 700, true);
        if (data.Volumes.Count > 0)
        {
            volumeIndex = Math.Min(volumeIndex, data.Volumes.Count - 1);
            var v = data.Volumes[volumeIndex];
            T(v.Name + "  " + (v.Label == "" ? "Local disk" : v.Label), 268, 388, 23, white, 630, true);
            T(v.Format + "  ·  " + v.Free.ToString("0.0") + " GB free of " + v.Total.ToString("0.0") + " GB", 269, 432, 11, muted, 860);
            Bar(270, 474, 930, v.UsedPercent, v.UsedPercent >= 90 ? amber : mint);
            T(v.UsedPercent.ToString("0") + "% used", 1047, 432, 11, white, 160);
        }
        else
            T("No fixed volumes available.", 269, 399, 17, muted);
        if (showDriveHealth)
        {
            PaintDriveHealth();
            return;
        }

        Box(246, 526, 980, 201, panel);
        T("DISK ACTIVITY", 269, 546, 10, white, 300, true);
        T("Read / Write · last 2 minutes", 620, 547, 10, muted, 410);
        double max = Math.Max(1, history.Where(r => (data.Time - r.Time).TotalSeconds <= 120).Select(r => Math.Max(double.IsNaN(r.DiskRead) ? 0 : r.DiskRead, double.IsNaN(r.DiskWrite) ? 0 : r.DiskWrite)).DefaultIfEmpty(1).Max());
        T(max.ToString("0.0") + " MB/s scale", 269, 577, 9, muted, 500);
        HistoryChart(r => r.DiskRead, 270, 611, 930, 81, blue, 120, max);
        HistoryChart(r => r.DiskWrite, 270, 611, 930, 81, amber, 120, max);
        T(data.DiskError != "" ? data.DiskError : "READ", 269, 704, 8, blue, 700);
        T("WRITE", 343, 704, 8, amber);
    }

    void ProcessesPage()
    {
        Box(246, 155, 980, 81, panel);
        T(data.Processes.Count + " PROCESSES", 268, 173, 10, mint, 290, true);
        T("CPU  " + data.Cpu.ToString("0") + "%", 589, 174, 18, white, 260, true);
        T("RAM  " + data.MemUsed.ToString("0.0") + " GB", 940, 174, 18, white, 260, true);
        T("Filter by app name or PID", 248, 239, 8, muted, 600);
        T(appActionStatus, 248, 717, 10, muted, 976);
    }

    void PreferencesPage()
    {
        Box(246, 155, 980, 113, panel);
        T("DESKTOP & REFRESH", 269, 174, 10, mint, 900, true);
        Box(246, 288, 980, 160, panel);
        T("GPU temperature · °C", 269, 358, 10, muted, 275);
        T("Memory usage · %", 579, 358, 10, muted, 275);
        T("Free disk space · %", 891, 358, 10, muted, 280);
        T("EVENT LOG", 247, 472, 10, mint, 590, true);
        T("Two consecutive samples trigger an alert. Recovery rearms it.", 248, 735, 8, muted, 975);
    }

    void HistoryChart(Func<Reading, double> selector, float x, float y, float w, float h, Color color, int seconds, double max)
    {
        using (var pen = new Pen(line))
            for (int i = 0; i < 4; i++)
                g.DrawLine(pen, x, y + h * i / 3, x + w, y + h * i / 3);
        var points = new List<PointF>();
        DateTime prior = DateTime.MinValue;
        DateTime end = history.Count > 0 ? history[history.Count - 1].Time : DateTime.Now;
        foreach (var r in history)
        {
            double age = (end - r.Time).TotalSeconds;
            if (age > seconds)
                continue;
            if (prior != DateTime.MinValue && (r.Time - prior).TotalSeconds > 15)
            {
                DrawHistoryLine(points, color);
                points.Clear();
            }

            prior = r.Time;
            double value = selector(r);
            if (double.IsNaN(value))
            {
                DrawHistoryLine(points, color);
                points.Clear();
                continue;
            }

            points.Add(new PointF(x + w * (float)(1 - age / seconds), y + h - h * (float)Math.Max(0, Math.Min(1, value / max))));
        }

        DrawHistoryLine(points, color);
    }

    void DrawHistoryLine(List<PointF> points, Color color)
    {
        if (points.Count < 2)
            return;
        using (var p = new Pen(color, 2))
            g.DrawLines(p, points.ToArray());
        var end = points[points.Count - 1];
        using (var b = new SolidBrush(color))
            g.FillEllipse(b, end.X - 3, end.Y - 3, 6, 6);
    }

    static string CsvNumber(double n)
    {
        return double.IsNaN(n) ? "" : n.ToString("0.###", CultureInfo.InvariantCulture);
    }

    string HistoryCsv()
    {
        var ids = history.SelectMany(r => r.SensorValues.Keys).Distinct().OrderBy(id => id).ToArray();
        var s = new StringBuilder("timestamp,cpu_percent,gpu_percent,gpu_temp_c,gpu_power_w,ram_used_gib,ram_total_gib,download_mib_s,upload_mib_s,disk_read_mib_s,disk_write_mib_s");
        foreach (var id in ids)
            s.Append(",\"sensor:" + id.Replace("\"", "\"\"") + "\"");
        s.AppendLine();
        foreach (var r in history)
        {
            s.Append(r.Time.ToString("o", CultureInfo.InvariantCulture) + "," + string.Join(",", new[] { r.Cpu, r.Gpu, r.Temp, r.Watts, r.MemUsed, r.MemTotal, r.Down, r.Up, r.DiskRead, r.DiskWrite }.Select(CsvNumber)));
            foreach (var id in ids)
            {
                double value;
                s.Append("," + (r.SensorValues.TryGetValue(id, out value) ? CsvNumber(value) : ""));
            }

            s.AppendLine();
        }

        return s.ToString();
    }

    void ExportHistory()
    {
        using (var d = new SaveFileDialog()
        {
            Filter = "CSV telemetry|*.csv",
            FileName = "Pulse-session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"
        }

        )
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(d.FileName, HistoryCsv());
                    notice = "Exported " + history.Count + " samples.";
                }
                catch (Exception e)
                {
                    notice = e.Message;
                }

                Invalidate();
            }
    }

    void VerifyFeatures()
    {
        var t = new Threshold();
        if (t.Evaluate(81, 80, 5, true) != 0 || t.Evaluate(81, 80, 5, true) != 1 || t.Evaluate(85, 80, 5, true) != 0 || t.Evaluate(74, 80, 5, true) != -1)
            throw new Exception("Temperature hysteresis test failed");
        var low = new Threshold();
        if (low.Evaluate(9, 10, 2, false) != 0 || low.Evaluate(9, 10, 2, false) != 1 || low.Evaluate(11, 10, 2, false) != 0 || low.Evaluate(13, 10, 2, false) != -1)
            throw new Exception("Low disk hysteresis test failed");
        if (Math.Abs(Collector.ProcessPercent(2, 2, 8) - 12.5) > 0.001)
            throw new Exception("Process CPU normalization failed");
        if (history.Count < 3 || data.Processes.Count == 0 || data.Volumes.Count == 0)
            throw new Exception("Extended collection failed");
        if (HistoryCsv().Split('\n').Length < history.Count + 1)
            throw new Exception("History export failed");
        processSearch.Text = "__no_such_process_for_verification__";
        page = 5;
        UpdateList();
        if (list.Items.Count != 0)
            throw new Exception("Process search failed");
        processSearch.Text = "";
        appSort = 2;
        UpdateList();
        if (list.Items.Count == 0)
            throw new Exception("Process sort failed");
        appSort = 1;
        UpdateList();
        // PerformClick requires a visible, selectable control. Navigate explicitly
        // instead of relying on the previous screenshot's Settings layout.
        page = 6;
        LayoutControls();
        topToggle.Checked = true;
        if (!TopMost)
            throw new Exception("Always-on-top failed");
        topToggle.Checked = false;
        if (TopMost)
            throw new Exception("Always-on-top reset failed");
        intervalButton.PerformClick();
        if (timer.Interval != 5000)
            throw new Exception("Refresh interval failed");
        intervalButton.PerformClick();
        if (timer.Interval != 1000)
            throw new Exception("Fast refresh failed");
        intervalButton.PerformClick();
        if (timer.Interval != 2000)
            throw new Exception("Refresh reset failed");
        page = 0;
        LayoutControls();
        rangeButton.PerformClick();
        if (rangeSeconds != 600)
            throw new Exception("History range failed");
        rangeButton.PerformClick();
        if (rangeSeconds != 120)
            throw new Exception("History range reset failed");
        string temp = Path.Combine(Path.GetDirectoryName(snapshot), "preferences-test.ini");
        var p = new Preferences()
        {
            Interval = 5,
            GpuLimit = 85,
            AlwaysOnTop = true
        };
        p.Save(temp);
        var loaded = Preferences.Load(temp);
        if (loaded.Interval != 5 || loaded.GpuLimit != 85 || !loaded.AlwaysOnTop)
            throw new Exception("Preference persistence failed");
        p.Interval = 1;
        p.Save(temp);
        if (Preferences.Load(temp).Interval != 1)
            throw new Exception("Preference replacement failed");
        File.Delete(temp);
    }

    void CaptureSnapshot()
    {
        if (snapshot == null || samplesReceived < 3)
            return;
        try
        {
            if (snapshotStage < 7)
            {
                page = snapshotStage;
                LayoutControls();
                UpdateList();
                using (var bitmap = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(page == 0 ? snapshot : Path.Combine(Path.GetDirectoryName(snapshot), "pulse-view-" + page + ".png"));
                }

                snapshotStage++;
                return;
            }

            if (snapshotStage == 7)
            {
                ClientSize = new Size(1044, 681);
                page = 6;
                LayoutControls();
                UpdateList();
                using (var bitmap = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshot), "pulse-small.png"));
                }

                snapshotStage++;
                return;
            }

            VerifyFeatures();
            VerifyRoadmap();
            VerifyExperience();
            VerifyProcessGroups();
            VerifyAppSorting();
            VerifyTaskMenu();
            VerifyGroupActions();
            VerifyDriveHealth();
            VerifyFanSpeed();
            pause.PerformClick();
            if (!paused)
                throw new Exception("Pause test failed");
            pause.PerformClick();
            if (paused)
                throw new Exception("Resume test failed");
            File.WriteAllText(Path.ChangeExtension(snapshot, ".txt"), Report() + "\r\nVerification: seven views and compact layout rendered; live process/disk readings; threshold alert and recovery; CPU normalization; process search/sort; preference persistence; CSV export; pause/resume passed.\r\n");
            File.WriteAllText(Path.ChangeExtension(snapshot, ".csv"), HistoryCsv());
            Close();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.ChangeExtension(snapshot, ".error.txt"), ex.ToString());
            Environment.ExitCode = 1;
            Close();
        }
    }
}
