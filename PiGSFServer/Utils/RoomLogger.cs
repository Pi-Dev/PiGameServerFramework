using PiGSF.Server;
using System.Collections.Concurrent;

public class RoomLogger
{
    private readonly Room _room;
    private readonly Queue<string> messages = new();
    internal readonly List<string> roomBuffer = new();
    private const int maxLogLines = 1000;
    internal readonly string _logFilePath;

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
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}│ {message}";
        ServerLogger.LogRoom(this, logEntry); 
    }
}
