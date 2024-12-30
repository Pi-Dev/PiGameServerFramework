using System;
using System.Collections.Generic;
using Terminal.Gui;

public class RoomLogger 
{
    internal readonly List<string> messages = new();
    const int maxLogLines = 1000;
    internal LogWindow? logWindow;

    public void WriteLine(string message)
    {
        lock (messages)
        {
            // Add timestamped log entry
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            messages.Add(logEntry);

            // Remove oldest logs if exceeding the limit
            if (messages.Count > maxLogLines)
                messages.RemoveAt(0);

            logWindow?.RefreshLogs();
        }
    }

}
