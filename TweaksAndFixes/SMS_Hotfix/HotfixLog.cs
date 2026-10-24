using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;

namespace TAFHotfixBatch1
{
    /// <summary>
    /// 统一日志：MelonLoader 控制台 + 追加式 UTF-8 文件（best-effort，写入失败绝不影响游戏）。
    /// </summary>
    internal static class HotfixLog
    {
        private const string FileName = "TAFHotfix-Batch1.log";
        private static readonly List<string> _pending = new List<string>();
        private static string _path;
        private static bool _ready;

        internal static void Init()
        {
            try
            {
                string root = AppContext.BaseDirectory;
                _path = Path.Combine(root, "Logs", FileName);
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _ready = true;
            }
            catch { _ready = false; }
        }

        internal static void Info(string msg) { Write("INFO ", msg); }
        /// <summary>默认静默；只有 `verbose=1` 时才输出（用于高频/已研究清楚的过程日志）。</summary>
        internal static void Debug(string msg) { if (HotfixParams.Verbose) Write("DEBUG", msg); }
        private static readonly System.Collections.Generic.HashSet<string> _once =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>同一 key 只打印一次（跨读档清空由 ResetOnce 控制）。</summary>
        internal static void InfoOnce(string key, string msg)
        {
            if (key == null || !_once.Add(key)) return;
            Info(msg);
        }

        internal static void Warn(string msg) { Write("WARN ", msg); }
        internal static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + msg;
            try { MelonLoader.MelonLogger.Msg(line); } catch { }
            _pending.Add(line);
            if (_pending.Count >= 32) Flush();
        }

        internal static void Flush()
        {
            if (!_ready || _pending.Count == 0) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string l in _pending) sb.AppendLine(l);
                File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
            _pending.Clear();
        }
    }
}
