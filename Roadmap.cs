using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

class SensorReading
{
    public string Id, Name, Hardware, HardwareId, Type, Unit;
    public double Value;
}

class SensorRule
{
    public string Id, Name, Unit;
    public double Limit;
    public bool High = true;
}

class SavedSample
{
    public string Time;
    public Dictionary<string, double?> Values;
}

partial class Reading
{
    public List<SensorReading> Detail = new List<SensorReading>();
    public Dictionary<string, double> SensorValues = new Dictionary<string, double>();
    public string SensorStatus = "Connecting sensor library…";
    public List<string[]> DiskHealth = new List<string[]>();
    public List<double> Cores = new List<double>();
}

partial class Collector
{
    object computer;
    bool sensorAttempted, lowLevelReady;
    string sensorFailure = "";
    DateTime healthNext = DateTime.MinValue;
    List<string[]> health = new List<string[]>();
    List<PerformanceCounter> coreCounters = new List<PerformanceCounter>();
    // Collector access is serialized; cache reflection metadata across sensor samples.
    static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> properties = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
    static readonly Dictionary<Type, Dictionary<string, MethodInfo>> methods = new Dictionary<Type, Dictionary<string, MethodInfo>>();
    static object Prop(object obj, string name)
    {
        var type = obj.GetType();
        Dictionary<string, PropertyInfo> members;
        if (!properties.TryGetValue(type, out members))
        {
            members = new Dictionary<string, PropertyInfo>();
            properties[type] = members;
        }

        PropertyInfo property;
        if (!members.TryGetValue(name, out property))
        {
            property = type.GetProperty(name);
            members[name] = property;
        }

        return property.GetValue(obj, null);
    }

    static void Set(object obj, string name, object value)
    {
        var p = obj.GetType().GetProperty(name);
        if (p != null)
            p.SetValue(obj, value, null);
    }

    static void Call(object obj, string method)
    {
        var type = obj.GetType();
        Dictionary<string, MethodInfo> members;
        if (!methods.TryGetValue(type, out members))
        {
            members = new Dictionary<string, MethodInfo>();
            methods[type] = members;
        }

        MethodInfo info;
        if (!members.TryGetValue(method, out info))
        {
            info = type.GetMethod(method);
            members[method] = info;
        }

        info.Invoke(obj, null);
    }

    public static bool Elevated()
    {
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }

