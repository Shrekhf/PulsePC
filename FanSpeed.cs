using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

partial class Dashboard
{
    Button fanPicker;
    string selectedFan;

    SensorReading[] FanReadings()
    {
        return data.Detail.Where(s => s.Type == "Fan" && s.Unit == "RPM" &&
            !double.IsNaN(s.Value) && !double.IsInfinity(s.Value) && s.Value >= 0)
            .OrderBy(s => s.Hardware).ThenBy(s => s.Name).ThenBy(s => s.Id).ToArray();
    }

    SensorReading SelectedFan()
    {
        var fans = FanReadings();
        // Retain the selected identity when a sensor disconnects.
        return string.IsNullOrEmpty(selectedFan) ? fans.FirstOrDefault() : fans.FirstOrDefault(s => s.Id == selectedFan);
    }

    void InitFanSpeed()
    {
        if (snapshot == null)
        {
            try { selectedFan = File.ReadAllText(Path.Combine(dataDir, "fan.txt")).Trim(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        fanPicker = Button("Choose fan", () =>
        {
            var menu = new ContextMenuStrip { BackColor = panel, ForeColor = white };
            menu.Items.Add("Automatic selection", null, (s, e) => ChooseFan(null));
            foreach (var fan in FanReadings())
            {
                var captured = fan;
                menu.Items.Add(fan.Hardware + " · " + fan.Name + " · " + fan.Id, null, (s, e) => ChooseFan(captured.Id));
            }
            DisposeMenuAfterClick(menu);
            menu.Show(fanPicker, new System.Drawing.Point(0, fanPicker.Height));
        });
    }

    void ChooseFan(string id)
    {
        try
        {
            if (snapshot == null) AtomicWrite(Path.Combine(dataDir, "fan.txt"), id ?? "");
            selectedFan = id;
            Invalidate();
        }
        catch (Exception ex) { notice = "Could not save fan selection: " + ex.Message; }
    }

    void LayoutFanSpeed()
    {
        if (fanPicker == null) return;
        fanPicker.Visible = page == 0 && cards.Contains(8);
        Place(fanPicker, 875, 94, 195, 31);
    }

    void DrawFanCard(int x)
    {
        var fan = SelectedFan();
        Metric(x, "FAN SPEED", fan == null ? "—" : fan.Value.ToString("0") + " RPM",
            fan == null ? "No reading · check sensor setup" : fan.Hardware + " · " + fan.Name,
            double.NaN, mint);
    }

    void VerifyFanSpeed()
    {
        var saved = data;
        string savedSelection = selectedFan;
        try
        {
            data = new Reading();
            data.Detail.Add(new SensorReading { Id = "fan/a", Hardware = "Board", Name = "CPU fan", Type = "Fan", Unit = "RPM", Value = 0 });
            data.Detail.Add(new SensorReading { Id = "fan/b", Hardware = "Board", Name = "CPU fan", Type = "Fan", Unit = "RPM", Value = 1400 });
            data.Detail.Add(new SensorReading { Id = "gpu/control", Name = "GPU fan", Type = "Control", Unit = "%", Value = 45 });
            selectedFan = "fan/a";
            if (FanReadings().Length != 2 || SelectedFan().Value != 0) throw new Exception("Fan units or stopped-fan handling failed");
            selectedFan = "fan/b";
            if (SelectedFan().Value != 1400) throw new Exception("Fan identity selection failed");
            data.Detail.RemoveAll(s => s.Id == "fan/b");
            if (SelectedFan() != null) throw new Exception("Disconnected fan silently changed identity");
        }
        finally { data = saved; selectedFan = savedSelection; }
    }
}
