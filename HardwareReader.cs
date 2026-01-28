using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using PawnIoDriver = LibreHardwareMonitor.PawnIo.PawnIo;
using Newtonsoft.Json;

public class HardwareReader
{
    private readonly Computer _computer;
    private readonly HardwareAccessMode _accessMode;
    private bool _initialized;
    private bool _initializationWarningLogged;
    public List<(string Name, string Hardware, string Type)> AllSensors { get; } = new();

    public bool IsInitialized => _initialized;

    public static void LogPawnIoStatus()
    {
        var (installed, version, error) = GetPawnIoState();
        Log.Info($"PawnIO status — installed: {installed}, version: {version}");
        if (!string.IsNullOrEmpty(error))
        {
            Log.Warn($"PawnIO status could not be determined: {error}");
        }
        if (!installed)
        {
            Log.Warn("PawnIO must be installed separately for motherboard/CPU sensors; those sensors may be unavailable until it is installed.");
        }
    }

    public static bool IsPawnIoInstalled() => GetPawnIoState().Installed;

    public static string GetPawnIoVersion() => GetPawnIoState().Version;

    private static (bool Installed, string Version, string? Error) GetPawnIoState()
    {
        try
        {
            return (PawnIoDriver.IsInstalled, PawnIoDriver.Version?.ToString() ?? "unknown", null);
        }
        catch (Exception ex)
        {
            return (false, "unknown", ex.Message);
        }
    }

    public HardwareReader()
        : this(HardwareAccessMode.Full)
    {
    }

    public HardwareReader(HardwareAccessMode accessMode)
    {
        _accessMode = accessMode;
        _computer = new Computer
        {
            IsCpuEnabled = accessMode == HardwareAccessMode.Full,
            IsGpuEnabled = true,
            IsMotherboardEnabled = accessMode == HardwareAccessMode.Full,
            IsMemoryEnabled = true,
            IsControllerEnabled = accessMode == HardwareAccessMode.Full,
            IsNetworkEnabled = true,
            IsStorageEnabled = true
        };

        if (_accessMode == HardwareAccessMode.SafeUserMode)
            Log.Info("Hardware access mode: SafeUserMode (kernel/board sensors disabled; PawnIO not required).");
        else
            Log.Info("Hardware access mode: Full (PawnIO is required for motherboard/CPU sensor coverage).");

        LogPawnIoStatus();

        try
        {
            _computer.Open();
            ScanAllSensors();
            _initialized = true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Hardware initialization failed ({_accessMode}): {ex.Message}");
        }
    }

    private void ScanAllSensors()
    {
        AllSensors.Clear();
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                AllSensors.Add((sensor.Name, hardware.HardwareType.ToString(), sensor.SensorType.ToString()));
            }
        }
    }

    public void ExportSensors(string path)
    {
        if (!_initialized)
        {
            Log.Warn("available-sensors.json export skipped: hardware initialization failed.");
            File.WriteAllText(path, JsonConvert.SerializeObject(Array.Empty<object>(), Formatting.Indented));
            return;
        }

        var export = AllSensors.Select(s => new { Name = s.Name, Hardware = s.Hardware, Type = s.Type }).ToList();
        File.WriteAllText(path, JsonConvert.SerializeObject(export, Formatting.Indented));
    }

    public Dictionary<string, float> GetSensorValues(List<SensorSelection> selected)
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (!_initialized)
        {
            if (!_initializationWarningLogged)
            {
                Log.Warn("Skipping sensor polling because hardware initialization failed.");
                _initializationWarningLogged = true;
            }
            return result;
        }

        foreach (var hardware in _computer.Hardware)
        {
            UpdateRecursive(hardware);
        }
        foreach (var sel in selected)
        {
            foreach (var hardware in _computer.Hardware)
            {
                var key = sel.GetContextFrameKey();
                if (result.ContainsKey(key)) continue;
                try
                {
                    FindSensorRecursive(hardware, sel, result);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Sensor \"{sel.Name}\" could not be read. PawnIO not installed or the program may need to be run as administrator. Error: {ex.Message}");
                }
            }
        }
        return result;
    }

    private void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    private void FindSensorRecursive(IHardware hardware, SensorSelection sel, Dictionary<string, float> result)
    {
        var key = sel.GetContextFrameKey();
        if (result.ContainsKey(key))
            return;
        foreach (var sensor in hardware.Sensors)
        {
            if (string.Equals(sensor.Name, sel.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(hardware.HardwareType.ToString(), sel.Hardware, StringComparison.OrdinalIgnoreCase)
                && string.Equals(sensor.SensorType.ToString(), sel.Type, StringComparison.OrdinalIgnoreCase)
                && sensor.Value.HasValue)
            {
                result[key] = sensor.Value.Value;
                return;
            }
        }
        foreach (var sub in hardware.SubHardware)
        {
            if (result.ContainsKey(key)) return;
            FindSensorRecursive(sub, sel, result);
        }
    }
}
