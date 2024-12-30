using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Terminal.Gui;

namespace PiGSF.Server.TUI
{
    // Custom TextWriter that writes to a TextView with an internal buffer
    public class TextViewWriter : TextWriter
    {
        private readonly TextView _textView;
        private readonly List<string> _logBuffer; // Store log lines
        private readonly int _maxLogLines; // Maximum number of lines to keep in buffer

        public TextViewWriter(TextView textView, int maxLogLines = 1000)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _logBuffer = new List<string>();
            _maxLogLines = maxLogLines;
            _textView.ContentSizeChanged += (s, e) => UpdateTextView();
        }

        public override void Write(char value)
        {
            Write(value.ToString());
        }

        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            lock (_logBuffer)
            {
                // Split the incoming value into lines and add to the buffer
                var lines = value.Replace("\r\n","\n").Split("\n", StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    _logBuffer.Add(line.TrimEnd());

                    // Remove excess lines if buffer exceeds the maximum
                    if (_logBuffer.Count > _maxLogLines)
                    {
                        _logBuffer.RemoveAt(0); // Remove oldest line
                    }
                }

                // Update the TextView with the visible lines
                UpdateTextView();
            }
        }

        private void UpdateTextView()
        {
            Application.Invoke(() =>
            {
                lock (_logBuffer)
                {
                    // Calculate the number of lines to display based on TextView's frame height
                    int linesToDisplay = _textView.Frame.Height;

                    // Get the last 'linesToDisplay' lines from the buffer
                    var visibleLines = new List<string>();
                    for (int i = Math.Max(0, _logBuffer.Count - linesToDisplay); i < _logBuffer.Count; i++)
                    {
                        visibleLines.Add(_logBuffer[i]);
                    }

                    // Set the TextView text to the visible lines
                    _textView.Text = string.Join("\n", visibleLines);
                    _textView.CursorPosition = new Point(0, _textView.Lines);
                }
            });
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
