using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

partial class Dashboard
{
    ProcessReading[] CaptureAppGroup(string group)
    {
        string filter = processSearch.Text.Trim();
        return data.Processes.Where(p => string.Equals(p.Name, group, StringComparison.OrdinalIgnoreCase) && (p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || p.Id.ToString().Contains(filter))).ToArray();
    }

    static string AppActionReason(ProcessReading app, bool kill)
    {
        if (app == null)
            return "Select an app or process first.";
        if (app.Id == Process.GetCurrentProcess().Id)
            return "This is Pulse itself. Use the window close button to exit Pulse.";
        if (app.Id <= 4)
            return "Windows system process: these actions are unavailable.";
        try
        {
            using (var p = ResolveApp(app))
            {
                bool critical;
                if (!IsProcessCritical(p.Handle, out critical))
                    return "Windows did not allow the process protection check.";
                if (critical)
                    return "Critical Windows process: these actions are unavailable.";
                if (!kill && p.MainWindowHandle == IntPtr.Zero)
                    return "No app window to close. Kill process is available.";
                return null;
            }
        }
        catch (Exception ex)
        {
            return "Process unavailable: " + ex.Message;
        }
    }

    static bool CanGroupAction(ProcessReading app, bool kill)
    {
        return AppActionReason(app, kill) == null;
    }

    void AddGroupActions(ToolStripItemCollection items, string group, ProcessReading[] members)
    {
        var closable = members.Where(p => CanGroupAction(p, false)).ToArray();
        var killable = members.Where(p => CanGroupAction(p, true)).ToArray();
        AddTaskItem(items, "Close app", () => RunGroupAction(group, closable, false), closable.Length > 0);
        AddTaskItem(items, "Kill processes…", () => RunGroupAction(group, killable, true), killable.Length > 0);
    }

    static int ApplyGroupAction(ProcessReading[] members, Action<ProcessReading> action, List<string> failures)
    {
        int succeeded = 0;
        foreach (var app in members)
        {
            try
            {
                action(app);
                succeeded++;
            }
            catch (Exception ex)
            {
                failures.Add("PID " + app.Id + ": " + ex.Message);
            }
        }

        return succeeded;
    }

    void RunGroupAction(string group, ProcessReading[] members, bool kill)
    {
        if (members.Length == 0)
            return;
        if (kill && MessageBox.Show(this, "Force kill " + members.Length + " processes in the " + group + " group?\n\nPIDs: " + string.Join(", ", members.Select(p => p.Id.ToString()).ToArray()) + "\n\nUnsaved work may be lost. This affects the listed processes only.", "Kill process group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        var failures = new List<string>();
        int succeeded = ApplyGroupAction(members, kill ? (Action<ProcessReading>)EndApp : CloseApp, failures);
        notice = (kill ? "Kill requested for " : "Close requested for ") + succeeded + " processes in " + group + "." + (kill ? "" : " Check the app for save prompts.");
        if (failures.Count > 0)
        {
            notice += " " + failures.Count + " could not be processed.";
            MessageBox.Show(this, notice + "\n\n" + string.Join("\n", failures.Take(8).ToArray()), "Pulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Invalidate();
    }

    void VerifyGroupActions()
    {
        var saved = data.Processes;
        string filter = processSearch.Text;
        try
        {
            data.Processes = new List<ProcessReading>
            {
                new ProcessReading
                {
                    Id = 111,
                    Name = "Example",
                    Start = 1
                },
                new ProcessReading
                {
                    Id = 112,
                    Name = "Example",
                    Start = 2
                },
                new ProcessReading
                {
                    Id = 113,
                    Name = "Other",
                    Start = 3
                }
            };
            processSearch.Text = "";
            var captured = CaptureAppGroup("Example");
            data.Processes.Add(new ProcessReading { Id = 114, Name = "Example", Start = 4 });
            var visited = new List<int>();
            var errors = new List<string>();
            int count = ApplyGroupAction(captured, p =>
            {
                visited.Add(p.Id);
                if (p.Id == 111)
                    throw new InvalidOperationException("Already exited");
            }, errors);
            if (count != 1 || errors.Count != 1 || !visited.SequenceEqual(new[] { 111, 112 }))
                throw new Exception("Group action snapshot or failure handling failed");
            processSearch.Text = "112";
            if (CaptureAppGroup("Example").Length != 1)
                throw new Exception("Filtered group capture failed");
            using (var menu = CreateTaskMenu("Example"))
            {
                if (!menu.Items.Cast<ToolStripItem>().Any(i => i.Text == "Close app") || !menu.Items.Cast<ToolStripItem>().Any(i => i.Text == "Kill processes…"))
                    throw new Exception("Group action menu missing");
            }
        }
        finally
        {
            data.Processes = saved;
            processSearch.Text = filter;
        }
    }
}
