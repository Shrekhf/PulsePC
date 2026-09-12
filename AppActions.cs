using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class Dashboard
{
    Button endProcess, openLocation, forceKill;
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool IsProcessCritical(IntPtr process, out bool critical);
    void InitAppActions()
    {
        InitProcessGroups();
        endProcess = Button("Close app", CloseSelectedApp);
        forceKill = Button("Force kill", () => RunAppAction(true));
        openLocation = Button("Open file location", () => RunAppAction(false));
        list.SelectedIndexChanged += (s, e) => UpdateAppActions();
        list.MouseUp += (s, e) =>
        {
            if (page != 5 || e.Button != MouseButtons.Right)
                return;
            var hit = list.GetItemAt(e.X, e.Y);
            if (hit == null)
                return;
            list.SelectedItems.Clear();
            hit.Selected = true;
            ShowTaskMenu(hit.Tag, e.Location);
        };
        list.KeyDown += (s, e) =>
        {
            if (page == 5 && (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10)) && list.SelectedItems.Count == 1)
            {
                var hit = list.SelectedItems[0];
                ShowTaskMenu(hit.Tag, new Point(hit.Bounds.Left, hit.Bounds.Bottom));
                e.Handled = e.SuppressKeyPress = true;
            }
        };
    }

    void LayoutAppActions()
    {
        if (endProcess == null)
            return;
        endProcess.Visible = openLocation.Visible = forceKill.Visible = page == 5;
        if (page == 5)
        {
            Place(listHost, 246, 311, 980, 340);
            Place(openLocation, 640, 670, 230, 36);
            Place(endProcess, 884, 670, 166, 36);
            Place(forceKill, 1064, 670, 162, 36);
        }

        UpdateAppActions();
    }

    ProcessReading SelectedApp()
    {
        return page == 5 && list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as ProcessReading : null;
    }

    string appActionStatus = "Select an app or process to use the actions.";
    void UpdateAppActions()
    {
        if (endProcess == null)
            return;
        UpdateGroupButton();
        var app = SelectedApp();
        openLocation.Enabled = app != null && app.Start != 0;
        forceKill.Enabled = CanGroupAction(app, true);
        endProcess.Enabled = CanGroupAction(app, false);
        appActionStatus = app == null ? "Select an app or process to use the actions." : app.Name + " / " + app.Id + " · " + (AppActionReason(app, true) ?? AppActionReason(app, false) ?? "Close app or kill this process.");
        var group = SelectedGroup();
        if (group != null)
        {
            var members = CaptureAppGroup(group);
            int close = members.Count(p => CanGroupAction(p, false)), kill = members.Count(p => CanGroupAction(p, true));
            endProcess.Enabled = close > 0;
            forceKill.Enabled = kill > 0;
            appActionStatus = group + " · " + (kill == 0 ? "No accessible, unprotected processes are available." : close + " windows can close · " + kill + " processes can be killed.");
        }

        if (page == 5)
            Invalidate();
    }

    static Process ResolveApp(ProcessReading app)
    {
        if (app == null || app.Start == 0)
            throw new InvalidOperationException("Select an accessible process first.");
        var p = Process.GetProcessById(app.Id);
        try
        {
            if (p.HasExited || p.StartTime.ToUniversalTime().Ticks != app.Start)
                throw new InvalidOperationException("This process has exited. Select it again from the refreshed list.");
            return p;
        }
        catch
        {
            p.Dispose();
            throw;
        }
    }

    static void EndApp(ProcessReading app)
    {
        using (var p = ResolveApp(app))
        {
            bool critical;
            if (p.Id <= 4 || p.Id == Process.GetCurrentProcess().Id || !IsProcessCritical(p.Handle, out critical) || critical)
                throw new InvalidOperationException("Pulse cannot end this protected process.");
            p.Kill();
        }
    }

    static void CloseApp(ProcessReading app)
    {
        using (var p = ResolveApp(app))
        {
            bool critical;
            if (p.Id <= 4 || p.Id == Process.GetCurrentProcess().Id || !IsProcessCritical(p.Handle, out critical) || critical)
                throw new InvalidOperationException("Pulse cannot close this protected process.");
            if (!p.CloseMainWindow())
                throw new InvalidOperationException("This process has no app window to close. Use Force kill if you want to terminate it.");
        }
    }

    void CloseSelectedApp()
    {
        var group = SelectedGroup();
        if (group != null)
        {
            RunGroupAction(group, CaptureAppGroup(group).Where(p => CanGroupAction(p, false)).ToArray(), false);
            return;
        }

        CloseSelectedApp(SelectedApp());
    }

    void CloseSelectedApp(ProcessReading app)
    {
        if (app == null)
            return;
        try
        {
            CloseApp(app);
            notice = "Close requested for " + app.Name + ". Check the app for a save prompt.";
        }
        catch (Exception ex)
        {
            notice = ex.Message;
            MessageBox.Show(this, notice, "Pulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Invalidate();
    }

    static string AppPath(ProcessReading app)
    {
        using (var p = ResolveApp(app))
        {
            string path = p.MainModule.FileName;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new InvalidOperationException("The executable location is unavailable.");
            return path;
        }
    }

    void RunAppAction(bool end)
    {
        var group = SelectedGroup();
        if (end && group != null)
        {
            RunGroupAction(group, CaptureAppGroup(group).Where(p => CanGroupAction(p, true)).ToArray(), true);
            return;
        }

        RunAppAction(end, SelectedApp());
    }

    void RunAppAction(bool end, ProcessReading app)
    {
        if (app == null)
            return;
        try
        {
            if (end)
            {
                if (MessageBox.Show(this, "Force kill " + app.Name + " (PID " + app.Id + ")?\n\nUnsaved work may be lost. Only this process will be ended.", "Force kill", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                EndApp(app);
                notice = "Force kill requested for " + app.Name + ". The list will update on the next refresh.";
            }
            else
            {
                string path = AppPath(app);
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                notice = "Opened the location of " + app.Name + ".";
            }
        }
        catch (Exception ex)
        {
            notice = (end ? "Could not end process: " : "Could not open location: ") + ex.Message;
            MessageBox.Show(this, notice, "Pulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Invalidate();
    }
}
