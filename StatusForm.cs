using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;

public class StatusForm : Form
{
    private readonly ListBox logListBox;
    private readonly CheckBox debugCheckBox;
    private static StatusForm? instance;
    public static StatusForm ShowSingleton()
    {
        if (instance == null || instance.IsDisposed)
        {
            instance = new StatusForm();
        }
        if (!instance.Visible)
        {
            instance.Show();
        }
        else
        {
            if (instance.WindowState == FormWindowState.Minimized)
                instance.WindowState = FormWindowState.Normal;
            instance.BringToFront();
            instance.Activate();
            FlashWindow(instance.Handle, true);
        }
        return instance;
    }
    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    public StatusForm()
    {
        instance = this;
        this.Text = "CmpInf - Logs";
        this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ClientSize = new Size(500, 300);
        this.TopMost = true;
        var label = new Label
        {
            Text = "CmpInf - Replacement for SteelSeries System Monitor App (using LibreHardwareMonitorLib)",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 32
        };
        debugCheckBox = new CheckBox
        {
            Text = "Enable Debug Logs",
            Dock = DockStyle.Top,
            Height = 24
        };
        debugCheckBox.CheckedChanged += (s, e) => {
            Log.Verbose = debugCheckBox.Checked;
        };
        logListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true
        };
        this.Controls.Add(logListBox);
        this.Controls.Add(debugCheckBox);
        this.Controls.Add(label);
        this.VisibleChanged += (s, e) =>
        {
            if (this.Visible)
            {
                Log.OnLog += AddLog;
            }
            else
            {
                Log.OnLog -= AddLog;
                logListBox.Items.Clear();
            }
        };
        this.FormClosing += (s, e) => {
            // Hide instead of closing, so it can be reopened
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        };
    }
    private void AddLog(string level, string msg)
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(new Action(() => AddLog(level, msg)));
            return;
        }
        if (level == "DEBUG" && !debugCheckBox.Checked) return;
        logListBox.Items.Add(msg);
        logListBox.TopIndex = logListBox.Items.Count - 1;
    }
}
