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

    static object currentOutputChannel = lastMessagesBuffer;
    public static void SetOutputToServer()
    {
        currentOutputChannel = lastMessagesBuffer;
        RenderCurrentChannel();
    }
    public static void SetOutputToRoom(Room room)
    {
        currentOutputChannel = room.Log.roomBuffer;
        RenderCurrentChannel();
    }
    static object renderLocker = new object();
    static void RenderCurrentChannel()
    {
        lock (renderLocker)
        {
            Console.Clear();
            if (currentOutputChannel is List<string> ss)
                foreach (var s in ss)
                    Console.Write(s);
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
                            io.rl.roomBuffer.Add(io.msg);
                            if (currentOutputChannel == io.rl.roomBuffer) lock (Console.Out) Console.Write(io.msg);
                        }
                        else
                        {
                            lastMessagesBuffer.Add(io.msg);
                            if (currentOutputChannel == lastMessagesBuffer) lock (Console.Out) Console.Write(io.msg);
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

    volatile static bool mustExit = false;
    internal static void Stop()
    {
        mustExit = true;
        loggerThread.Interrupt();
    }
}
