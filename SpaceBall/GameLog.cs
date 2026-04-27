using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpaceDNA
{
    /// <summary>
    /// In-game log: in-memory history + file. Use for консоль (~) and debugging.
    /// </summary>
    public static class GameLog
    {
        private const int MaxLines = 2000;
        private static readonly List<string> _lines = new List<string>();
        private static readonly object _lock = new object();
        private static StreamWriter? _file;
        private static string _logFilePath = "spacedna.log";

        public static void SetLogPath(string path)
        {
            lock (_lock)
            {
                _logFilePath = path;
            }
        }

        private static void EnsureFile()
        {
            if (_file != null) return;
            try
            {
                string dir = Path.GetDirectoryName(_logFilePath) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                _file = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                _lines.Add($"[LOG] Failed to open log file: {ex.Message}");
            }
        }

        /// <summary>Add a line to the log (timestamped) and to the file.</summary>
        public static void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            lock (_lock)
            {
                _lines.Add(line);
                if (_lines.Count > MaxLines)
                    _lines.RemoveAt(0);
                EnsureFile();
                try
                {
                    _file?.WriteLine(line);
                }
                catch { /* ignore */ }
            }
            Console.WriteLine(message);
        }

        /// <summary>Log as error (same as Log but can be styled differently in UI).</summary>
        public static void LogError(string message)
        {
            Log($"[ERR] {message}");
        }

        /// <summary>Get a copy of log lines for display (e.g. ImGui).</summary>
        public static IReadOnlyList<string> GetLines()
        {
            lock (_lock)
            {
                return _lines.ToList();
            }
        }

        /// <summary>Clear in-memory history (file is not truncated).</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _lines.Clear();
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                try
                {
                    _file?.Dispose();
                }
                catch { }
                _file = null;
            }
        }
    }
}
