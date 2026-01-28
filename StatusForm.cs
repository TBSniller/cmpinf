using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class StatusForm : Form
{
    private readonly ListBox logListBox;
    private readonly CheckBox debugCheckBox;
    private readonly Label pawnIoStatusLabel;
    private readonly LinkLabel pawnIoStateLinkLabel;
    private readonly Button openPawnIoInstallerButton;
    private readonly Button openRepoButton;
    private static StatusForm? instance;
    private const string PawnIoIssueUrl = "https://github.com/namazso/PawnIO.Setup/issues/1";
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
        pawnIoStatusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 20
        };
        pawnIoStateLinkLabel = new LinkLabel
        {
            AutoSize = true,
            TextAlign = ContentAlignment.TopLeft,
            Dock = DockStyle.Top,
            MaximumSize = new Size(460, 0),
            LinkBehavior = LinkBehavior.HoverUnderline
        };
        openPawnIoInstallerButton = new Button
        {
            Text = "Open PawnIO Installer Website",
            AutoSize = true
        };
        openRepoButton = new Button
        {
            Text = "Open CmpInf GitHub page",
            AutoSize = true
        };
        var pawnIoButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Height = 30,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        pawnIoButtonPanel.Controls.Add(openPawnIoInstallerButton);
        pawnIoButtonPanel.Controls.Add(openRepoButton);
        var pawnIoPanel = new Panel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(8, 4, 8, 4),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        pawnIoPanel.Controls.Add(pawnIoButtonPanel);
        pawnIoPanel.Controls.Add(pawnIoStateLinkLabel);
        pawnIoPanel.Controls.Add(pawnIoStatusLabel);
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
        this.Controls.Add(pawnIoPanel);
        this.Controls.Add(label);
        openPawnIoInstallerButton.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://pawnio.eu/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Opening PawnIO installer website failed: {ex.Message}");
            }
        };
        openRepoButton.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/TBSniller/cmpinf",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Opening repository link failed: {ex.Message}");
            }
        };
        pawnIoStateLinkLabel.LinkClicked += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PawnIoIssueUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Opening PawnIO link failed: {ex.Message}");
            }
        };
        UpdatePawnIoStatus();
        this.VisibleChanged += (s, e) =>
        {
            if (this.Visible)
            {
                Log.OnLog += AddLog;
                // Re-emit driver status once the log window is visible so the user can see which kernel driver is active.
                HardwareReader.LogPawnIoStatus();
                UpdatePawnIoStatus();
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

    private void UpdatePawnIoStatus()
    {
        bool installed = HardwareReader.IsPawnIoInstalled();
        var version = HardwareReader.GetPawnIoVersion();
        pawnIoStatusLabel.Text = installed
            ? $"PawnIO status: installed (version {version})."
            : "PawnIO status: not installed. Restart CmpInf after installation.";
        const string linkText = "[PawnIO.Setup GitHub Issue #1]";
        const string missingText = "PawnlO is required for some motherboard or CPU sensors, that are not handled by LibreHardwareMonitorLib alone. Be aware that PawnIO can have an influence on cheat detection software like FaceIT: " + linkText;
        pawnIoStateLinkLabel.Text = installed
            ? "PawnIO is installed; motherboard and CPU sensors should be available."
            : missingText;
        pawnIoStateLinkLabel.Links.Clear();
        if (!installed)
        {
            int start = missingText.IndexOf(linkText, StringComparison.Ordinal);
            if (start >= 0)
            {
                pawnIoStateLinkLabel.Links.Add(start, linkText.Length, PawnIoIssueUrl);
                pawnIoStateLinkLabel.LinkArea = new LinkArea(start, linkText.Length);
            }
        }
        else
        {
            pawnIoStateLinkLabel.LinkArea = new LinkArea(0, 0);
        }
    }
}
