public static class Log
{
    public static bool Verbose { get; set; } = false;
    public static event Action<string, string>? OnLog;
    public static void Debug(string msg)
    {
        if (Verbose)
        {
            var line = $"[DEBUG] {msg}";
            try { Console.WriteLine(line); } catch { /* ignore */ }
            var singleLine = line.Replace("\r\n", "").Replace("\n", "").Replace("\r", "");
            OnLog?.Invoke("DEBUG", singleLine);
        }
    }
    public static void Info(string msg)
    {
        var line = $"[INFO] {msg}";
        try { Console.WriteLine(line); } catch { /* ignore */ }
        OnLog?.Invoke("INFO", line);
    }
    public static void Warn(string msg)
    {
        var line = $"[WARN] {msg}";
        try { Console.WriteLine(line); } catch { /* ignore */ }
        OnLog?.Invoke("WARN", line);
    }
}
