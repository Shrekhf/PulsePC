using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

partial class Reading
{
    public double Cpu, MemUsed, MemTotal, Down, Up;
    public double Gpu = double.NaN, Temp = double.NaN, Watts = double.NaN, Fan = double.NaN, Clock = double.NaN, Vram = double.NaN, VramTotal = double.NaN;
    public string GpuName = "Detecting graphics", Power = "Reading…", Error = "";
    public DateTime Time;
    public List<string[]> Sensors = new List<string[]>();
}

partial class Collector
{
    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx([In, Out] Memory m);
    [StructLayout(LayoutKind.Sequential)]
    class Memory
    {
        public uint length = 64, load;
        public ulong total, avail, page, availPage, virt, availVirt, extended;
    }

    long pi, pk, pu, rx, tx;
    DateTime last = DateTime.UtcNow;
    bool primed;
    public List<string[]> Inventory = new List<string[]>();
    public List<string[]> Plans = new List<string[]>();
    public string CpuName = "Processor", Board = "Motherboard", OS = "Windows", CpuDetail = "";
    public static string Run(string exe, string args)
    {
        using (var p = new Process())
        {
            p.StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            p.Start();
            var output = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(5000))
            {
                try
                {
                    p.Kill();
                }
                catch
                {
                }

                throw new Exception("Hardware query timed out");
            }

            if (!Task.WaitAll(new Task[] { output, err }, 1000))
                throw new TimeoutException("Hardware query output timed out");
            if (p.ExitCode != 0)
                throw new Exception(err.Result.Trim());
            return output.Result;
        }
    }

    public static List<Dictionary<string, string>> Query(string scope, string query)
    {
        var list = new List<Dictionary<string, string>>();
        using (var s = new ManagementObjectSearcher(scope, query))
        {
            s.Options.Timeout = TimeSpan.FromSeconds(3);
            using (var results = s.Get())
            {
                foreach (ManagementObject o in results)
                {
                    using (o)
                    {
                        var d = new Dictionary<string, string>();
                        foreach (PropertyData p in o.Properties)
                            d[p.Name] = Convert.ToString(p.Value, CultureInfo.InvariantCulture);
                        list.Add(d);
                    }
                }
            }
        }

        return list;
    }

    void Add(string kind, string name, string detail)
    {
        Inventory.Add(new[] { kind, name, detail });
    }

    public void Init()
    {
        try
        {
            foreach (var x in Query("root\\cimv2", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))
            {
                CpuName = x["Name"].Trim();
                CpuDetail = x["NumberOfCores"] + " cores / " + x["NumberOfLogicalProcessors"] + " threads";
                Add("PROCESSOR", CpuName, CpuDetail);
            }
        }
        catch
        {
        }

        try
        {
            foreach (var x in Query("root\\cimv2", "SELECT Manufacturer,Product FROM Win32_BaseBoard"))
            {
                Board = x["Product"];
                Add("MOTHERBOARD", x["Manufacturer"] + " " + Board, "Baseboard");
            }
        }
        catch
        {
        }

        try
        {
            foreach (var x in Query("root\\cimv2", "SELECT Manufacturer,Capacity,ConfiguredClockSpeed,DeviceLocator FROM Win32_PhysicalMemory"))
                Add("MEMORY", x["Manufacturer"] + " · " + (double.Parse(x["Capacity"]) / 1073741824).ToString("0") + " GB", x["ConfiguredClockSpeed"] + " MT/s · " + x["DeviceLocator"]);
        }
        catch
        {
        }

        try
        {
            foreach (var x in Query("root\\cimv2", "SELECT Name,DriverVersion FROM Win32_VideoController"))
                Add("GRAPHICS", x["Name"], "Driver " + x["DriverVersion"]);
        }
        catch
        {
        }

        try
        {
            foreach (var x in Query("root\\cimv2", "SELECT Model,Size,InterfaceType FROM Win32_DiskDrive"))
                Add("STORAGE", x["Model"], (Num(x["Size"]) / 1e9).ToString("0") + " GB · " + x["InterfaceType"]);
        }
        catch
        {
        }

        try
        {
            foreach (var x in Query("root\\cimv2", "SELECT Caption,Version FROM Win32_OperatingSystem"))
            {
                OS = x["Caption"];
                Add("SYSTEM", OS, "Version " + x["Version"]);
            }
        }
        catch
        {
        }

        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            Add("NETWORK", n.Description, n.OperationalStatus.ToString());
        try
        {
            foreach (Match m in Regex.Matches(Run("powercfg.exe", "/list"), @"([a-fA-F0-9-]{36})\s+\((.+)\)"))
                Plans.Add(new[] { m.Groups[1].Value, m.Groups[2].Value });
        }
        catch
        {
        }
    }

    static double Num(string s)
    {
        double v;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
    }

    // Missing WMI providers are retried once per minute instead of every sample.
    readonly Dictionary<string, DateTime> providerRetry = new Dictionary<string, DateTime>();
    public Reading Read()
    {
        var r = new Reading();
        long i, k, u;
        if (GetSystemTimes(out i, out k, out u))
        {
            long total = (k - pk) + (u - pu);
            r.Cpu = primed && total > 0 ? Math.Max(0, Math.Min(100, 100.0 * (total - (i - pi)) / total)) : 0;
            pi = i;
            pk = k;
            pu = u;
        }

        var m = new Memory();
        if (GlobalMemoryStatusEx(m))
        {
            r.MemTotal = m.total / 1073741824.0;
            r.MemUsed = (m.total - m.avail) / 1073741824.0;
        }

        long nr = 0, nt = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try
            {
                var s = n.GetIPv4Statistics();
                nr += s.BytesReceived;
                nt += s.BytesSent;
            }
            catch
            {
            }
        }

        double seconds = (DateTime.UtcNow - last).TotalSeconds;
        if (primed && seconds > 0)
        {
            r.Down = Math.Max(0, nr - rx) / seconds / 1048576;
            r.Up = Math.Max(0, nt - tx) / seconds / 1048576;
        }

        rx = nr;
        tx = nt;
        last = DateTime.UtcNow;
        primed = true;
        try
        {
            string line = Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"), "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw,fan.speed,clocks.gr --format=csv,noheader,nounits").Trim().Split('\n')[0];
            var v = line.Split(',').Select(s => s.Trim()).ToArray();
            if (v.Length >= 8)
            {
                r.GpuName = v[0];
                r.Temp = Num(v[1]);
                r.Gpu = Num(v[2]);
                r.Vram = Num(v[3]) / 1024;
                r.VramTotal = Num(v[4]) / 1024;
                r.Watts = Num(v[5]);
                r.Fan = Num(v[6]);
                r.Clock = Num(v[7]);
            }
        }
        catch
        {
            r.GpuName = "GPU telemetry unavailable";
            r.Error = "NVIDIA sensor query unavailable";
        }

        try
        {
            string p = Run("powercfg.exe", "/getactivescheme");
            var match = Regex.Match(p, @"\((.+)\)");
            r.Power = match.Success ? match.Groups[1].Value : p.Trim();
        }
        catch
        {
            r.Power = "Unavailable";
        }

        foreach (string provider in new[]
        {
            "LibreHardwareMonitor",
            "OpenHardwareMonitor"
        }

        )
        {
            DateTime retry;
            if (providerRetry.TryGetValue(provider, out retry) && DateTime.UtcNow < retry)
                continue;
            try
            {
                foreach (var s in Query("root\\" + provider, "SELECT Name,SensorType,Value,Identifier FROM Sensor"))
                {
                    string unit = s["SensorType"] == "Temperature" ? " °C" : s["SensorType"] == "Fan" ? " RPM" : s["SensorType"] == "Voltage" ? " V" : s["SensorType"] == "Power" ? " W" : "";
                    if (unit != "")
                        r.Sensors.Add(new[] { s["Name"], s["Value"] + unit, provider + " · " + s["Identifier"] });
                }

                providerRetry.Remove(provider);
            }
            catch
            {
                providerRetry[provider] = DateTime.UtcNow.AddMinutes(1);
            }
        }

        CollectExtras(r);
        CollectRoadmap(r);
        r.Time = DateTime.Now;
        return r;
    }
}

