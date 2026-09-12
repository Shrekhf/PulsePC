using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

partial class Dashboard
{
    string[] cardChoices =
    {
        "CPU utilization",
        "GPU temperature",
        "Memory in use",
        "GPU power",
        "GPU utilization",
        "VRAM in use",
        "Download speed",
        "Disk space free"
    };
    int[] cards =
    {
        0,
        1,
        2,
        3
    };
    void InitPersonalization()
    {
        try
        {
            string icon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pulse.ico");
            if (File.Exists(icon))
            {
                Icon = new Icon(icon);
                tray.Icon = Icon;
            }

            string config = Path.Combine(dataDir, "overview.json");
            if (snapshot == null && File.Exists(config))
            {
                var saved = Json().Deserialize<int[]>(File.ReadAllText(config));
                if (saved.Length == 4 && saved.All(i => i >= 0 && i < cardChoices.Length))
                    cards = saved;
            }
        }
        catch
        {
        }
    }

    void CustomizeCards()
    {
        var f = new Form()
        {
            Text = "Pulse · Customize overview",
            ClientSize = new Size(500, 335),
            BackColor = bg,
            ForeColor = white,
            Font = Font,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false
        };
        var choices = new ComboBox[4];
        for (int i = 0; i < 4; i++)
        {
            var label = new Label()
            {
                Text = "Card " + (i + 1),
                Location = new Point(24, 30 + i * 57),
                Size = new Size(95, 30),
                ForeColor = white
            };
            choices[i] = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(135, 28 + i * 57),
                Size = new Size(335, 30),
                BackColor = panel,
                ForeColor = white
            };
            choices[i].Items.AddRange(cardChoices);
            choices[i].SelectedIndex = cards[i];
            f.Controls.Add(label);
            f.Controls.Add(choices[i]);
        }

        var save = new Button()
        {
            Text = "Save layout",
            Location = new Point(310, 273),
            Size = new Size(160, 37),
            BackColor = panel,
            ForeColor = white,
            FlatStyle = FlatStyle.Flat
        };
        save.Click += (s, e) =>
        {
            int[] next = choices.Select(c => c.SelectedIndex).ToArray();
            try
            {
                if (snapshot == null)
                    AtomicWrite(Path.Combine(dataDir, "overview.json"), Json().Serialize(next));
                cards = next;
                f.Close();
                Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(f, ex.Message, "Could not save layout");
            }
        };
        f.Controls.Add(save);
        f.ShowDialog(this);
    }

    void DrawCards()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            int x = 246 + 250 * i;
            switch (cards[i])
            {
                case 0:
                    Metric(x, "CPU UTILIZATION", data.Cpu.ToString("0") + "%", collector.CpuDetail, data.Cpu, mint);
                    break;
                case 1:
                    Metric(x, "GPU TEMPERATURE", double.IsNaN(data.Temp) ? "—" : data.Temp.ToString("0") + "°C", "NVIDIA graphics sensor", data.Temp, amber);
                    break;
                case 2:
                    Metric(x, "MEMORY IN USE", data.MemUsed.ToString("0.0") + " GB", data.MemTotal.ToString("0.0") + " GB usable", data.MemTotal > 0 ? 100 * data.MemUsed / data.MemTotal : 0, blue);
                    break;
                case 3:
                    Metric(x, "GPU POWER", double.IsNaN(data.Watts) ? "—" : data.Watts.ToString("0") + " W", "Live board power draw", double.NaN, mint);
                    break;
                case 4:
                    Metric(x, "GPU UTILIZATION", double.IsNaN(data.Gpu) ? "—" : data.Gpu.ToString("0") + "%", "NVIDIA graphics load", data.Gpu, blue);
                    break;
                case 5:
                    Metric(x, "VRAM IN USE", double.IsNaN(data.Vram) ? "—" : data.Vram.ToString("0.0") + " GB", Val(data.VramTotal, " GB available"), data.VramTotal > 0 ? 100 * data.Vram / data.VramTotal : double.NaN, blue);
                    break;
                case 6:
                    Metric(x, "DOWNLOAD SPEED", data.Down.ToString("0.00"), "MB/s · active adapters", double.NaN, mint);
                    break;
                case 7:
                    double total = data.Volumes.Sum(v => v.Total), free = data.Volumes.Sum(v => v.Free);
                    Metric(x, "DISK SPACE FREE", free.ToString("0") + " GB", "Across fixed volumes", total > 0 ? 100 * free / total : double.NaN, mint);
                    break;
            }
        }
    }
}
