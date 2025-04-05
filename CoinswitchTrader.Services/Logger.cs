using System.Diagnostics;

public static class Logger
{
    static readonly string logFilePath = Path.Combine(FileSystem.AppDataDirectory, "trading_log.txt");

    public static void Log(string message)
    {
        string timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        // Log to Logcat (or console on Windows)
        Debug.WriteLine(timestamped);

        // Append to file
        try
        {
            File.AppendAllText(logFilePath, timestamped + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Logger] Failed to write to log file: {ex.Message}");
        }
    }

    public static string GetLogFilePath() => logFilePath;
}
