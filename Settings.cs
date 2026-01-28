using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

public class Settings
{
    [JsonProperty("UpdateIntervalMs")]
    public int UpdateIntervalMs { get; set; } = 1000;
    [JsonProperty("Pages")]
    public List<OledPage> Pages { get; set; } = new();
    [JsonProperty("GameSenseRetryIntervalMs")]
    public int GameSenseRetryIntervalMs { get; set; } = 5000; // Default: 5 seconds
    [JsonProperty("GameSenseHeartbeatIntervalMs")]
    public int GameSenseHeartbeatIntervalMs { get; set; } = 10000; // Default: 10 seconds
    [JsonProperty("RunAsAdmin")]
    public bool RunAsAdmin { get; set; } = false;
    [JsonProperty("HardwareAccessMode")]
    [JsonConverter(typeof(StringEnumConverter))]
    public HardwareAccessMode HardwareAccessMode { get; set; } = HardwareAccessMode.Full;
    [JsonProperty("ShowCapsLockIndicator")]
    public bool ShowCapsLockIndicator { get; set; } = true;
    [JsonProperty("CapsLockIndicatorTextLine1")]
    public string CapsLockIndicatorTextLine1 { get; set; } = "⇪ ";
    [JsonProperty("CapsLockIndicatorTextLine2")]
    public string CapsLockIndicatorTextLine2 { get; set; } = "  ";

    /// Returns a default Settings object with three example pages.
    public static Settings GetDefault()
    {
        return new Settings
        {
            UpdateIntervalMs = 1000,
            GameSenseRetryIntervalMs = 5000,
            GameSenseHeartbeatIntervalMs = 10000,
            HardwareAccessMode = HardwareAccessMode.Full,
            ShowCapsLockIndicator = true,
            CapsLockIndicatorTextLine1 = "⇪ ",
            CapsLockIndicatorTextLine2 = "  ",
            Pages = new List<OledPage>
            {
                new OledPage
                {
                    DurationMs = 5000,
                    IconId = 43,
                    Sensors = new List<SensorSelection>
                    {
                        new SensorSelection { Name = "CPU Package", Hardware = "Cpu", Type = "Temperature", Prefix = "CPU: ", Suffix = " °C", DecimalPlaces = 0 },
                        new SensorSelection { Name = "GPU Core", Hardware = "GpuNvidia", Type = "Temperature", Prefix = "GPU: ", Suffix = " °C", DecimalPlaces = 0 }
                    }
                },
                new OledPage
                {
                    DurationMs = 3000,
                    IconId = 27,
                    Sensors = new List<SensorSelection>
                    {
                        new SensorSelection { Name = "CPU Total", Hardware = "Cpu", Type = "Load", Prefix = "CPU: ", Suffix = "%", DecimalPlaces = 0 },
                        new SensorSelection { Name = "GPU Core", Hardware = "GpuNvidia", Type = "Load", Prefix = "GPU: ", Suffix = "%", DecimalPlaces = 0 }
                    }
                },
                new OledPage
                {
                    DurationMs = 3000,
                    IconId = 29,
                    Sensors = new List<SensorSelection>
                    {
                        new SensorSelection { Name = "Memory Used", Hardware = "Memory", Type = "Data", Prefix = "Mem: ", Suffix = "GB", DecimalPlaces = 1 }
                    }
                }
            }
        };
    }
}

public class OledPage
{
    [JsonProperty("DurationMs")]
    public int DurationMs { get; set; } = 5000;
    [JsonProperty("IconId")]
    public int IconId { get; set; } = 0;
    [JsonProperty("Sensors")]
    public List<SensorSelection> Sensors { get; set; } = new();
}

public class SensorSelection
{
    [JsonProperty("Name")]
    public string Name { get; set; } = string.Empty;
    [JsonProperty("Hardware")]
    public string Hardware { get; set; } = string.Empty;
    [JsonProperty("Type")]
    public string Type { get; set; } = string.Empty;
    [JsonProperty("Prefix")]
    public string Prefix { get; set; } = string.Empty;
    [JsonProperty("Suffix")]
    public string Suffix { get; set; } = string.Empty;
    [JsonProperty("DecimalPlaces")]
    public int DecimalPlaces { get; set; } = 1;

    // Set during paging to ensure unique keys for duplicate sensors
    public int KeyInstance { get; set; } = 1;

    private string NormalizeBaseKey()
    {
        if (string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Hardware) && string.IsNullOrWhiteSpace(Type))
            return "dummy";
        return $"{Name}_{Hardware}_{Type}".ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
    }

    public string GetNormalizedBaseKey() => NormalizeBaseKey();

    public string GetContextFrameKey()
    {
        var baseKey = NormalizeBaseKey();
        if (baseKey == "dummy")
            return $"dummy_{KeyInstance}";
        return KeyInstance > 1 ? $"{baseKey}_{KeyInstance}" : baseKey;
    }
}
