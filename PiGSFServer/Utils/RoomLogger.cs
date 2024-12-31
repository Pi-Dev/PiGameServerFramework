using PiGSF.Server;
using System.Collections.Concurrent;

public class RoomLogger
{
    private readonly Room _room;
    internal readonly ConcurrentQueue<string> messages = new();
    private const int maxLogLines = 1000;
    private readonly string _logFilePath;
    internal LogWindow? logWindow;

    public RoomLogger(Room room)
    {
        _room = room ?? throw new ArgumentNullException(nameof(room));

        // Generate log file path
        var logsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsFolder); // Ensure the logs folder exists

        var logFileName = $"{_room.Id}-{_room.GetType().Name}-{DateTime.Now:yyyyMMddHHmmss}.log";
        _logFilePath = Path.Combine(logsFolder, logFileName);

        // Write initial log file header
        File.AppendAllText(_logFilePath, $"--- Log started for Room {_room.Id} ({_room.GetType().Name}) at {DateTime.Now} ---\n");
    }

    public void Write(string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
        lock (messages)
        {
            messages.Enqueue(logEntry);
        }

        // Write to the log file
        //File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);

        // Update the log window, if any
        //logWindow?.RefreshLogs();
    }
}