    void CollectRoadmap(Reading r)
    {
        if (!sensorAttempted)
        {
            sensorAttempted = true;
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sensors");
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    string file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                };
                var asm = Assembly.LoadFrom(Path.Combine(dir, "LibreHardwareMonitorLib.dll"));
                computer = Activator.CreateInstance(asm.GetType("LibreHardwareMonitor.Hardware.Computer"));
                lowLevelReady = Elevated() && (bool)asm.GetType("LibreHardwareMonitor.PawnIo.PawnIo").GetProperty("IsInstalled").GetValue(null, null);
                Set(computer, "IsCpuEnabled", lowLevelReady);
                Set(computer, "IsMotherboardEnabled", lowLevelReady);
                Set(computer, "IsStorageEnabled", true);
                Set(computer, "IsMemoryEnabled", true);
                Set(computer, "IsGpuEnabled", false);
                Set(computer, "IsControllerEnabled", false);
                Call(computer, "Open");
            }
            catch (Exception ex)
            {
                sensorFailure = ex.GetBaseException().Message;
                computer = null;
            }

            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                try
                {
                    var p = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
                    p.NextValue();
                    coreCounters.Add(p);
                }
                catch
                {
                    break;
                }
            }
        }

        if (computer != null)
        {
            try
            {
                foreach (var h in (IEnumerable)Prop(computer, "Hardware"))
                    ReadHardware(h, r);
                r.SensorStatus = "Hardware connected · " + r.Detail.Count + " readings" + (lowLevelReady ? "" : " · limited access");
            }
            catch (Exception ex)
            {
                r.SensorStatus = "Sensor provider: " + ex.GetBaseException().Message;
            }
        }
        else
            r.SensorStatus = "Sensor library unavailable: " + sensorFailure;
        foreach (var p in coreCounters)
        {
            try
            {
                r.Cores.Add(Math.Max(0, Math.Min(100, p.NextValue())));
            }
            catch
            {
                r.Cores.Add(double.NaN);
            }
        }

        r.Detail.Add(new SensorReading() { Id = "nvidia/temperature", Name = "GPU core", Hardware = r.GpuName, Type = "Temperature", Unit = "°C", Value = r.Temp });
        r.Detail.Add(new SensorReading() { Id = "nvidia/fan", Name = "GPU fan", Hardware = r.GpuName, Type = "Control", Unit = "%", Value = r.Fan });
        r.Detail.Add(new SensorReading() { Id = "nvidia/power", Name = "GPU board power", Hardware = r.GpuName, Type = "Power", Unit = "W", Value = r.Watts });
        // Also accept external WMI providers already supported by Pulse.
        foreach (var s in r.Sensors)
        {
            string[] parts = s[2].Split(new[] { " · " }, StringSplitOptions.None);
            if (parts.Length < 2)
                continue;
            string type = s[1].Contains("°C") ? "Temperature" : s[1].Contains("RPM") ? "Fan" : "Other";
            string numeric = s[1].Split(' ')[0];
            double value;
            if (double.TryParse(numeric, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value) && !r.Detail.Any(d => d.Id == parts[1]))
                r.Detail.Add(new SensorReading() { Id = parts[1], Name = s[0], Hardware = parts[0], Type = type, Unit = s[1].Substring(numeric.Length).Trim(), Value = value });
        }

        foreach (var d in r.Detail)
            if (!double.IsNaN(d.Value) && !double.IsInfinity(d.Value))
                r.SensorValues[d.Id] = d.Value;
        if (DateTime.UtcNow >= healthNext)
        {
            healthNext = DateTime.UtcNow.AddMinutes(1);
            health = new List<string[]>();
            try
            {
                foreach (var d in Query("root\\Microsoft\\Windows\\Storage", "SELECT FriendlyName,HealthStatus FROM MSFT_PhysicalDisk"))
                {
                    string state = d["HealthStatus"] == "0" ? "Healthy" : d["HealthStatus"] == "1" ? "Warning" : d["HealthStatus"] == "2" ? "Unhealthy" : "Unknown";
                    health.Add(new[] { d["FriendlyName"], state, "Windows storage health" });
                }
            }
            catch
            {
                health.Add(new[] { "Storage health", "Unavailable", "Windows storage provider did not return health status" });
            }
        }

        r.DiskHealth = health;
    }

    void ReadHardware(object hardware, Reading r)
    {
        try
        {
            Call(hardware, "Update");
            string name = Convert.ToString(Prop(hardware, "Name"));
            foreach (var sensor in (IEnumerable)Prop(hardware, "Sensors"))
            {
                object value = Prop(sensor, "Value");
                if (value == null)
                    continue;
                double number = Convert.ToDouble(value);
                if (double.IsNaN(number) || double.IsInfinity(number))
                    continue;
                string type = Convert.ToString(Prop(sensor, "SensorType"));
                if (type == "Temperature" && number == 0)
                    continue;
                string unit = type == "Temperature" ? "°C" : type == "Fan" ? "RPM" : type == "Voltage" ? "V" : type == "Power" ? "W" : type == "Load" || type == "Control" || type == "Level" ? "%" : type == "Clock" ? "MHz" : "";
                if (unit == "")
                    continue;
                r.Detail.Add(new SensorReading() { Id = Convert.ToString(Prop(sensor, "Identifier")), Name = Convert.ToString(Prop(sensor, "Name")), Hardware = name, HardwareId = Convert.ToString(Prop(hardware, "Identifier")), Type = type, Unit = unit, Value = number });
            }

            foreach (var sub in (IEnumerable)Prop(hardware, "SubHardware"))
                ReadHardware(sub, r);
        }
        catch
        {
        }
    }

    public void CloseSensors()
    {
        if (computer != null)
            try
            {
                Call(computer, "Close");
            }
            catch
            {
            }

        foreach (var p in coreCounters)
            p.Dispose();
        if (diskRead != null)
            diskRead.Dispose();
        if (diskWrite != null)
            diskWrite.Dispose();
    }
}

