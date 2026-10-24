using System;
using HarmonyLib;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// 日志抑制：TAF 的 ShipsPerPlayer Prefix 会对"**某一个包**在该年没有该舰种"打 ERROR，
    /// 但这不是真缺口（stock 有 3 万+ 保底，后续包/年份仍可取）。我们的 PredefPoolProbe 已在
    /// 取用层面接管（[P] alias fallback），因此这条 ERROR 纯属噪音。
    ///
    /// 本模块在 MelonLogger 层拦截这一条特定文本（不碰其它日志），可逆（参数开关）。
    /// </summary>
    internal static class LogSuppress
    {
        private const string Pattern = "has no valid predefined designs";
        private static int _suppressed;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.SuppressPredefPackErrors) { HotfixLog.Info("[SMS.1.L] LogSuppress: disabled"); return; }
            int patched = 0;
            try
            {
                foreach (var m in typeof(MelonLoader.MelonLogger).GetMethods(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (m.Name != "Error") continue;
                    var ps = m.GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(string)) continue;
                    harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(LogSuppress), nameof(Prefix_Error))));
                    patched++;
                }
                HotfixLog.Info("[SMS.1.L] LogSuppress: armed on " + patched + " MelonLogger.Error overload(s)");
            }
            catch (Exception e) { HotfixLog.Warn("[SMS.1.L] install failed: " + e.Message); }
        }

        // 返回 false ⇒ 抑制本次日志
        internal static bool Prefix_Error(string __0)
        {
            try
            {
                if (__0 != null && __0.IndexOf(Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _suppressed++;
                    if (_suppressed == 1 || _suppressed % 50 == 0)
                        HotfixLog.Info("[SMS.1.L] suppressed predef pack error(s): total=" + _suppressed
                                       + "  (这一条只是某个包在该年缺该舰种，不是真缺口)");
                    return false;
                }
            }
            catch { }
            return true;
        }

        // 抑制 .NET9/UserLibs 互操作噪音（WasmDisassembler 之类）
        internal static bool Prefix_Warning(string __0)
        {
            try
            {
                if (__0 == null) return true;
                if (__0.IndexOf("WasmDisassembler", StringComparison.OrdinalIgnoreCase) >= 0
                    || __0.IndexOf("System.Runtime, Version=9.0.0.0", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _suppressed++;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
