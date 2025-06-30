using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class GameSenseClient
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly string _developer = "TBSniller";
    private readonly string _gameName;
    private readonly string _gameDisplayName;
    private string _gameSenseAddress;
    private int _retryIntervalMs = 5000;
    private readonly SemaphoreSlim _addressLock = new(1, 1);
    private DateTime _lastSendOrHeartbeat = DateTime.MinValue;
    private int _heartbeatIntervalMs = 10000;

    public string Developer => _developer;

    public GameSenseClient(string gameName, string gameDisplayName)
    {
        _gameName = gameName;
        _gameDisplayName = gameDisplayName;
        _gameSenseAddress = GetGameSenseAddress();
    }

    public void SetRetryInterval(int ms)
    {
        _retryIntervalMs = ms > 0 ? ms : 5000;
    }

    public void SetHeartbeatInterval(int ms)
    {
        _heartbeatIntervalMs = ms > 0 ? ms : 10000;
    }

    private async Task<string> GetOrUpdateGameSenseAddressAsync()
    {
        await _addressLock.WaitAsync();
        try
        {
            // Always reload in case the port changed or connection failed
            _gameSenseAddress = GetGameSenseAddressSafe();
            return _gameSenseAddress;
        }
        finally
        {
            _addressLock.Release();
        }
    }

    private async Task<T> RetryGameSenseCall<T>(Func<string, Task<T>> call, string operation)
    {
        Exception? lastEx = null;
        while (true)
        {
            try
            {
                var address = await GetOrUpdateGameSenseAddressAsync();
                Log.Debug($"[HTTP] {operation} using {address}");
                return await call(address);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Log.Warn($"GameSense {operation} failed: {ex.Message}. Retrying in {_retryIntervalMs / 1000.0:F1}s ...");
                await Task.Delay(_retryIntervalMs);
            }
        }
    }

    private string GetCorePropsPath()
    {
        string programData = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData";
        string[] possiblePaths = new[] {
            Path.Combine(programData, "SteelSeries", "GG", "coreProps.json"),
            Path.Combine(programData, "SteelSeries", "SteelSeries Engine 3", "coreProps.json")
        };
        return possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths.Last();
    }

    private string GetGameSenseAddressSafe()
    {
        try
        {
            return GetGameSenseAddress();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetGameSenseAddress()
    {
        string programData = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData";
        string[] possiblePaths = new[] {
            Path.Combine(programData, "SteelSeries", "GG", "coreProps.json"),
            Path.Combine(programData, "SteelSeries", "SteelSeries Engine 3", "coreProps.json")
        };
        string configPath = possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths.Last();
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"GameSense coreProps.json not found at {configPath}");
        }
        string json = File.ReadAllText(configPath);
        dynamic? config = JsonConvert.DeserializeObject(json);
        string? address = config?.address;
        if (string.IsNullOrEmpty(address))
        {
            throw new Exception("Could not read GameSense address from coreProps.json");
        }
        // If address does not start with http, prepend it
        if (!address.StartsWith("http://") && !address.StartsWith("https://"))
        {
            address = "http://" + address;
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new Exception($"GameSense address in coreProps.json is not a valid absolute URI: {address}");
        }
        return address.TrimEnd('/');
    }

    public async Task RegisterOledEventAsync(string eventName, int iconId)
    {
        await RetryGameSenseCall(async (baseAddress) => {
            var handlerLines = new[] {
                new Dictionary<string, object> { { "has-text", true }, { "context-frame-key", "line1" } },
                new Dictionary<string, object> { { "has-text", true }, { "context-frame-key", "line2" } }
            };
            var handler = new Dictionary<string, object>
            {
                { "device-type", "screened" },
                { "zone", "one" },
                { "mode", "screen" },
                { "datas", new[]
                    {
                        new Dictionary<string, object>
                        {
                            { "lines", handlerLines },
                            { "icon-id", iconId }
                        }
                    }
                }
            };
            var bindPayload = new Dictionary<string, object>
            {
                { "game", _gameName },
                { "event", eventName },
                { "icon_id", iconId },
                { "value_optional", true },
                { "handlers", new[] { handler } }
            };
            var bindContent = new StringContent(JsonConvert.SerializeObject(bindPayload), Encoding.UTF8, "application/json");
            Log.Debug($"[HTTP] POST {baseAddress}/bind_game_event\n{JsonConvert.SerializeObject(bindPayload, Formatting.Indented)}");
            var resp = await _httpClient.PostAsync($"{baseAddress}/bind_game_event", bindContent);
            var respBody = await resp.Content.ReadAsStringAsync();
            Log.Debug($"[HTTP] Response: {resp.StatusCode}\n{respBody}");
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"bind_game_event: {resp.StatusCode} {respBody}");
            }
            return 0;
        }, "RegisterOledEventAsync");
    }

    public async Task SendOledDisplayAsync(string eventName, string[] lines)
    {
        await SendHeartbeatIfNeededAsync();
        await RetryGameSenseCall(async (baseAddress) => {
            var frame = new Dictionary<string, object>
            {
                ["line1"] = lines.Length > 0 ? lines[0] : " ",
                ["line2"] = lines.Length > 1 ? lines[1] : " "
            };
            var payload = new
            {
                game = _gameName,
                @event = eventName,
                data = new { frame = frame, value = 0 }
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            Log.Debug($"[HTTP] POST {baseAddress}/game_event\n{JsonConvert.SerializeObject(payload, Formatting.Indented)}");
            var resp = await _httpClient.PostAsync($"{baseAddress}/game_event", content);
            var respBody = await resp.Content.ReadAsStringAsync();
            Log.Debug($"[HTTP] Response: {resp.StatusCode}\n{respBody}");
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"game_event: {resp.StatusCode} {respBody}");
            }
            _lastSendOrHeartbeat = DateTime.UtcNow;
            return 0;
        }, "SendOledDisplayAsync");
    }

    public async Task RegisterGameMetadataAsync()
    {
        await RetryGameSenseCall(async (baseAddress) => {
            var payload = new
            {
                game = _gameName,
                game_display_name = _gameDisplayName,
                developer = _developer
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            Log.Debug($"[HTTP] POST {baseAddress}/game_metadata\n{JsonConvert.SerializeObject(payload, Formatting.Indented)}");
            var resp = await _httpClient.PostAsync($"{baseAddress}/game_metadata", content);
            var respBody = await resp.Content.ReadAsStringAsync();
            Log.Debug($"[HTTP] Response: {resp.StatusCode}\n{respBody}");
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"game_metadata: {resp.StatusCode} {respBody}");
            }
            return 0;
        }, "RegisterGameMetadataAsync");
    }

    private async Task SendHeartbeatIfNeededAsync()
    {
        if ((DateTime.UtcNow - _lastSendOrHeartbeat).TotalMilliseconds < _heartbeatIntervalMs)
            return;
        await RetryGameSenseCall(async (baseAddress) => {
            var payload = new { game = _gameName };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            Log.Debug($"[HTTP] POST {baseAddress}/game_heartbeat\n{JsonConvert.SerializeObject(payload, Formatting.Indented)}");
            var resp = await _httpClient.PostAsync($"{baseAddress}/game_heartbeat", content);
            var respBody = await resp.Content.ReadAsStringAsync();
            Log.Debug($"[HTTP] Response: {resp.StatusCode}\n{respBody}");
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"game_heartbeat: {resp.StatusCode} {respBody}");
            }
            _lastSendOrHeartbeat = DateTime.UtcNow;
            return 0;
        }, "SendHeartbeatIfNeededAsync");
    }
}