partial class Dashboard
{
    string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    List<SensorRule> rules = new List<SensorRule>();
    Button sensorSetup, sensorAction, desktopOptions, vendorCooling, vendorRgb, coreView;
    DateTime lastHistorySave = DateTime.MinValue;
    Task historySave;
    bool closingSave;
    int samplesReceived;
    JavaScriptSerializer Json()
    {
        return new JavaScriptSerializer()
        {
            MaxJsonLength = 32 * 1024 * 1024
        };
    }

    void InitRoadmap()
    {
        if (snapshot == null)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                LoadHistory(Path.Combine(dataDir, "history.json"));
                string path = Path.Combine(dataDir, "sensor-alerts.json");
                if (File.Exists(path))
                    rules = Json().Deserialize<List<SensorRule>>(File.ReadAllText(path)) ?? new List<SensorRule>();
            }
            catch (Exception ex)
            {
                notice = "Saved data: " + ex.Message;
            }
        }

        sensorSetup = Button("Sensor status", SensorSetup);
        sensorSetup.Tag = "sensorSetup";
        sensorAction = Button("Selected sensor · trend / alert", OpenSelectedSensor);
        sensorAction.Tag = "sensorAction";
        desktopOptions = Button("Desktop options ▾", DesktopOptions);
        desktopOptions.Tag = "desktopOptions";
        vendorCooling = Button("Open ASUS cooling", () => LaunchArmoury());
        vendorCooling.Tag = "vendorCooling";
        vendorRgb = Button("Open ASUS lighting", () => LaunchArmoury());
        vendorRgb.Tag = "vendorRgb";
        coreView = Button("Logical CPU detail", () => OpenCoreView());
        coreView.Tag = "coreView";
        list.DoubleClick += (s, e) =>
        {
            if (page == 2)
                OpenSelectedSensor();
        };
        trayMenu.Items.Insert(1, new ToolStripMenuItem("Compact widget", null, (s, e) => OpenWidget()));
        InitPersonalization();
        FormClosing += async (s, e) =>
        {
            if (closingSave)
                return;
            e.Cancel = true;
            timer.Stop();
            closingSave = true;
            Enabled = false;
            try
            {
                if (initialization != null)
                    await initialization;
                while (busy)
                    await Task.Delay(50);
                if (historySave != null)
                    await historySave;
                if (snapshot == null)
                    SaveHistory(Path.Combine(dataDir, "history.json"));
                await Task.Run(() => collector.CloseSensors());
            }
            catch
            {
            }

            BeginInvoke(new Action(Close));
        };
    }

    void LayoutRoadmap()
    {
        if (sensorSetup == null)
            return;
        sensorSetup.Visible = sensorAction.Visible = page == 2;
        Place(sensorSetup, 1018, 174, 185, 35);
        Place(sensorAction, 826, 678, 400, 35);
        if (page == 2)
        {
            Place(listHost, 246, 307, 980, 350);
            list.Columns[0].Width = (int)(370 * scale);
            list.Columns[1].Width = (int)(180 * scale);
            list.Columns[2].Width = (int)(400 * scale);
        }

        desktopOptions.Visible = page == 6;
        Place(desktopOptions, 692, 465, 305, 32);
        vendorCooling.Visible = vendorRgb.Visible = page == 3;
        Place(vendorCooling, 269, 659, 430, 43);
        Place(vendorRgb, 729, 659, 467, 43);
        coreView.Visible = page == 0;
        Place(coreView, 670, 686, 203, 30);
        LayoutExperience();
    }

    void ReceiveRoadmap(Reading r)
    {
        samplesReceived++;
        history[history.Count - 1].SensorValues = new Dictionary<string, double>(r.SensorValues);
        if (preferences.Alerts)
            foreach (var rule in rules)
            {
                double value;
                if (!r.SensorValues.TryGetValue(rule.Id, out value))
                    value = double.NaN;
                CheckThreshold(rule.Name, value, rule.Limit, Math.Max(0.01, Math.Abs(rule.Limit) * 0.05), rule.High, rule.Unit ?? "");
            }

        if (snapshot == null && (DateTime.UtcNow - lastHistorySave).TotalSeconds >= 30 && (historySave == null || historySave.IsCompleted))
        {
            lastHistorySave = DateTime.UtcNow;
            string json = EncodeHistory();
            historySave = Task.Run(() =>
            {
                try
                {
                    AtomicWrite(Path.Combine(dataDir, "history.json"), json);
                }
                catch
                {
                }
            });
        }
    }

    static void AtomicWrite(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    static void Put(Dictionary<string, double?> d, string key, double v)
    {
        d[key] = double.IsNaN(v) || double.IsInfinity(v) ? (double? )null : v;
    }

    string EncodeHistory()
    {
        var saved = new List<SavedSample>();
        foreach (var r in history)
        {
            var values = new Dictionary<string, double?>();
            foreach (var pair in r.SensorValues)
                Put(values, pair.Key, pair.Value);
            string[] keys =
            {
                "cpu",
                "gpu",
                "temp",
                "watts",
                "ram",
                "ramTotal",
                "down",
                "up",
                "read",
                "write"
            };
            double[] numbers =
            {
                r.Cpu,
                r.Gpu,
                r.Temp,
                r.Watts,
                r.MemUsed,
                r.MemTotal,
                r.Down,
                r.Up,
                r.DiskRead,
                r.DiskWrite
            };
            for (int i = 0; i < keys.Length; i++)
                Put(values, "$" + keys[i], numbers[i]);
            saved.Add(new SavedSample() { Time = r.Time.ToString("o"), Values = values });
        }

        return Json().Serialize(saved);
    }

    void SaveHistory(string path)
    {
        AtomicWrite(path, EncodeHistory());
    }

    static double SavedValue(Dictionary<string, double?> values, string key)
    {
        double? v;
        return values.TryGetValue(key, out v) && v.HasValue ? v.Value : double.NaN;
    }

    void LoadHistory(string path)
    {
        if (!File.Exists(path))
            return;
        var saved = Json().Deserialize<List<SavedSample>>(File.ReadAllText(path));
        if (saved == null)
            return;
        foreach (var item in saved.Skip(Math.Max(0, saved.Count - 1800)))
        {
            DateTime time;
            if (item.Values == null || !DateTime.TryParse(item.Time, null, System.Globalization.DateTimeStyles.RoundtripKind, out time))
                continue;
            var d = item.Values;
            var r = new Reading()
            {
                Time = time,
                Cpu = SavedValue(d, "$cpu"),
                Gpu = SavedValue(d, "$gpu"),
                Temp = SavedValue(d, "$temp"),
                Watts = SavedValue(d, "$watts"),
                MemUsed = SavedValue(d, "$ram"),
                MemTotal = SavedValue(d, "$ramTotal"),
                Down = SavedValue(d, "$down"),
                Up = SavedValue(d, "$up"),
                DiskRead = SavedValue(d, "$read"),
                DiskWrite = SavedValue(d, "$write")
            };
            foreach (var pair in d)
                if (!pair.Key.StartsWith("$") && pair.Value.HasValue)
                    r.SensorValues[pair.Key] = pair.Value.Value;
            history.Add(r);
            sampleTimes.Add(r.Time);
        }
    }

    bool UpdateRoadmapList()
    {
        if (page != 2)
            return false;
        string selected = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Name : null;
        int scroll = list.TopItem == null ? 0 : list.TopItem.Index;
        list.BeginUpdate();
        list.Items.Clear();
        list.Columns[0].Text = "Sensor";
        list.Columns[1].Text = "Current";
        list.Columns[2].Text = "Hardware / saved range";
        foreach (var d in data.Detail.Where(IncludeSensor).OrderBy(s => s.Type != "Temperature").ThenBy(s => s.Hardware).ThenBy(s => s.Name))
        {
            var values = history.Where(r => r.SensorValues.ContainsKey(d.Id)).Select(r => r.SensorValues[d.Id]).ToList();
            string range = values.Count == 0 ? "" : " · " + values.Min().ToString("0.#") + "–" + values.Max().ToString("0.#") + " " + d.Unit;
            var item = new ListViewItem(new[] { SensorName(d) + (rules.Any(rule => rule.Id == d.Id) ? "  ●" : ""), Val(d.Value, " " + d.Unit), d.Hardware + range });
            item.Name = d.Id;
            item.Selected = d.Id == selected;
            list.Items.Add(item);
        }

        if (list.Items.Count > 0)
            list.TopItem = list.Items[Math.Min(scroll, list.Items.Count - 1)];
        list.EndUpdate();
        return true;
    }

    void RichSensors()
    {
        Box(246, 155, 980, 130, panel);
        T("SENSOR CONNECTION", 268, 175, 9, mint, 720, true);
        T(data.SensorStatus, 268, 207, 15, white, 920, true);
        bool cpu = data.Detail.Any(s => s.Type == "Temperature" && s.Id.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0);
        T(cpu ? "Select a sensor for its trend, saved range, and a custom alert." : "Collecting the temperature sensors supported by your hardware.", 268, 248, 10, muted, 920);
        T("Double-click a sensor to inspect its trend. ● indicates a custom alert.", 246, 680, 9, muted, 570);
        T("Readings are stored locally with your performance history.", 246, 723, 10, muted, 970);
    }

    ContextMenuStrip CreateSensorSetupMenu(Action restart, Action install, Action documentation)
    {
        var menu = new ContextMenuStrip()
        {
            BackColor = panel,
            ForeColor = white
        };
        menu.Items.Add("Restart Pulse as administrator", null, (s, e) => restart());
        menu.Items.Add("Install / repair PawnIO sensor driver", null, (s, e) => install());
        menu.Items.Add("Open sensor documentation", null, (s, e) => documentation());
        DisposeMenuAfterClick(menu);
        return menu;
    }

    void DisposeMenuAfterClick(ContextMenuStrip menu)
    {
        menu.Closed += (s, e) => BeginInvoke(new Action(() => menu.Dispose()));
    }

    void SensorSetup()
    {
        var menu = CreateSensorSetupMenu(RestartAdmin, InstallSensorDriver, () => MessageBox.Show(this, data.SensorStatus + "\n\nOptional setup: place LibreHardwareMonitor 0.9.6 libraries in Sensors. Install PawnIO from its official release, then restart Pulse as administrator for low-level readings. See THIRD-PARTY.md. Basic monitoring works without these components.", "Pulse hardware sensors", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Show(sensorSetup, new Point(0, sensorSetup.Height));
    }

    void InstallSensorDriver()
    {
        string installer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sensors", "PawnIO_setup.exe");
        try
        {
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" });
            notice = "Finish the PawnIO installer, then restart Pulse as administrator.";
        }
        catch (Exception ex)
        {
            notice = "Sensor driver setup: " + ex.Message;
        }

        Invalidate();
    }

    void RestartAdmin()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory });
            if (p != null)
                Close();
        }
        catch (Exception ex)
        {
            notice = "Administrator restart: " + ex.Message;
            Invalidate();
        }
    }

    void LaunchArmoury()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\B9ECED6F.ArmouryCrate_qmba6cd70vzyy!App") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            notice = ex.Message;
            Invalidate();
        }
    }

    void DesktopOptions()
    {
        var menu = new ContextMenuStrip()
        {
            BackColor = panel,
            ForeColor = white
        };
        menu.Items.Add("Customize overview cards", null, (s, e) => CustomizeCards());
        menu.Items.Add("Open compact widget", null, (s, e) => OpenWidget());
        bool login = false;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            login = key != null && key.GetValue("PulsePC") != null;
        var start = new ToolStripMenuItem("Launch at login")
        {
            Checked = login
        };
        start.Click += (s, e) =>
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (login)
                        key.DeleteValue("PulsePC", false);
                    else
                        key.SetValue("PulsePC", "\"" + Application.ExecutablePath + "\"");
                }

                notice = login ? "Launch at login disabled." : "Launch at login enabled.";
            }
            catch (Exception ex)
            {
                notice = ex.Message;
            }

            Invalidate();
        };
        menu.Items.Add(start);
        menu.Items.Add("Open saved data folder", null, (s, e) =>
        {
            Directory.CreateDirectory(dataDir);
            Launch(dataDir);
        });
        menu.Items.Add("Test Windows notification", null, (s, e) => tray.ShowBalloonTip(5000, "Pulse notification test", "Notifications are working. Windows may suppress this when Do not disturb is enabled.", ToolTipIcon.Info));
        DisposeMenuAfterClick(menu);
        menu.Show(desktopOptions, new Point(0, desktopOptions.Height));
    }

    Form compactWindow;
    void OpenWidget()
    {
        if (compactWindow != null && !compactWindow.IsDisposed)
        {
            compactWindow.Show();
            compactWindow.Activate();
            return;
        }

        var widget = new Form()
        {
            Text = "Pulse · Compact",
            ClientSize = new Size(330, 205),
            BackColor = bg,
            ForeColor = white,
            TopMost = true,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterScreen
        };
        var refresh = new Timer()
        {
            Interval = 2000
        };
        widget.Paint += (s, e) =>
        {
            using (var title = new Font("Segoe UI", 14, FontStyle.Bold))
            using (var text = new Font("Segoe UI", 12))
            using (var accent = new SolidBrush(mint))
            using (var ink = new SolidBrush(white))
            {
                e.Graphics.DrawString("◈  pulse", title, accent, 20, 15);
                e.Graphics.DrawString("CPU    " + data.Cpu.ToString("0") + "%", text, ink, 20, 57);
                e.Graphics.DrawString("GPU    " + Val(data.Temp, " °C") + "  /  " + Val(data.Gpu, "%"), text, ink, 20, 89);
                e.Graphics.DrawString("RAM    " + data.MemUsed.ToString("0.0") + " / " + data.MemTotal.ToString("0.0") + " GB", text, ink, 20, 121);
                e.Graphics.DrawString(paused ? "Paused" : "Updated " + data.Time.ToString("HH:mm:ss"), text, accent, 20, 159);
            }
        };
        refresh.Tick += (s, e) => widget.Invalidate();
        widget.FormClosed += (s, e) => refresh.Dispose();
        FormClosed += (s, e) =>
        {
            if (!widget.IsDisposed)
                widget.Close();
        };
        compactWindow = widget;
        widget.Show();
        refresh.Start();
    }

    void OpenCoreView()
    {
        var f = new Form()
        {
            Text = "Pulse · Logical CPU usage",
            ClientSize = new Size(660, 415),
            BackColor = bg,
            ForeColor = white,
            StartPosition = FormStartPosition.CenterParent
        };
        var refresh = new Timer()
        {
            Interval = 1000
        };
        f.Paint += (s, e) =>
        {
            using (var font = new Font("Segoe UI", 11))
            using (var accent = new SolidBrush(mint))
            using (var ink = new SolidBrush(white))
            using (var track = new SolidBrush(line))
            {
                e.Graphics.DrawString("Logical processors · live Windows CPU counters", font, ink, 24, 22);
                for (int i = 0; i < data.Cores.Count; i++)
                {
                    int col = i % 2, row = i / 2;
                    float x = 24 + col * 320, y = 65 + row * 39;
                    e.Graphics.DrawString("CPU " + i + "   " + Val(data.Cores[i], "%"), font, ink, x, y);
                    e.Graphics.FillRectangle(track, x, y + 26, 285, 4);
                    if (!double.IsNaN(data.Cores[i]))
                        e.Graphics.FillRectangle(accent, x, y + 26, (float)(285 * data.Cores[i] / 100), 4);
                }
            }
        };
        refresh.Tick += (s, e) => f.Invalidate();
        f.FormClosed += (s, e) => refresh.Dispose();
        f.Show(this);
        refresh.Start();
    }

    void OpenSelectedSensor()
    {
        if (list.SelectedItems.Count == 0)
        {
            notice = "Select a sensor first.";
            Invalidate();
            return;
        }

        string id = list.SelectedItems[0].Name;
        var sensor = data.Detail.FirstOrDefault(s => s.Id == id);
        if (sensor == null)
            return;
        var f = new Form()
        {
            Text = "Pulse · " + sensor.Name,
            ClientSize = new Size(680, 435),
            BackColor = bg,
            ForeColor = white,
            Font = Font,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false
        };
        var existing = rules.FirstOrDefault(r => r.Id == id);
        var enabled = new CheckBox()
        {
            Text = "Custom threshold alert",
            Checked = existing != null,
            ForeColor = white,
            Location = new Point(24, 280),
            Size = new Size(285, 30)
        };
        var high = new CheckBox()
        {
            Text = "Alert above threshold",
            Checked = existing == null || existing.High,
            ForeColor = white,
            Location = new Point(24, 320),
            Size = new Size(285, 30)
        };
        var limit = new NumericUpDown()
        {
            Minimum = -10000,
            Maximum = 100000,
            DecimalPlaces = 1,
            Value = (decimal)(existing == null ? Math.Max(-10000, Math.Min(100000, double.IsNaN(sensor.Value) ? 80 : sensor.Value + 10)) : existing.Limit),
            Location = new Point(360, 322),
            Size = new Size(180, 35),
            BackColor = panel,
            ForeColor = white
        };
        var save = new Button()
        {
            Text = "Save alert",
            Location = new Point(482, 375),
            Size = new Size(172, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = panel,
            ForeColor = white
        };
        save.Click += (s, e) =>
        {
            rules.RemoveAll(r => r.Id == id);
            if (enabled.Checked)
                rules.Add(new SensorRule() { Id = id, Name = sensor.Name + " (" + sensor.Hardware + ")", Limit = (double)limit.Value, High = high.Checked, Unit = sensor.Unit });
            try
            {
                if (snapshot == null)
                    AtomicWrite(Path.Combine(dataDir, "sensor-alerts.json"), Json().Serialize(rules));
                thresholds.Clear();
                f.Close();
                UpdateList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(f, ex.Message, "Could not save alert");
            }
        };
        f.Controls.AddRange(new Control[] { enabled, high, limit, save });
        f.Controls.Add(new Label() { Text = "Recovery margin: 5% of threshold (minimum 0.01).", Location = new Point(24, 377), Size = new Size(442, 30), ForeColor = muted, Font = new Font("Segoe UI", 9) });
        f.Paint += (s, e) =>
        {
            var points = history.Where(r => r.SensorValues.ContainsKey(id)).ToList();
            using (var font = new Font("Segoe UI", 13, FontStyle.Bold))
            using (var small = new Font("Segoe UI", 10))
            using (var ink = new SolidBrush(white))
            using (var dim = new SolidBrush(muted))
            using (var pen = new Pen(mint, 2))
            {
                e.Graphics.DrawString(sensor.Name + " · " + sensor.Unit, font, ink, 24, 20);
                e.Graphics.DrawString(sensor.Hardware, small, dim, 24, 54);
                if (points.Count < 2)
                {
                    e.Graphics.DrawString("Collecting trend samples…", small, dim, 24, 125);
                    return;
                }

                double min = points.Min(r => r.SensorValues[id]), max = points.Max(r => r.SensorValues[id]);
                double span = Math.Max(1, max - min);
                double seconds = Math.Max(1, (points.Last().Time - points.First().Time).TotalSeconds);
                var plot = new List<PointF>();
                DateTime prior = points[0].Time;
                foreach (var r in points)
                {
                    if ((r.Time - prior).TotalSeconds > 15)
                    {
                        if (plot.Count > 1)
                            e.Graphics.DrawLines(pen, plot.ToArray());
                        plot.Clear();
                    }

                    plot.Add(new PointF(26 + (float)((r.Time - points.First().Time).TotalSeconds / seconds) * 625, 228 - (float)((r.SensorValues[id] - min) / span) * 123));
                    prior = r.Time;
                }

                if (plot.Count > 1)
                    e.Graphics.DrawLines(pen, plot.ToArray());
                e.Graphics.DrawString("Saved range  " + min.ToString("0.#") + "–" + max.ToString("0.#") + " " + sensor.Unit + "   ·   " + points.Count + " samples", small, dim, 24, 242);
            }
        };
        var refresh = new Timer()
        {
            Interval = 2000
        };
        refresh.Tick += (s, e) => f.Invalidate();
        f.FormClosed += (s, e) => refresh.Dispose();
        f.Show(this);
        refresh.Start();
    }

    void VerifyRoadmap()
    {
        string path = Path.Combine(Path.GetDirectoryName(snapshot), "history-roundtrip.json");
        int before = history.Count;
        SaveHistory(path);
        var saved = history.ToList();
        history.Clear();
        sampleTimes.Clear();
        LoadHistory(path);
        if (history.Count != before || history.Last().Time != saved.Last().Time || history.Last().SensorValues.Count != saved.Last().SensorValues.Count)
            throw new Exception("History round-trip failed");
        File.Delete(path);
        if (data.Cores.Count == 0 || data.DiskHealth.Count == 0 || data.Detail.Count < 3)
            throw new Exception("Roadmap collection failed");
        VerifyAuxiliaryWindows();
    }

    void VerifyAuxiliaryWindows()
    {
        VerifySensorMenu();
        OpenWidget();
        CaptureAuxiliary("Pulse · Compact", "widget.png");
        OpenCoreView();
        CaptureAuxiliary("Pulse · Logical CPU usage", "cores.png");
        page = 2;
        LayoutControls();
        UpdateList();
        var item = list.Items["nvidia/temperature"];
        if (item == null)
            throw new Exception("Sensor selection unavailable");
        item.Selected = true;
        OpenSelectedSensor();
        CaptureAuxiliary("Pulse · GPU core", "sensor-trend.png");
        page = 0;
        LayoutControls();
        UpdateList();
    }

    void VerifySensorMenu()
    {
        page = 2;
        LayoutControls();
        int restart = 0, install = 0, docs = 0;
        for (int index = 0; index < 3; index++)
        {
            var menu = CreateSensorSetupMenu(() => restart++, () => install++, () => docs++);
            menu.Show(sensorSetup, new Point(0, sensorSetup.Height));
            menu.Items[index].PerformClick();
        }

        if (restart != 1 || install != 1 || docs != 1)
            throw new Exception("Sensor setup action dispatch failed");
    }

    void CaptureAuxiliary(string title, string filename)
    {
        var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == title);
        if (form == null)
            throw new Exception("Auxiliary window failed: " + title);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(Path.GetDirectoryName(snapshot), filename));
        }

        form.Close();
    }
}
