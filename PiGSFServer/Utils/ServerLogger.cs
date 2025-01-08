using PiGSF.Server;
using System;
using System.Collections.Concurrent;
using System.Text;

public class ConsoleWriteHandler : TextWriter
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
    class LogEntry
    {
        internal DateTime ts;
        internal object sender;
        internal string message;
    }
    private static readonly Queue<LogEntry> messages = new();
    public static List<string> lastMessagesBuffer = new();

    private const int maxLogLines = 1000;
    private static readonly string _logFilePath;
    //internal static ServerLogView? logWindow;

    public static Room? currentRoomChannel { get; private set; }
    static object currentOutputChannel = lastMessagesBuffer;
    public static void SetOutputToServer()
    {
        currentRoomChannel = null;
        currentOutputChannel = lastMessagesBuffer;
        RenderCurrentChannel();
    }
    public static void SetOutputToRoom(Room room)
    {
        currentRoomChannel = room;
        currentOutputChannel = room.Log.roomBuffer;
        RenderCurrentChannel();
    }
    static object renderLocker = new object();
    static void RenderCurrentChannel()
    {
        var sb = new StringBuilder();
        if (currentOutputChannel is List<string> ss)
            lock (ss)
                foreach (var s in ss)
                    if (filter == "" || (filter != "" && s.ToLower().Contains(filter.ToLower())))
                        sb.Append(s);

        lock (renderLocker)
        {
			Console.ForegroundColor = ConsoleColor.Gray;
            Console.Clear();
            Console.Write(sb);
			WritePrompt();
        }
    }

    static Thread loggerThread;
    static ServerLogger()
    {
        // Generate log file path
        var logsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsFolder); // Ensure the logs folder exists

        var logFileName = $"SERVER-{DateTime.Now:yyyyMMddHHmmss}.log";
        _logFilePath = Path.Combine(logsFolder, logFileName);

        // Write initial log file header
        File.AppendAllText(_logFilePath, $"--- Server Framework started at {DateTime.Now} ---\n");

        loggerThread = new Thread(LoggerThread);
        loggerThread.Name = "Server Logger";
        loggerThread.Start();
    }

    static void LoggerThread()
    {
        try
        {
            while (true)
            {
                LogEntry[] msgs;
                lock (messages)
                {
                    if (messages.Count == 0) Monitor.Wait(messages, 500);
                    msgs = messages.ToArray();
                    messages.Clear();
                }
                foreach (var m in msgs)
                {
                    string fn, entry;
                    var IOs = new List<(string fn, string msg, RoomLogger? rl)>();
                    entry = $"{m.ts:yyyy-MM-dd HH:mm:ss}│ {m.message}";
                    if (m.sender is RoomLogger l)
                    {
                        var first = IOs.Where(x => x.fn == l._logFilePath).ToArray();
                        if (first.Length == 0) IOs.Add((l._logFilePath, m.message + "\n", l));
                        else first[0].msg += m.message + "\n";
                    }
                    else
                    {
                        var first = IOs.Where(x => x.fn == _logFilePath).ToArray();
                        if (first.Length == 0) IOs.Add((_logFilePath, m.message + "\n", null));
                        else first[0].msg += m.message + "\n";
                    }
                    foreach (var io in IOs)
                    {
                        File.AppendAllText(io.fn, io.msg);
                        if (io.rl != null)
                        {
                            lock (io.rl.roomBuffer) io.rl.roomBuffer.Add(io.msg);
                            if (filter == "" || (filter != "" && io.msg.ToLower().Contains(filter.ToLower())))
                                if (currentOutputChannel == io.rl.roomBuffer)
                                    WriteMessageToScreen(io.msg);
                        }
                        else
                        {
                            lock (lastMessagesBuffer) lastMessagesBuffer.Add(io.msg);
                            if (filter == "" || (filter != "" && io.msg.Contains(filter)))
                                if (currentOutputChannel == lastMessagesBuffer)
                                    WriteMessageToScreen(io.msg);
                        }
                    }
                }
            }
        }
        catch (ThreadInterruptedException)
        {
            if (mustExit) return;
        }
    }

    public static void LogRoom(RoomLogger l, string message)
    {
        lock (messages)
        {
            messages.Enqueue(new() { message = message, sender = l, ts = DateTime.Now });
            Monitor.Pulse(messages);
        }
    }

    public static void Log(string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}│ {message}";
        lock (messages)
        {
            messages.Enqueue(new() { message = logEntry, ts = DateTime.Now });
            Monitor.Pulse(messages);
        }
    }

	public static string prompt;
	public static string inputBuffer;
    public static void WriteMessageToScreen(string message)
    {
        lock (Console.Out)
        {
			Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("\r"+message.TrimEnd());
            WritePrompt();
        }
    }
    internal static void WritePrompt()
    {
        lock (Console.Out)
        {
			Console.ForegroundColor = ConsoleColor.White;
            Console.Write("\r"+"".PadLeft(80)+"\r"+prompt);
			Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(inputBuffer);
        }
    }

    volatile static bool mustExit = false;
    internal static void Stop()
    {
        mustExit = true;
        loggerThread.Interrupt();
        loggerThread.Join();
    }

    static string filter = "";
    public static void SetFilter(string s)
    {
        filter = s;
        RenderCurrentChannel();
    }
}
