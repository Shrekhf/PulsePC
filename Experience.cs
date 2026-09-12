using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

class PulseButton : Button
{
    bool hover, pressed;
    public PulseButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = BackColor;
        if (Enabled && hover)
            fill = ControlPaint.Light(fill, pressed ? 0.04f : 0.1f);
        using (var p = new GraphicsPath())
        {
            float d = 14, w = Width - 1, h = Height - 1;
            p.AddArc(0, 0, d, d, 180, 90);
            p.AddArc(w - d, 0, d, d, 270, 90);
            p.AddArc(w - d, h - d, d, d, 0, 90);
            p.AddArc(0, h - d, d, d, 90, 90);
            p.CloseFigure();
            using (var b = new SolidBrush(fill))
                g.FillPath(b, p);
            using (var pen = new Pen(Focused ? Color.FromArgb(123, 241, 198) : Color.FromArgb(43, 54, 67)))
                g.DrawPath(pen, p);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(96, 110, 125), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

partial class Dashboard
{
    TextBox sensorSearch;
    Button sensorFilter, customize, quickWidget;
    int sensorMode;
    ImageList rowSpacing;
    ToolTip hints;
    readonly int[] navigationOrder =
    {
        0,
        2,
        1,
        4,
        5,
        3,
        6
    };
    void InitExperience()
    {
        InitAppActions();
        InitDriveHealth();
        hints = new ToolTip()
        {
            InitialDelay = 450,
            ReshowDelay = 100,
            AutoPopDelay = 7000
        };
        sensorSearch = new TextBox()
        {
            BackColor = panel,
            ForeColor = white,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Search sensors"
        };
        sensorSearch.TextChanged += (s, e) => UpdateList();
        Controls.Add(sensorSearch);
        sensorFilter = Button("Temperatures ▾", () =>
        {
            var menu = new ContextMenuStrip()
            {
                BackColor = panel,
                ForeColor = white
            };
            string[] labels =
            {
                "Temperatures",
                "Fans",
                "Power & clocks",
                "All sensors"
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                menu.Items.Add(labels[i], null, (s, e) =>
                {
                    sensorMode = index;
                    sensorFilter.Text = labels[index] + " ▾";
                    UpdateList();
                });
            }

            DisposeMenuAfterClick(menu);
            menu.Show(sensorFilter, new Point(0, sensorFilter.Height));
        });
        sensorFilter.Tag = "sensorFilter";
        customize = Button("Customize", CustomizeCards);
        customize.Tag = "customize";
        quickWidget = Button("Compact view", OpenWidget);
        quickWidget.Tag = "quickWidget";
        rowSpacing = new ImageList()
        {
            ImageSize = new Size(1, 32)
        };
        list.SmallImageList = rowSpacing;
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(list, true, null);
        list.DrawItem += (s, e) =>
        {
            e.DrawDefault = false;
        };
        list.DrawSubItem += (s, e) =>
        {
            e.DrawDefault = false;
            bool selected = e.Item.Selected;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(34, 60, 58) : e.ItemIndex % 2 == 0 ? panel : Color.FromArgb(24, 31, 40)))
                e.Graphics.FillRectangle(brush, e.Bounds);
            var rect = e.Bounds;
            rect.Inflate(-8, 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, rect, selected ? white : e.ColumnIndex == 1 ? mint : e.ColumnIndex == 2 ? muted : white, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.SelectedIndexChanged += (s, e) =>
        {
            sensorAction.Enabled = list.SelectedItems.Count > 0;
        };
        hints.SetToolTip(sensorSearch, "Filter by sensor or hardware name · Ctrl+F");
        hints.SetToolTip(sensorFilter, "Keep the useful readings in view. All sensors includes technical limits and reference values.");
        hints.SetToolTip(pause, "Pause or resume live monitoring · Ctrl+Space");
        hints.SetToolTip(quickWidget, "A small always-on-top view of your PC");
        hints.SetToolTip(customize, "Choose the four overview cards");
        hints.SetToolTip(sensorAction, "Inspect the selected reading and configure an alert · Enter");
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D7)
            {
                Navigate(navigationOrder[(int)e.KeyCode - (int)Keys.D1]);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                if (page != 2 && page != 5)
                    Navigate(2);
                if (page == 2)
                    sensorSearch.Focus();
                else
                    processSearch.Focus();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Space)
            {
                pause.PerformClick();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter && page == 2 && list.Focused)
            {
                OpenSelectedSensor();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        FormClosed += (s, e) =>
        {
            rowSpacing.Dispose();
            hints.Dispose();
        };
    }

    void Navigate(int destination)
    {
        page = destination;
        notice = "";
        LayoutControls();
        UpdateList();
        Invalidate();
    }

    void LayoutExperience()
    {
        if (sensorSearch == null)
            return;
        foreach (Control c in Controls)
        {
            string tag = c.Tag as string;
            if (tag == null)
                continue;
            if (tag.StartsWith("nav"))
            {
                int index = int.Parse(tag.Substring(3));
                int row = Array.IndexOf(navigationOrder, index);
                Place(c, 22, 157 + row * 53, 165, 42);
            }
            else if (tag == "csv")
                c.Visible = false;
            else if (tag == "export")
            {
                Place(c, 1118, 42, 114, 36);
                c.Text = "Export ▾";
            }
            else if (tag == "pause")
                Place(c, 996, 42, 110, 36);
        }

        customize.Visible = page == 0;
        Place(customize, 1085, 94, 147, 31);
        quickWidget.Visible = true;
        Place(quickWidget, 22, 653, 165, 40);
        sensorSearch.Visible = sensorFilter.Visible = page == 2;
        Place(sensorFilter, 246, 293, 201, 36);
        Place(sensorSearch, 465, 299, 762, 30);
        if (page == 2)
        {
            Place(listHost, 246, 346, 980, 323);
            Place(sensorAction, 961, 686, 265, 36);
            sensorAction.Text = "Inspect selected";
            sensorAction.Enabled = list.SelectedItems.Count > 0;
            Place(sensorSetup, 246, 686, 158, 36);
            list.Columns[0].Width = (int)(330 * scale);
            list.Columns[1].Width = (int)(160 * scale);
            list.Columns[2].Width = (int)(460 * scale);
        }

        int height = Math.Max(24, (int)(32 * scale));
        if (rowSpacing.ImageSize.Height != height)
            rowSpacing.ImageSize = new Size(1, height);
        LayoutAppActions();
        LayoutDriveHealth();
        if (page == 0)
        {
            Place(coreView, 710, 347, 164, 32);
            Place(rangeButton, 552, 347, 146, 32);
        }
    }

    void ShowExportMenu()
    {
        var button = Controls.Cast<Control>().First(c => (c.Tag as string) == "export");
        var menu = new ContextMenuStrip()
        {
            BackColor = panel,
            ForeColor = white
        };
        menu.Items.Add("Hardware report (.txt)", null, (s, e) => Export());
        menu.Items.Add("Performance history (.csv)", null, (s, e) => ExportHistory());
        DisposeMenuAfterClick(menu);
        menu.Show(button, new Point(0, button.Height));
    }

    static bool ReferenceSensor(SensorReading s)
    {
        return new[]
        {
            "limit",
            "threshold",
            "resolution",
            "warning",
            "critical"
        }.Any(word => s.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    bool IncludeSensor(SensorReading s)
    {
        if (sensorMode == 0 && (s.Type != "Temperature" || ReferenceSensor(s) || s.Name.StartsWith("Temperature #")))
            return false;
        if (sensorMode == 1 && s.Type != "Fan" && !(s.Type == "Control" && s.Name.IndexOf("fan", StringComparison.OrdinalIgnoreCase) >= 0))
            return false;
        if (sensorMode == 2 && s.Type != "Power" && s.Type != "Clock")
            return false;
        string term = sensorSearch == null ? "" : sensorSearch.Text.Trim();
        return (s.Name + " " + s.Hardware + " " + SensorName(s)).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    string SensorName(SensorReading s)
    {
        if (s.Name == "Core (Tctl/Tdie)")
            return "CPU package";
        if (s.Name == "Composite Temperature")
            return "SSD composite";
        return s.Name;
    }

    SensorReading CpuReading()
    {
        return data.Detail.FirstOrDefault(s => s.Type == "Temperature" && s.Name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0) ?? data.Detail.FirstOrDefault(s => s.Type == "Temperature" && s.Id.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    SensorReading RamReading()
    {
        return data.Detail.Where(s => s.Type == "Temperature" && s.Name.StartsWith("DIMM #")).OrderByDescending(s => s.Value).FirstOrDefault();
    }

    SensorReading SsdReading()
    {
        return data.Detail.FirstOrDefault(s => s.Type == "Temperature" && s.Name == "Composite Temperature");
    }

    string Temperature(SensorReading reading)
    {
        return reading == null ? "—" : reading.Value.ToString("0.#") + "°C";
    }

    void ThermalTile(float x, string title, string value, string detail, Color color)
    {
        Box(x, 155, 232, 117, panel);
        T(title, x + 19, 171, 9, muted, 195, true);
        T(value, x + 16, 197, 26, color, 200, true);
        T(detail, x + 19, 245, 8, muted, 195);
    }

    void FocusedThermals()
    {
        ThermalTile(246, "PROCESSOR", Temperature(CpuReading()), "CPU package", mint);
        ThermalTile(496, "GRAPHICS", double.IsNaN(data.Temp) ? "—" : data.Temp.ToString("0") + "°C", "GPU core", blue);
        ThermalTile(746, "MEMORY", Temperature(RamReading()), "Warmest memory module", white);
        ThermalTile(996, "STORAGE", Temperature(SsdReading()), "SSD composite", white);
        T("Search sensor or hardware name", 467, 276, 8, muted, 720);
        T("Double-click a reading to explore its trend and alerts.", 426, 696, 9, muted, 523);
        T("Live measurements · technical reference values are available under All sensors.", 247, 735, 8, muted, 972);
    }

    void OverviewThermals()
    {
        Box(246, 583, 650, 145, panel);
        T("TEMPERATURES", 266, 600, 9, muted, 610, true);
        string[] labels =
        {
            "CPU",
            "GPU",
            "RAM",
            "SSD"
        };
        string[] values =
        {
            Temperature(CpuReading()),
            double.IsNaN(data.Temp) ? "—" : data.Temp.ToString("0") + "°C",
            Temperature(RamReading()),
            Temperature(SsdReading())
        };
        for (int i = 0; i < 4; i++)
        {
            float x = 266 + 154 * i;
            T(labels[i], x, 632, 9, muted, 135);
            T(values[i], x, 654, 22, i == 0 ? mint : white, 145, true);
        }

        T("Detailed readings in Thermals", 266, 704, 8, muted, 605);
    }

    void VerifyExperience()
    {
        page = 2;
        LayoutControls();
        sensorMode = 0;
        sensorSearch.Text = "__no_matching_sensor__";
        UpdateList();
        if (list.Items.Count != 0)
            throw new Exception("Sensor filtering failed");
        sensorSearch.Text = "";
        UpdateList();
        if (list.Items.Cast<ListViewItem>().Any(i => i.Text.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) >= 0))
            throw new Exception("Technical references leaked into focused temperatures");
        sensorMode = 3;
        UpdateList();
        if (list.Items.Count < data.Detail.Count)
            throw new Exception("Advanced sensors were lost");
        sensorMode = 0;
        UpdateList();
        Navigate(0);
    }
}
