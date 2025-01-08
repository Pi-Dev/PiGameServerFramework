using System;
using System.Collections.Generic;
using System.Threading;

class Program
{
    static void Main()
    {
        List<string> messages = new List<string>(); // Chat messages
        string inputBuffer = string.Empty;          // Input buffer
        bool exit = false;


        // Background thread for rendering messages
        new Thread(() =>
        {
            var r = new Random();
            string[] dummyMessages = {
                        "User joined the chat: Hello!",
                        "Bot is typing...: Error 404.",
                        "Developer left the chat: OMG!",
                        "User mentioned you: Hello!",
                        "User edited a message: This is awesome!"
                    };
            while (!exit)
            {

                lock (messages)
                {

                    // Re-render the input prompt after displaying messages
                    Console.WriteLine("\r" + dummyMessages[r.Next(dummyMessages.Length)].PadRight(7 + inputBuffer.Length));
                    
                    Console.CursorVisible = false;
                    Console.Write($"Input: {inputBuffer}");
                    Console.CursorVisible = true;

                }

                Thread.Sleep(16); // Small delay for smooth rendering
            }
        })
        { IsBackground = true }.Start();

        // Main thread handles user input
        while (!exit)
        {
            var key = Console.ReadKey(intercept: false);

            lock (messages)
            {
                if (key.Key == ConsoleKey.Enter)
                {
                    if (inputBuffer.ToLower() == "exit")
                    {
                        exit = true; // Exit condition
                    }
                    else
                    {
                        // Add input as a new message
                        Console.CursorVisible = false;
                        Console.Write($"\rInput: ".PadLeft(Console.WindowWidth));
                        Console.CursorVisible = true;

                        messages.Add($"You: {inputBuffer}");
                        inputBuffer = string.Empty; // Clear input buffer
                    }
                }
                else if (key.Key == ConsoleKey.Backspace && inputBuffer.Length > 0)
                {
                    // Handle backspace
                    inputBuffer = inputBuffer.Substring(0, inputBuffer.Length - 1);
                }
                else if (key.Key != ConsoleKey.Backspace)
                {
                    // Append typed character to input buffer
                    inputBuffer += key.KeyChar;
                }

                // Redraw the prompt after every key press
                Console.Write($"\rInput: {inputBuffer} ");
            }
        }
    }
}
