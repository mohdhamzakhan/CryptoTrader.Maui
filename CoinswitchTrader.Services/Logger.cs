using CoinswitchTrader.Services;
using Org.BouncyCastle.Asn1.Tsp;
using System.Diagnostics;

public static class Logger
{
    static readonly string logFilePath = Path.Combine(FileSystem.AppDataDirectory, "trading_log.txt");
    private static readonly SettingsService _settingsService = new SettingsService();
    public static void Log(string message)
    {
        if (Convert.ToBoolean(_settingsService.LoggingEnabled))
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
    }

    public static async Task ShareLogFileAsync()
    {
        try
        {
            var logPath = Logger.GetLogFilePath();

            if (File.Exists(logPath))
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Share Trading Log",
                    File = new ShareFile(logPath)
                });
            }
            else
            {
                Debug.WriteLine("[Logger] Log file does not exist to share.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Logger] Failed to share log file: {ex.Message}");
        }
    }

    public static void Delete()
    {
        try
        {
            File.Delete(logFilePath);
            File.AppendAllText(logFilePath, "File was deleted by User" + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Logger] Failed to write to log file: {ex.Message}");
        }
    }

    public static string GetLogFilePath() => logFilePath;
}
