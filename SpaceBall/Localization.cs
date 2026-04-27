using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SpaceDNA
{
    public enum Language
    {
        Ru,
        En
    }

    /// <summary>
    /// Minimal localization for UI strings. Falls back to key when missing.
    /// </summary>
    public sealed class Localization
    {
        private readonly Dictionary<string, string> _ru = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _en = new(StringComparer.Ordinal);

        public Language Current { get; private set; } = Language.Ru;

        public void SetLanguage(Language lang) => Current = lang;

        public void LoadFromFiles(string ruPath, string enPath)
        {
            LoadInto(_ru, ruPath);
            LoadInto(_en, enPath);
        }

        public string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var dict = Current == Language.Ru ? _ru : _en;
            return dict.TryGetValue(key, out var s) ? s : key;
        }

        public string F(string key, params object[] args)
        {
            try
            {
                return string.Format(T(key), args);
            }
            catch
            {
                return T(key);
            }
        }

        private static void LoadInto(Dictionary<string, string> dst, string path)
        {
            dst.Clear();
            try
            {
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (map == null) return;
                foreach (var kv in map)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                        dst[kv.Key] = kv.Value ?? "";
                }
            }
            catch
            {
                // Intentionally ignore localization load errors
            }
        }
    }
}

