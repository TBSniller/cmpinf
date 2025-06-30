using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

public static class AutostartHelper
{
    private static string AppName => "CmpInf_SteelSeriesOledPcInfo";
    private static string ExeName => "CmpInf_SteelSeriesOledPcInfo.exe";
    private static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    private static string TaskName => "CmpInf_SteelSeriesOledPcInfo_Autostart";
    private static string StartupFolder => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private static string ShortcutPath => Path.Combine(StartupFolder, $"{AppName}.lnk");

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);
    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    public static bool IsAutostartTaskEnabled()
    {
        var arguments = $"/Query /TN \"{TaskName}\"";
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool IsAutostartShortcutEnabled()
    {
        return !string.IsNullOrEmpty(ShortcutPath) && File.Exists(ShortcutPath);
    }

    public static string InstallToAppDataAndEnableAutostart(bool runAsAdmin)
    {
        string exeDst = Path.Combine(AppDataDir, ExeName);
        string msg;
        if (runAsAdmin)
        {
            DeleteShortcut();
            if (!string.IsNullOrEmpty(exeDst) && File.Exists(exeDst))
                CreateScheduledTask(exeDst);
            msg =
                "Autostart enabled with Windows Task Scheduler for the current user (admin mode).";
        }
        else
        {
            DeleteScheduledTask();
            CreateShortcut(exeDst);
            msg =
                "Autostart enabled with shortcut in autorun folder (user mode).";
        }
        return msg;
    }

    public static string Uninstall()
    {
        var msg = new System.Text.StringBuilder();
        DeleteScheduledTask();
        msg.AppendLine("Autostart task removed (if existed and in admin mode).");
        DeleteShortcut();
        msg.AppendLine("Autostart shortcut in autorun folder removed (if existed and in user mode).");
        try
        {
            if (!string.IsNullOrEmpty(AppDataDir) && Directory.Exists(AppDataDir))
            {
                Directory.Delete(AppDataDir, true);
                msg.AppendLine("Installed files in AppData removed.");
            }
        }
        catch (Exception)
        {
            // Delete on reboot fallback
            try
            {
                if (!string.IsNullOrEmpty(AppDataDir))
                {
                    foreach (var file in Directory.GetFiles(AppDataDir, "*", SearchOption.AllDirectories))
                    {
                        MoveFileEx(file, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    }
                    foreach (var dir in Directory.GetDirectories(AppDataDir, "*", SearchOption.AllDirectories).Reverse())
                    {
                        MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    }
                    MoveFileEx(AppDataDir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    msg.AppendLine("Installed AppData folder and contents scheduled for deletion on next reboot.");
                }
            }
            catch (Exception ex2)
            {
                msg.AppendLine($"Could not schedule deletion for installed AppData folder: {ex2.Message}");
            }
        }
        return msg.ToString();
    }

    private static void CreateScheduledTask(string exePath)
    {
        DeleteScheduledTask();
        string user = Environment.UserName;
        var arguments = $"/Create /F /RL HIGHEST /SC ONLOGON /TN \"{TaskName}\" /TR \"{exePath}\" /RU \"{user}\"";
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc != null) proc.WaitForExit();
    }

    private static void DeleteScheduledTask()
    {
        var arguments = $"/Delete /F /TN \"{TaskName}\"";
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc != null) proc.WaitForExit();
    }

    private static void CreateShortcut(string exePath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        try
        {
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.WindowStyle = 1;
            shortcut.Description = "CmpInf - SteelSeriesOLEDPCInfo";
            shortcut.IconLocation = exePath;
            shortcut.Save();
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to create autostart shortcut: {ex.Message}");
        }
    }

    private static void DeleteShortcut()
    {
        if (!string.IsNullOrEmpty(ShortcutPath) && File.Exists(ShortcutPath))
            File.Delete(ShortcutPath);
    }
}
