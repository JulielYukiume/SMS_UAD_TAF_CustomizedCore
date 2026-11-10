using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HCP] "命定之死"诊断探针（只读，不改行为）。
    ///
    /// ⚠ rc7 的教训：**不要**给 `Ship.HitChanceCalc.Add`（UI 每帧调用的实例方法）加 Postfix ——
    /// 会触发 AccessViolation（il2cpp→managed thunk 参数不匹配），直接崩游戏。
    /// 本版只挂 `Ship::HitChance/10` 的静态 Postfix，且**不声明任何原方法参数**，只取 `__result`。
    /// </summary>
    internal static class HitChanceProbe
    {
        private static int _n;
        private static readonly HashSet<int> _seen = new HashSet<int>();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HitChanceProbe) { HotfixLog.Info("[SMS.2.HCP] disabled"); return; }
            try
            {
                var hc = AccessTools.Method(typeof(Ship), "HitChance");
                if (hc == null) { HotfixLog.Warn("[SMS.2.HCP] Ship.HitChance not found"); return; }
                harmony.Patch(hc, postfix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Post_HitChance))));
                HotfixLog.Info("[SMS.2.HCP] HitChance probe armed (read-only)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HCP] install failed: " + e); }
        }

        internal static void Post_HitChance(ref float __result)
        {
            try
            {
                if (__result < 0f || __result > 0.05f) return;
                if (_n >= 62) return;
                int key = (int)Math.Round(__result * 1000000f);
                if (!_seen.Add(key)) return;
                _n++;
                HotfixLog.Info("[SMS.2.HCP][hit] result=" + __result.ToString("F6"));
            }
            catch { }
        }
    }
}
