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
        /// <summary>仅在 `verbose=1` 时输出的调试日志（默认静默）。</summary>
        internal static void Dbg(string msg)
        {
            if (!HotfixParams.Verbose) return;   // Dbg 仍受 verbose 控制（输出为 INFO 级）
            try { if (HotfixParams.Verbose) Info("[DBG] " + msg); } catch { }
        }

        private const string FileName = "SMS_UAD_TAF_SMS2.log";
        private static readonly List<string> _pending = new List<string>();
        private static string _path;
        private static bool _ready;

        internal static void Init()
        {
            // ★ 不再在启动时创建 Logs 目录：`log_no_file=1`（默认）时不应产生任何痕迹。
            //   目录与文件都在第一次真正写入时才懒创建（见 EnsureFile）。
            _ready = true;
        }

        /// <summary>按需创建 Logs 目录与文件路径（仅在允许写文件时）。</summary>
        private static bool EnsureFile()
        {
            if (_path != null) return true;
            try
            {
                if (HotfixParams.LogNoFile) return false;
                string root = AppContext.BaseDirectory;
                _path = Path.Combine(root, "Logs", FileName);
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return true;
            }
            catch { _path = null; return false; }
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
            // MelonLoader 自带 "[HH:mm:ss.fff]" 前缀，这里不再重复写时间
            string line = "[" + level + "] " + msg;
            try { MelonLoader.MelonLogger.Msg(line); } catch { }
            _pending.Add(line);
            if (_pending.Count >= 32) Flush();
        }

        internal static void Flush()
        {
            if (!_ready || _pending.Count == 0) return;
            try
            {
                if (!EnsureFile()) { _pending.Clear(); return; }   // log_no_file=1：直接丢弃，不建目录
                StringBuilder sb = new StringBuilder();
                foreach (string l in _pending) sb.AppendLine(l);
                File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
            _pending.Clear();
        }
    }
}
