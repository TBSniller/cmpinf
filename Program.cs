using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Security.Principal;
using System.Drawing;
using System.Reflection;

class Program
{
    // Constants for file names
    private const string AppName = "CmpInf_SteelSeriesOledPcInfo";
    private const string ExeName = "CmpInf_SteelSeriesOledPcInfo.exe";
    private const string SettingsFile = "settings.json";
    private const string SensorsFile = "available-sensors.json";

    /// <summary>
    /// Main entry point. Handles self-move to AppData, settings/sensors file creation, and tray icon setup.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        string? currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentExe))
        {
            Log.Warn("Could not determine current executable path.");
            return;
        }
        MainSync(args);
    }

    static void MainSync(string[] args)
    {
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
        string appDataExe = Path.Combine(appDataDir, ExeName);
        string? currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentExe))
        {
            Log.Warn("Could not determine current executable path.");
            return;
        }
        bool isInAppData = string.Equals(
            Path.GetFullPath(currentExe),
            Path.GetFullPath(appDataExe),
            StringComparison.OrdinalIgnoreCase);
        string settingsPath = Path.Combine(appDataDir, SettingsFile);
        string sensorsPath = Path.Combine(appDataDir, SensorsFile);

        // Self-move to AppData if needed
        if (!isInAppData)
        {
            Directory.CreateDirectory(appDataDir);
            File.Copy(currentExe, appDataExe, true);
            if (!File.Exists(settingsPath))
            {
                File.WriteAllText(settingsPath, Newtonsoft.Json.JsonConvert.SerializeObject(Settings.GetDefault(), Newtonsoft.Json.Formatting.Indented));
            }
            if (!File.Exists(sensorsPath))
            {
                var initialDesiredMode = DetermineHardwareAccessMode(Settings.GetDefault());
                GenerateAvailableSensors(sensorsPath, initialDesiredMode, "Initial sensor scan (first run)");
            }
            var result = MessageBox.Show(
                "The application will now move itself to AppData and restart from there. You can access the settings and uninstall the application using the Tray-Icon.\n\nDo you want to create a shortcut on your Desktop?",
                "CmpInf - First Start", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktop, $"{AppName}.lnk");
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    try
                    {
                        dynamic? shell = Activator.CreateInstance(shellType);
                        if (shell == null) return;
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = appDataExe;
                        shortcut.WorkingDirectory = appDataDir;
                        shortcut.WindowStyle = 1;
                        shortcut.Description = "CmpInfo - SteelSeriesOLEDPCInfo";
                        shortcut.IconLocation = appDataExe;
                        shortcut.Save();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Failed to create desktop shortcut: {ex.Message}");
                    }
                }
            }
            var psi = new System.Diagnostics.ProcessStartInfo(appDataExe) { UseShellExecute = true };
            if (IsRunAsAdmin()) psi.Verb = "runas";
            try { System.Diagnostics.Process.Start(psi); } catch (Exception ex) { Log.Warn($"Restart from AppData failed: {ex.Message}"); }
            Environment.Exit(0);
            return;
        }

        // Ensure settings file exists
        if (!File.Exists(settingsPath))
        {
            File.WriteAllText(settingsPath, Newtonsoft.Json.JsonConvert.SerializeObject(Settings.GetDefault(), Newtonsoft.Json.Formatting.Indented));
        }

        if (!string.IsNullOrEmpty(appDataDir))
        {
            Directory.SetCurrentDirectory(appDataDir);
        }
        else
        {
            Log.Warn("AppData directory is null or empty. Working directory not set.");
        }

        // Load settings
        Settings? settings = null;
        if (File.Exists(settingsPath))
        {
            try { settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath)); } catch (Exception ex) { Log.Warn($"settings.json could not be loaded: {ex.Message}"); }
        }
        if (settings == null)
        {
            settings = Settings.GetDefault();
            Log.Warn("settings.json invalid, using defaults.");
        }

        var desiredMode = DetermineHardwareAccessMode(settings);
        if (!File.Exists(sensorsPath))
        {
            GenerateAvailableSensors(sensorsPath, desiredMode, "Initial sensor scan");
        }

        // Admin rights check
        if (settings.RunAsAdmin && !IsRunAsAdmin())
        {
            RestartAsAdmin();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Icon? appIcon = null;
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream("CmpInf.cmpinf_icon.ico"))
        {
            if (stream != null)
                appIcon = new Icon(stream);
            else
                appIcon = SystemIcons.Application;
        }
        var trayIcon = new NotifyIcon
        {
            Icon = appIcon!,
            Visible = true,
            Text = "CmpInf - Replacement for SteelSeries System Monitor App"
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (s, e) => StatusForm.ShowSingleton());
        contextMenu.Items.Add(new ToolStripSeparator());
        var settingsMenu = new ToolStripMenuItem("Settings");
        settingsMenu.DropDownItems.Clear();
        settingsMenu.DropDownItems.Add("Regenerate available-sensors.json (after installing PawnIO)", null, (s, e) => {
            try {
                var desiredMode = DetermineHardwareAccessMode(settings);
                GenerateAvailableSensors(SensorsFile, desiredMode, "Manual sensor regeneration");
                Log.Info("available-sensors.json regenerated.");
            } catch (Exception ex) { Log.Warn($"Regenerate sensors failed: {ex.Message}"); }
        });
        settingsMenu.DropDownItems.Add("Open available-sensors.json", null, (s, e) => {
            try {
                if (!File.Exists(SensorsFile))
                {
                    var desiredMode = DetermineHardwareAccessMode(settings);
                    GenerateAvailableSensors(SensorsFile, desiredMode, "Opening available-sensors.json");
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SensorsFile,
                    UseShellExecute = true
                });
            } catch (Exception ex) { Log.Warn($"Open sensors.json failed: {ex.Message}"); }
        });
        settingsMenu.DropDownItems.Add("Open settings.json", null, (s, e) => {
            try {
                if (!File.Exists(SettingsFile))
                {
                    File.WriteAllText(SettingsFile, Newtonsoft.Json.JsonConvert.SerializeObject(Settings.GetDefault(), Newtonsoft.Json.Formatting.Indented));
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SettingsFile,
                    UseShellExecute = true
                });
            } catch (Exception ex) { Log.Warn($"Open settings.json failed: {ex.Message}"); }
        });
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        var autostartItem = new ToolStripMenuItem("Enable autostart") { CheckOnClick = true };
        bool isInitializing = true;
        autostartItem.Checked = AutostartHelper.IsAutostartTaskEnabled() || AutostartHelper.IsAutostartShortcutEnabled();
        autostartItem.CheckedChanged += AutostartCheckedChanged;
        isInitializing = false;
        settingsMenu.DropDownItems.Add(autostartItem);

        var adminItem = new ToolStripMenuItem("Run as administrator") { CheckOnClick = true };
        adminItem.Checked = IsRunAsAdmin();
        adminItem.CheckedChanged += (s, e) => {
            settings.RunAsAdmin = adminItem.Checked;
            File.WriteAllText(SettingsFile, Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented));
            bool wasAutostart = AutostartHelper.IsAutostartTaskEnabled() || AutostartHelper.IsAutostartShortcutEnabled();
            if (wasAutostart)
            {
                try { AutostartHelper.InstallToAppDataAndEnableAutostart(settings.RunAsAdmin); } catch (Exception ex) { MessageBox.Show($"Error updating autostart: {ex.Message}", "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            if (settings.RunAsAdmin && !IsRunAsAdmin())
            {
                if (wasAutostart)
                {
                    MessageBox.Show(
                        "Autostart was enabled for user mode. After the restart with administrator rights, you need to enable autostart again for admin mode.",
                        "Autostart notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                RestartAsAdmin();
                Application.Exit();
            }
            autostartItem.Checked = AutostartHelper.IsAutostartTaskEnabled() || AutostartHelper.IsAutostartShortcutEnabled();
        };
        settingsMenu.DropDownItems.Add(adminItem);
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        settingsMenu.DropDownItems.Add("Uninstall", null, (s, e) => {
            string msg = AutostartHelper.Uninstall();
            MessageBox.Show(msg, "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        contextMenu.Items.Add(settingsMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) => {
            trayIcon.Visible = false;
            Application.Exit();
            Environment.Exit(0);
        });
        trayIcon.ContextMenuStrip = contextMenu;
        trayIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                StatusForm.ShowSingleton();
            }
        };
        Application.ApplicationExit += (s, e) => trayIcon.Visible = false;
        Task.Run(() => RunMonitor(args));
        Application.Run();

        // Event handler extracted as a method for clarity
        void AutostartCheckedChanged(object? sender, EventArgs e)
        {
            if (isInitializing) return;
            var autostartItemLocal = (ToolStripMenuItem)sender!;
            bool success = false;
            string msg = "";
            try {
                bool isAdmin = IsRunAsAdmin();
                string appDataExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, ExeName);
                if (autostartItemLocal.Checked)
                {
                    if (isAdmin && !settings.RunAsAdmin) {
                        settings.RunAsAdmin = true;
                        File.WriteAllText(SettingsFile, Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented));
                    }
                    msg = AutostartHelper.InstallToAppDataAndEnableAutostart(isAdmin);
                    success = AutostartHelper.IsAutostartTaskEnabled() || AutostartHelper.IsAutostartShortcutEnabled();
                    string? currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (success && !string.IsNullOrEmpty(currentExePath) && !string.Equals(currentExePath, appDataExePath, StringComparison.OrdinalIgnoreCase) && File.Exists(appDataExePath))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(appDataExePath);
                        if (isAdmin) psi.Verb = "runas";
                        psi.UseShellExecute = true;
                        try { System.Diagnostics.Process.Start(psi); } catch (Exception ex) { Log.Warn($"Autostart restart failed: {ex.Message}"); }
                        Application.Exit();
                        return;
                    }
                }
                else
                {
                    msg = AutostartHelper.Uninstall();
                    success = !AutostartHelper.IsAutostartTaskEnabled() && !AutostartHelper.IsAutostartShortcutEnabled();
                }
            } catch (Exception ex) {
                msg += $"Error while changing autostart: {ex.Message}\n";
                Log.Warn(msg);
            }
            isInitializing = true;
            autostartItemLocal.Checked = AutostartHelper.IsAutostartTaskEnabled() || AutostartHelper.IsAutostartShortcutEnabled();
            isInitializing = false;
            MessageBox.Show(msg, "Autostart", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Main monitoring loop: loads settings, checks sensors, and updates OLED display.
    /// </summary>
    static async Task RunMonitor(string[] args)
    {
        Log.Info("Starting CmpInf...");
        Settings? settings = await LoadSettings(SettingsFile);
        if (settings == null)
        {
            settings = Settings.GetDefault();
            File.WriteAllText(SettingsFile, Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented));
            Log.Warn($"settings.json was created automatically. Please adjust and restart the program.");
            return;
        }
        var desiredMode = DetermineHardwareAccessMode(settings);
        var hardwareReader = new HardwareReader(desiredMode);
        CheckConfiguredSensorsExist(hardwareReader, settings);
        if (settings.Pages.Count == 0 || settings.Pages.All(p => p.Sensors.Count == 0))
        {
            Log.Warn("No pages or sensors configured in settings.json.");
            return;
        }
        var gameSenseClient = new GameSenseClient("CMPINF", "CmpInf (using LibreHardwareMonitorLib)");
        gameSenseClient.SetRetryInterval(settings.GameSenseRetryIntervalMs);
        gameSenseClient.SetHeartbeatInterval(settings.GameSenseHeartbeatIntervalMs);
        await gameSenseClient.RegisterGameMetadataAsync();
        var pages = settings.Pages;
        for (int i = 0; i < pages.Count; i++)
        {
            var keyInstanceCounter = new Dictionary<string, int>();
            for (int j = 0; j < pages[i].Sensors.Count; j++)
            {
                var sensor = pages[i].Sensors[j];
                var baseKey = sensor.GetNormalizedBaseKey();
                if (!keyInstanceCounter.ContainsKey(baseKey))
                    keyInstanceCounter[baseKey] = 1;
                else
                    keyInstanceCounter[baseKey]++;
                sensor.KeyInstance = keyInstanceCounter[baseKey];
            }
            if (pages[i].Sensors.Count > 2)
            {
                Log.Warn($"Page {i + 1}: More than 2 sensors defined, only the first 2 will be used.");
                pages[i].Sensors = pages[i].Sensors.Take(2).ToList();
            }
            while (pages[i].Sensors.Count < 2)
            {
                pages[i].Sensors.Add(new SensorSelection { Name = "", Hardware = "", Type = "", Prefix = "" });
            }
        }
        int page = 0;
        var lastPageSwitch = DateTime.UtcNow;
        bool firstLoop = true;
        string[] eventNames = Enumerable.Range(0, pages.Count).Select(i => $"OLED_{i+1}").ToArray();
        var registerTasks = new List<Task>();
        for (int i = 0; i < pages.Count; i++)
        {
            registerTasks.Add(gameSenseClient.RegisterOledEventAsync(eventNames[i], pages[i].IconId));
        }
        await Task.WhenAll(registerTasks);
        while (true)
        {
            if ((DateTime.UtcNow - lastPageSwitch).TotalMilliseconds >= pages[page].DurationMs || firstLoop)
            {
                if (!firstLoop) {
                    page = (page + 1) % pages.Count;
                }
                lastPageSwitch = DateTime.UtcNow;
                firstLoop = false;
            }
            var pageSensors = pages[page].Sensors;
            var values = hardwareReader.GetSensorValues(pageSensors);
            int prefixWidth = pageSensors.Max(sel => (string.IsNullOrWhiteSpace(sel.Name) ? 0 : (string.IsNullOrWhiteSpace(sel.Prefix) ? sel.Name.Length : sel.Prefix.Length)));
            int valueWidth = pageSensors.Max(sel =>
            {
                if (string.IsNullOrWhiteSpace(sel.Name)) return 0;
                string format = $"F{sel.DecimalPlaces}";
                if (values.TryGetValue(sel.GetContextFrameKey(), out var val))
                    return val.ToString(format, System.Globalization.CultureInfo.InvariantCulture).Length;
                else
                    return 1; // "-"
            });
            var lines = pageSensors
                .Select(sel =>
                {
                    if (string.IsNullOrWhiteSpace(sel.Name)) return " ";
                    var prefix = string.IsNullOrWhiteSpace(sel.Prefix) ? sel.Name : sel.Prefix;
                    var suffix = sel.Suffix ?? string.Empty;
                    int decimals = sel.DecimalPlaces;
                    string format = $"F{decimals}";
                    string valueStr;
                    if (values.TryGetValue(sel.GetContextFrameKey(), out var val))
                    {
                        valueStr = val.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        valueStr = "-";
                        Log.Warn($"Sensor '{sel.Name}' could not be read. PawnIO not installed or the program may need to be run as administrator.");
                    }
                    string line = $"{prefix.PadRight(prefixWidth)}{valueStr.PadLeft(valueWidth)}{suffix}";
                    return line.Length > 20 ? line.Substring(0, 20) : line;
                })
                .ToArray();
            Log.Debug($"Page {page + 1}/{pages.Count}: {string.Join(", ", lines)}");
            await gameSenseClient.SendOledDisplayAsync(eventNames[page], lines);
            await Task.Delay(settings.UpdateIntervalMs);
        }
    }

    /// <summary>
    /// Loads settings from file, returns null if invalid or missing.
    /// </summary>
    static async Task<Settings?> LoadSettings(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(json);
            if (settings == null || settings.Pages == null)
            {
                Log.Warn("[ERROR] settings.json is invalid or empty.");
                return null;
            }
            return settings;
        }
        catch (Exception ex)
        {
            Log.Warn($"[ERROR] settings.json could not be loaded: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if all configured sensors exist in hardwareReader. Logs warnings for missing sensors.
    /// </summary>
    static void CheckConfiguredSensorsExist(HardwareReader hardwareReader, Settings settings)
    {
        var available = hardwareReader.AllSensors;
        var missing = new List<SensorSelection>();
        foreach (var page in settings.Pages)
        {
            foreach (var sel in page.Sensors)
            {
                if (string.IsNullOrWhiteSpace(sel.Name)) continue;
                bool exists = available.Any(s =>
                    string.Equals(s.Name, sel.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Hardware, sel.Hardware, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Type, sel.Type, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    missing.Add(sel);
                }
            }
        }
        foreach (var sel in missing)
        {
            Log.Warn($"Sensor not found: Name='{sel.Name}', Hardware='{sel.Hardware}', Type='{sel.Type}'");
        }
    }

    /// <summary>
    /// Returns true if running as administrator.
    /// </summary>
    static bool IsRunAsAdmin()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Restarts the application as administrator.
    /// </summary>
    static void RestartAsAdmin()
    {
        string? exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exeName))
        {
            Log.Warn("Could not determine executable path for restart as admin.");
            return;
        }
        var startInfo = new System.Diagnostics.ProcessStartInfo(exeName)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        try { System.Diagnostics.Process.Start(startInfo); } catch (Exception ex) { Log.Warn($"Restart as admin failed: {ex.Message}"); }
    }

    static HardwareAccessMode DetermineHardwareAccessMode(Settings settings)
    {
        var mode = settings.HardwareAccessMode;
        if (mode == HardwareAccessMode.Full && !HardwareReader.IsPawnIoInstalled())
        {
            Log.Warn("PawnIO is not installed; falling back to SafeUserMode.");
            return HardwareAccessMode.SafeUserMode;
        }
        return mode;
    }

    static void GenerateAvailableSensors(string path, HardwareAccessMode desiredMode, string context)
    {
        var reader = new HardwareReader(desiredMode);
        reader.ExportSensors(path);
    }
}