partial class Dashboard : Form
{
    public static bool LifecycleTest;
    public static string DiagnosticsPath;
    Color bg = Color.FromArgb(13, 17, 23), panel = Color.FromArgb(21, 27, 35), line = Color.FromArgb(39, 48, 59), muted = Color.FromArgb(142, 157, 176), white = Color.FromArgb(237, 243, 249), mint = Color.FromArgb(123, 241, 198), blue = Color.FromArgb(119, 172, 255), amber = Color.FromArgb(248, 196, 112);
    Collector collector = new Collector();
    Reading data = new Reading();
    List<float> cpu = new List<float>(), gpu = new List<float>(), mem = new List<float>();
    Task initialization;
    Timer timer = new Timer();
    bool busy, paused, ready;
    int page;
    float scale = 1;
    string notice = "";
    Button pause;
    Button plans;
    int selectedPlan = -1;
    Panel listHost;
    ListView list;
    string snapshot;
    public Dashboard(string shot)
    {
        snapshot = shot;
        Text = "Pulse — PC Dashboard";
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1060, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = bg;
        ForeColor = white;
        Font = new Font("Segoe UI", 10);
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        for (int j = 0; j < 7; j++)
        {
            int p = j;
            var b = Button(pageNames[j], () =>
            {
                page = p;
                notice = "";
                LayoutControls();
                UpdateList();
                Invalidate();
            });
            b.Tag = "nav" + j;
        }

        pause = Button("Pause live", () =>
        {
            paused = !paused;
            pause.Text = paused ? "Resume live" : "Pause live";
            Invalidate();
        });
        pause.Tag = "pause";
        var export = Button("Export ▾", ShowExportMenu);
        export.Tag = "export";
        plans = Button("Choose power plan", () =>
        {
            var menu = new ContextMenuStrip()
            {
                BackColor = panel,
                ForeColor = white
            };
            for (int index = 0; index < collector.Plans.Count; index++)
            {
                int choice = index;
                menu.Items.Add(collector.Plans[index][1], null, (s, e) =>
                {
                    selectedPlan = choice;
                    plans.Text = collector.Plans[choice][1] + "   ▾";
                });
            }

            DisposeMenuAfterClick(menu);
            menu.Show(plans, new Point(0, plans.Height));
        });
        var apply = Button("Apply power plan", ApplyPower);
        apply.Tag = "apply";
        string[] labels =
        {
            "Task Manager",
            "Display settings",
            "Sound settings",
            "Network settings",
            "Device Manager",
            "Power settings"
        };
        string[] commands =
        {
            "taskmgr.exe",
            "ms-settings:display",
            "ms-settings:sound",
            "ms-settings:network",
            "devmgmt.msc",
            "ms-settings:powersleep"
        };
        for (int j = 0; j < labels.Length; j++)
        {
            string command = commands[j];
            var b = Button(labels[j], () => Launch(command));
            b.Tag = "action" + j;
        }

        listHost = new Panel()
        {
            BackColor = panel
        };
        list = new ListView()
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BackColor = panel,
            ForeColor = white,
            BorderStyle = BorderStyle.None,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false
        };
        list.OwnerDraw = true;
        list.DrawColumnHeader += (s, e) =>
        {
            using (var brush = new SolidBrush(line))
                e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, e.Bounds, muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        list.DrawItem += (s, e) =>
        {
            e.DrawDefault = true;
        };
        list.DrawSubItem += (s, e) =>
        {
            e.DrawDefault = true;
        };
        list.Columns.Add("Component", 150);
        list.Columns.Add("Name / reading", 440);
        list.Columns.Add("Details / source", 420);
        listHost.Controls.Add(list);
        Controls.Add(listHost);
        Resize += (s, e) =>
        {
            LayoutControls();
            Invalidate();
        };
        Shown += async (s, e) =>
        {
            initialization = Task.Run(() => collector.Init());
            try
            {
                await initialization;
            }
            catch (Exception ex)
            {
                notice = "Hardware initialization: " + ex.Message;
            }

            if (IsDisposed || closingSave)
                return;
            ready = true;
            if (collector.Plans.Count > 0)
            {
                selectedPlan = 0;
                plans.Text = collector.Plans[0][1] + "   ▾";
            }

            await RefreshData();
            if (!IsDisposed && !closingSave)
                timer.Start();
        };
        timer.Interval = 2000;
        timer.Tick += async (s, e) =>
        {
            if (!paused)
                await RefreshData();
        };
        FormClosed += (s, e) => timer.Dispose();
        InitFeatures();
        InitExperience();
        LayoutControls();
    }

