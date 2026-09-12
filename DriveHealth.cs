using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

class DriveHealthView
{
    public string Id, Name, Status;
    public List<SensorReading> Sensors = new List<SensorReading>();
    public double Value(string name)
    {
        var s = Sensors.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return s == null ? double.NaN : s.Value;
    }
}

partial class Dashboard
{
    Button drivePicker, driveMode;
    bool showDriveHealth = true;
    string selectedDrive;
    void InitDriveHealth()
    {
        drivePicker = Button("Select physical drive ▾", () =>
        {
            var menu = new ContextMenuStrip
            {
                BackColor = panel,
                ForeColor = white
            };
            foreach (var drive in HealthDrives())
            {
                var chosen = drive;
                menu.Items.Add(drive.Name + " · " + drive.Id, null, (s, e) =>
                {
                    selectedDrive = chosen.Id;
                    LayoutDriveHealth();
                    Invalidate();
                });
            }

            DisposeMenuAfterClick(menu);
            menu.Show(drivePicker, new Point(0, drivePicker.Height));
        });
        driveMode = Button("Disk activity", () =>
        {
            showDriveHealth = !showDriveHealth;
            LayoutDriveHealth();
            Invalidate();
        });
    }

    List<DriveHealthView> HealthDrives()
    {
        var result = data.Detail.Where(s => s.HardwareId != null && (s.HardwareId.StartsWith("/hdd/") || s.HardwareId.StartsWith("/nvme/") || s.HardwareId.StartsWith("/storage/"))).GroupBy(s => s.HardwareId).Select(g => new DriveHealthView { Id = g.Key, Name = g.First().Hardware, Status = "Unknown", Sensors = g.ToList() }).ToList();
        foreach (var drive in result)
        {
            var windows = data.DiskHealth.Where(h => h[0] == drive.Name).ToList();
            if (windows.Count == 1 && result.Count(d => d.Name == drive.Name) == 1)
                drive.Status = windows[0][1];
        }

        for (int i = 0; i < data.DiskHealth.Count; i++)
        {
            var h = data.DiskHealth[i];
            if (!result.Any(d => d.Name == h[0]))
                result.Add(new DriveHealthView { Id = "windows/" + i, Name = h[0], Status = h[1] });
        }

        return result;
    }

    DriveHealthView SelectedHealthDrive()
    {
        var drives = HealthDrives();
        return drives.FirstOrDefault(d => d.Id == selectedDrive) ?? drives.FirstOrDefault();
    }

    void LayoutDriveHealth()
    {
        if (drivePicker == null)
            return;
        driveMode.Visible = page == 4;
        drivePicker.Visible = page == 4 && showDriveHealth;
        Place(driveMode, 1044, 536, 160, 33);
        Place(drivePicker, 481, 536, 548, 33);
        driveMode.Text = showDriveHealth ? "Disk activity" : "Drive health";
        var drive = SelectedHealthDrive();
        drivePicker.Enabled = HealthDrives().Count > 1;
        drivePicker.Text = drive == null ? "No drive data" : drive.Name + (drivePicker.Enabled ? " ▾" : "");
    }

    static string HealthNumber(double value, string unit)
    {
        return double.IsNaN(value) ? "—" : value.ToString("0.#") + unit;
    }

    void PaintDriveHealth()
    {
        LayoutDriveHealth();
        Box(246, 526, 980, 201, panel);
        T("DRIVE HEALTH", 269, 546, 10, mint, 200, true);
        var drive = SelectedHealthDrive();
        if (drive == null)
        {
            T("Waiting for drive health data…", 269, 592, 14, muted, 900);
            return;
        }

        double temp = drive.Value("Composite Temperature");
        if (double.IsNaN(temp))
        {
            var reading = drive.Sensors.FirstOrDefault(s => s.Type == "Temperature" && !ReferenceSensor(s));
            if (reading != null)
                temp = reading.Value;
        }

        double life = drive.Value("Life"), wear = drive.Value("Percentage Used"), spare = drive.Value("Available Spare");
        string[] labels =
        {
            "TEMPERATURE",
            "LIFE REMAINING",
            "ENDURANCE USED",
            "AVAILABLE SPARE"
        };
        double[] values =
        {
            temp,
            life,
            wear,
            spare
        };
        for (int i = 0; i < 4; i++)
        {
            int x = 269 + i * 239;
            T(labels[i], x, 587, 9, muted, 222, true);
            T(HealthNumber(values[i], i == 0 ? "°C" : "%"), x, 609, 25, white, 222, true);
        }

        string status = "Windows: " + drive.Status;
        double threshold = drive.Value("Available Spare Threshold");
        if (!double.IsNaN(spare) && !double.IsNaN(threshold) && spare < threshold)
            status += " · Spare below reported threshold";
        if (!double.IsNaN(wear) && wear >= 100)
            status += " · Rated endurance reached";
        T(status, 269, 658, 10, drive.Status == "Unhealthy" || drive.Status == "Warning" ? amber : mint, 915);
        T("Reported drive values · — means unavailable · Endurance is not a failure prediction.", 269, 699, 8, muted, 922);
    }

    void VerifyDriveHealth()
    {
        var saved = data;
        try
        {
            data = new Reading();
            data.Detail.Add(new SensorReading { HardwareId = "/nvme/0", Hardware = "SSD", Name = "Life", Value = 99 });
            data.Detail.Add(new SensorReading { HardwareId = "/nvme/1", Hardware = "SSD", Name = "Life", Value = 80 });
            data.DiskHealth.Add(new[] { "SSD", "Healthy", "Windows" });
            var drives = HealthDrives();
            if (drives.Count != 2 || drives[0].Value("Life") != 99 || drives[1].Value("Life") != 80 || drives.Any(d => d.Status != "Unknown"))
                throw new Exception("Drive identity isolation failed");
            if (!double.IsNaN(drives[0].Value("Available Spare")) || HealthNumber(double.NaN, "%") != "—")
                throw new Exception("Missing drive sensor handling failed");
        }
        finally
        {
            data = saved;
        }
    }
}
