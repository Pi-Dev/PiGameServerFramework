using PiGSF.Server;
using System.Text;

public class ConsoleWriteHandler: TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        ServerLogger.Log("ConsoleWriteHandler cannot handle this write, please debug");
    }

    public override void Write(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        ServerLogger.Log(value);
    }
}

public static class ServerLogger
{
    internal static readonly List<string> messages = new();
    private const int maxLogLines = 1000;
    private static readonly string _logFilePath;
    internal static ServerLogView? logWindow;

    static ServerLogger()
    {
        // Generate log file path
        var logsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsFolder); // Ensure the logs folder exists

        var logFileName = $"SERVER-{DateTime.Now:yyyyMMddHHmmss}.log";
        _logFilePath = Path.Combine(logsFolder, logFileName);

        // Write initial log file header
        File.AppendAllText(_logFilePath, $"--- Server Framework started at {DateTime.Now} ---\n");
    }

    public static void Log(string message)
    {
        lock (messages)
        {
            // Add timestamped log entry
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}│ {message}";
            messages.Add(logEntry);

            // Remove oldest logs if exceeding the limit
            if (messages.Count > maxLogLines)
                messages.RemoveAt(0);

            // Write to the log file
            File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);

            // Update the log window, if any
            //logWindow?.RefreshLogs();
        }
    }
}
