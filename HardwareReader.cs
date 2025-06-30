using System;
using System.Collections.Generic;
using System.IO;
using LibreHardwareMonitor.Hardware;
using Newtonsoft.Json;

public class HardwareReader
{
    private readonly Computer _computer;
    public List<(string Name, string Hardware, string Type)> AllSensors { get; } = new();

    public HardwareReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true
        };
        _computer.Open();
        ScanAllSensors();
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
        var export = AllSensors.Select(s => new { Name = s.Name, Hardware = s.Hardware, Type = s.Type }).ToList();
        File.WriteAllText(path, JsonConvert.SerializeObject(export, Formatting.Indented));
    }

    public Dictionary<string, float> GetSensorValues(List<SensorSelection> selected)
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        // Update hardware only once per tick
        foreach (var hardware in _computer.Hardware)
        {
            UpdateRecursive(hardware);
        }
        // Only search for relevant sensors for performance
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
                    Log.Warn($"Sensor '{sel.Name}' could not be read. The program may need to be run as administrator. Error: {ex.Message}");
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