    Button Button(string text, Action action)
    {
        var b = new PulseButton()
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = panel,
            ForeColor = white,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = line;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 58, 64);
        b.Click += (s, e) => action();
        Controls.Add(b);
        return b;
    }

    void LayoutControls()
    {
        scale = Math.Min(ClientSize.Width / 1280f, ClientSize.Height / 820f);
        foreach (Control c in Controls)
        {
            string t = c.Tag as string;
            if (t == null)
                continue;
            c.Visible = true;
            if (t.StartsWith("nav"))
            {
                int n = int.Parse(t.Substring(3));
                Place(c, 22, 159 + n * 55, 165, 42);
                c.BackColor = page == n ? Color.FromArgb(34, 64, 58) : bg;
                c.ForeColor = page == n ? mint : muted;
            }
            else if (t == "pause")
                Place(c, 1058, 42, 174, 35);
            else if (t == "export")
                Place(c, 22, 704, 165, 37);
            else if (t == "apply")
            {
                c.Visible = page == 3;
                Place(c, 953, 205, 245, 42);
            }
            else if (t.StartsWith("action"))
            {
                int n = int.Parse(t.Substring(6));
                c.Visible = page == 3;
                Place(c, 260 + (n % 3) * 315, 362 + (n / 3) * 73, 285, 48);
            }
        }

        plans.Visible = page == 3;
        Place(plans, 260, 208, 663, 38);
        listHost.Visible = page == 1 || page == 2;
        Place(listHost, 246, page == 1 ? 165 : 310, 980, page == 1 ? 560 : 390);
        for (int i = 0; i < 3; i++)
            list.Columns[i].Width = (int)(new[]
            {
                145,
                400,
                400
            }[i] * scale);
        LayoutFeatures();
    }

    void Place(Control c, int x, int y, int w, int h)
    {
        c.SetBounds((int)(x * scale), (int)(y * scale), (int)(w * scale), (int)(h * scale));
    }

    async Task RefreshData()
    {
        if (busy || closingSave || IsDisposed)
            return;
        busy = true;
        try
        {
            var r = await Task.Run(() => collector.Read());
            if (IsDisposed)
                return;
            data = r;
            if (cpu.Count == 0)
            {
                int active = collector.Plans.FindIndex(p => p[1] == r.Power);
                if (active >= 0)
                {
                    selectedPlan = active;
                    plans.Text = collector.Plans[active][1] + "   ▾";
                }
            }

            ReceiveFeatures(r);
            Add(cpu, r.Cpu);
            Add(gpu, r.Gpu);
            Add(mem, r.MemTotal > 0 ? r.MemUsed / r.MemTotal * 100 : 0);
            UpdateList();
            Invalidate();
            CaptureSnapshot();
            if (DiagnosticsPath != null && samplesReceived >= 3)
            {
                File.WriteAllText(DiagnosticsPath, Report());
                using (var bitmap = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(Path.ChangeExtension(DiagnosticsPath, ".png"));
                }

                DiagnosticsPath = null;
            }

            if (LifecycleTest && samplesReceived >= 3 && !closingSave)
                Close();
        }
        catch (Exception ex)
        {
            notice = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    void Add(List<float> values, double v)
    {
        values.Add((float)v);
        if (values.Count > 1800)
            values.RemoveAt(0);
    }

    void UpdateList()
    {
        if (UpdateRoadmapList())
            return;
        if (UpdateFeatureList())
            return;
        if (page != 1 && page != 2)
            return;
        list.Columns[0].Text = "Component";
        list.Columns[1].Text = "Name / reading";
        list.Columns[2].Text = "Details / source";
        list.BeginUpdate();
        list.Items.Clear();
        if (page == 1)
        {
            foreach (var r in collector.Inventory)
                list.Items.Add(new ListViewItem(r));
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                list.Items.Add(new ListViewItem(new[] { "VOLUME", d.Name + " " + d.VolumeLabel, (d.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " GB free / " + (d.TotalSize / 1073741824.0).ToString("0.0") + " GB" }));
        }
        else
        {
            list.Items.Add(new ListViewItem(new[] { "GPU temperature", Val(data.Temp, " °C"), "NVIDIA driver / nvidia-smi" }));
            list.Items.Add(new ListViewItem(new[] { "GPU fan", Val(data.Fan, " %"), "NVIDIA · reported fan speed" }));
            list.Items.Add(new ListViewItem(new[] { "GPU power", Val(data.Watts, " W"), "NVIDIA · board power draw" }));
            foreach (var r in data.Sensors)
                list.Items.Add(new ListViewItem(r));
        }

        list.EndUpdate();
    }

    static string Val(double d, string suffix)
    {
        return double.IsNaN(d) ? "Unavailable" : d.ToString("0.#") + suffix;
    }

    void Launch(string command)
    {
        try
        {
            Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    async void ApplyPower()
    {
        if (selectedPlan < 0)
            return;
        string id = collector.Plans[selectedPlan][0];
        try
        {
            await Task.Run(() => Collector.Run("powercfg.exe", "/setactive " + id));
            notice = "Power plan applied.";
            await RefreshData();
        }
        catch (Exception e)
        {
            notice = "Could not apply power plan: " + e.Message;
        }

        Invalidate();
    }

    string Report()
    {
        var s = new StringBuilder("PULSE PC · Hardware report\r\n" + DateTime.Now + "\r\n\r\n");
        foreach (var r in collector.Inventory)
            s.AppendLine(string.Join(" | ", r));
        s.AppendLine("\r\nCPU usage: " + Val(data.Cpu, " %"));
        s.AppendLine("RAM: " + Val(data.MemUsed, " GB") + " / " + Val(data.MemTotal, " GB"));
        s.AppendLine("GPU: " + data.GpuName + " | " + Val(data.Temp, " °C") + " | " + Val(data.Gpu, " %") + " | " + Val(data.Watts, " W"));
        s.AppendLine("Power plan: " + data.Power);
        s.AppendLine("Uptime: " + data.Uptime);
        foreach (var v in data.Volumes)
            s.AppendLine("Volume " + v.Name + " | " + v.Free.ToString("0.0") + " GB free / " + v.Total.ToString("0.0") + " GB");
        s.AppendLine("Disk read/write: " + Val(data.DiskRead, " MB/s") + " / " + Val(data.DiskWrite, " MB/s"));
        s.AppendLine("Observed processes: " + data.Processes.Count);
        s.AppendLine(data.SensorStatus);
        foreach (var d in data.Detail)
            s.AppendLine(d.Hardware + " | " + d.Name + " | " + Val(d.Value, " " + d.Unit));
        foreach (var h in data.DiskHealth)
            s.AppendLine(string.Join(" | ", h));
        foreach (var r in data.Sensors)
            s.AppendLine(string.Join(" | ", r));
        s.AppendLine("\r\nCPU/motherboard sensors require compatible hardware, PawnIO, and administrator access, or a working external WMI sensor provider. Unavailable values are not estimated.");
        return s.ToString();
    }

    void Export()
    {
        using (var d = new SaveFileDialog()
        {
            Filter = "Text report|*.txt",
            FileName = "Pulse-PC-report.txt"
        }

        )
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(d.FileName, Report());
                    notice = "Report saved.";
                }
                catch (Exception e)
                {
                    notice = e.Message;
                }

                Invalidate();
            }
    }

    Graphics g;
    void T(string s, float x, float y, float size, Color color, float width = 1000, bool bold = false)
    {
        using (var f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular))
        using (var b = new SolidBrush(color))
        using (var fmt = new StringFormat()
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        }

        )
            g.DrawString(s, f, b, new RectangleF(x, y, width, size * 2 + 8), fmt);
    }

    void Box(float x, float y, float w, float h, Color fill)
    {
        using (var p = Round(new RectangleF(x, y, w, h), 14))
        using (var b = new SolidBrush(fill))
        using (var pen = new Pen(line))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
    }

    static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void Bar(float x, float y, float w, double value, Color color)
    {
        using (var b = new SolidBrush(line))
            g.FillRectangle(b, x, y, w, 4);
        if (!double.IsNaN(value))
            using (var b = new SolidBrush(color))
                g.FillRectangle(b, x, y, w * (float)Math.Max(0, Math.Min(1, value / 100)), 4);
    }

    void Chart(List<float> values, float x, float y, float w, float h, Color color)
    {
        using (var p = new Pen(line))
        {
            for (int i = 0; i < 4; i++)
                g.DrawLine(p, x, y + h * i / 3, x + w, y + h * i / 3);
        }

        if (values.Count < 2)
            return;
        var points = new List<PointF>();
        for (int i = 0; i < values.Count; i++)
        {
            if (float.IsNaN(values[i]))
            {
                if (points.Count > 1)
                    using (var p = new Pen(color, 2))
                        g.DrawLines(p, points.ToArray());
                points.Clear();
                continue;
            }

            points.Add(new PointF(x + w - w * (values.Count - 1 - i) / 59f, y + h - h * Math.Max(0, Math.Min(100, values[i])) / 100));
        }

        if (points.Count > 1)
        {
            using (var p = new Pen(color, 2))
                g.DrawLines(p, points.ToArray());
            var end = points[points.Count - 1];
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, end.X - 3, end.Y - 3, 6, 6);
        }
    }

    void Metric(float x, string label, string value, string detail, double percent, Color color)
    {
        if (data.Time == default(DateTime))
        {
            value = "—";
            detail = "Connecting to your PC…";
        }

        Box(x, 155, 232, 158, panel);
        T(label, x + 20, 174, 10, muted, 195, true);
        T(value, x + 17, 199, 31, white, 200, true);
        T(detail, x + 20, 257, 10, muted, 195);
        Bar(x + 20, 291, 192, percent, color);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        g = e.Graphics;
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var b = new SolidBrush(Color.FromArgb(16, 22, 29)))
            g.FillRectangle(b, 0, 0, 208, 820);
        using (var p = new Pen(line))
            g.DrawLine(p, 208, 0, 208, 820);
        T("◈", 23, 30, 29, mint, 50, true);
        T("pulse", 67, 32, 26, white, 120, true);
        T("YOUR PC, IN FOCUS", 25, 86, 8, muted, 175, true);
        T("MONITOR", 25, 130, 8, muted, 170, true);
        T("LOCAL / PRIVATE", 25, 581, 8, mint, 170, true);
        T(data.Uptime.Days + "d " + data.Uptime.Hours + "h " + data.Uptime.Minutes + "m uptime", 25, 604, 10, muted, 170);
        T("PULSE  1.4", 25, 765, 8, muted, 170, true);
        string title = pageTitles[page];
        T(title, 244, 37, 27, white, 790, true);
        T(pageDescriptions[page], 246, 91, 11, muted, 820);
        if (page == 0)
            Overview();
        else if (page == 1)
        {
            T("HARDWARE INVENTORY", 246, 136, 9, mint, 600, true);
        }
        else if (page == 2)
            FocusedThermals();
        else if (page == 3)
            ControlsPage();
        else if (page == 4)
            StoragePage();
        else if (page == 5)
            ProcessesPage();
        else
            PreferencesPage();
        using (var p = new Pen(line))
            g.DrawLine(p, 244, 755, 1232, 755);
        bool stale = data.Time != default(DateTime) && (DateTime.Now - data.Time).TotalSeconds > 12;
        T(!ready ? "●  CONNECTING" : paused ? "●  PAUSED" : stale ? "●  DATA STALE" : "●  LIVE · " + preferences.Interval + " SECOND REFRESH", 246, 773, 8, paused || stale ? amber : mint, 350, true);
        T(notice != "" ? notice : data.Time == default(DateTime) ? "Discovering hardware…" : "Updated " + data.Time.ToString("h:mm:ss tt") + "   ·   " + collector.OS, 590, 772, 9, muted, 640);
    }

    void Overview()
    {
        DrawCards();
        Box(246, 333, 650, 230, panel);
        T("PERFORMANCE", 266, 352, 10, white, 300, true);
        T("CPU", 269, 387, 9, mint);
        T("GPU", 334, 387, 9, blue);
        T("RAM", 399, 387, 9, amber);
        HistoryChart(r => r.Cpu, 270, 423, 600, 104, mint, rangeSeconds, 100);
        HistoryChart(r => r.Gpu, 270, 423, 600, 104, blue, rangeSeconds, 100);
        HistoryChart(r => r.MemTotal > 0 ? 100 * r.MemUsed / r.MemTotal : double.NaN, 270, 423, 600, 104, amber, rangeSeconds, 100);
        T("−" + (rangeSeconds / 60) + " min", 270, 535, 8, muted);
        T("NOW", 836, 535, 8, muted);
        Box(916, 333, 312, 230, panel);
        T("GRAPHICS", 936, 352, 10, white, 270, true);
        T(data.GpuName.Replace("NVIDIA GeForce ", ""), 936, 384, 17, white, 271, true);
        T("Utilization", 936, 426, 10, muted, 160);
        T(Val(data.Gpu, " %"), 1120, 425, 11, mint, 95, true);
        T("VRAM", 936, 461, 10, muted, 120);
        T(Val(data.Vram, " GB"), 1100, 460, 11, white, 113);
        T("Core clock", 936, 496, 10, muted, 160);
        T(Val(data.Clock, " MHz"), 1090, 495, 11, white, 128);
        OverviewThermals();
        Box(916, 583, 312, 145, panel);
        T("NETWORK TRAFFIC", 936, 600, 9, white, 270, true);
        T("↓ " + data.Down.ToString("0.00") + " MB/s", 936, 628, 22, mint, 270, true);
        T("↑ " + data.Up.ToString("0.00") + " MB/s upload", 936, 680, 11, muted, 275);
    }

    string CpuTemp()
    {
        var s = data.Detail.FirstOrDefault(d => d.Type == "Temperature" && d.Id.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0);
        return s == null ? "Unavailable · open Sensor setup" : Val(s.Value, " °C");
    }

    void Sensors()
    {
        Box(246, 155, 980, 130, panel);
        T("CPU / MOTHERBOARD / CASE FANS", 268, 175, 9, mint, 920, true);
        T(data.Sensors.Count == 0 ? "Connect a sensor provider to see more." : data.Sensors.Count + " additional sensors connected.", 268, 200, 18, white, 925, true);
        T("Run LibreHardwareMonitor or OpenHardwareMonitor with WMI enabled, then keep it open.", 268, 240, 11, muted, 928);
        T("GPU fan speed is reported as a percentage. Unavailable sensors are never estimated.", 248, 720, 10, muted, 970);
    }

    void ControlsPage()
    {
        Box(246, 155, 980, 127, panel);
        T("POWER PROFILE", 266, 174, 9, mint, 925, true);
        T("WINDOWS SHORTCUTS", 246, 321, 10, white, 950, true);
        Box(246, 535, 980, 192, panel);
        T("DEVICE SOFTWARE", 269, 554, 9, mint, 928, true);
        T("Your ASUS controls", 269, 579, 19, white, 925, true);
        T("Open Armoury Crate for cooling and lighting settings.", 269, 628, 11, muted, 925);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();
    [STAThread]
    static void Main(string[] args)
    {
        LifecycleTest = args.Contains("--lifecycle");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string shot = args.Length == 2 && args[0] == "--snapshot" ? args[1] : null;
        if (args.Length >= 2 && args[0] == "--diagnostics")
            DiagnosticsPath = args[1];
        var dashboard = new Dashboard(shot);
        if (args.Contains("--storage"))
            dashboard.Shown += (sender, e) => dashboard.Navigate(4);
        Application.Run(dashboard);
    }
}
