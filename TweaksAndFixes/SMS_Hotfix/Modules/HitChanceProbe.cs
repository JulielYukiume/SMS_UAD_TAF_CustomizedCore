using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace TAFHotfixBatch1.Modules
{
    /// <summary>
    /// [SMS.2.HCP] 命中率"1% 下限"诊断探针（**只读**，不改任何行为）。
    ///
    /// 历史教训：
    ///   · 不要 patch `Ship.HitChance` —— 参数含 `Nullable<Vector3>`/`Nullable<ShellType>`，
    ///     Harmony 包装后会走 il2cpp→managed thunk，直接 AccessViolation 崩游戏（rc7/rc8 实测）。
    ///   · 不要 patch `Ship.HitChanceCalc.Add` —— UI 每帧调用，同样崩。
    ///
    /// 本版只挂两个**静态、无 nullable、低风险**的方法：
    ///   1. `Util.RoundToStep(float,float)` Prefix —— 证明我们的网格改动是否真的被调用（只记 step≈0.01 的调用）。
    ///   2. `Util.ShortFloat(float,int,float,bool)` Prefix —— 记录 UI 真正拿到的数值（百分比单位）。
    /// </summary>
    internal static class HitChanceProbe
    {
        private static int _step, _sf;
        private static readonly HashSet<int> _seen = new HashSet<int>();

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (!HotfixParams.HitChanceProbe) { HotfixLog.Info("[SMS.2.HCP] disabled"); return; }
            try
            {
                var rts = AccessTools.Method(typeof(Util), "RoundToStep", new Type[] { typeof(float), typeof(float) });
                if (rts != null)
                    harmony.Patch(rts, prefix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Pre_RoundToStep))));
                else HotfixLog.Warn("[SMS.2.HCP] Util.RoundToStep missing");

                var sf = AccessTools.Method(typeof(Util), "ShortFloat", new Type[] { typeof(float), typeof(int), typeof(float), typeof(bool) });
                if (sf != null)
                    harmony.Patch(sf, prefix: new HarmonyMethod(AccessTools.Method(typeof(HitChanceProbe), nameof(Pre_ShortFloat))));
                else HotfixLog.Warn("[SMS.2.HCP] Util.ShortFloat missing");

                HotfixLog.Info("[SMS.2.HCP] probes armed (RoundToStep + ShortFloat, read-only)");
            }
            catch (Exception e) { HotfixLog.Error("[SMS.2.HCP] install failed: " + e); }
        }

        internal static void Pre_RoundToStep(float value, float step)
        {
            try
            {
                if (_step >= 24) return;
                if (Math.Abs(step - 0.01f) > 1e-9f && Math.Abs(step - 0.001f) > 1e-9f) return;
                _step++;
                HotfixLog.Info("[SMS.2.HCP][step] value=" + value.ToString("F6") + " step=" + step.ToString("F6"));
            }
            catch { }
        }

        internal static void Pre_ShortFloat(float v, int signs, float threshold)
        {
            try
            {
                float a = v < 0f ? -v : v;
                if (a <= 0f || a > 3f) return;              // 只看 0~3（百分比单位）
                if (_sf >= 60) return;
                int key = (int)Math.Round(a * 100f);
                if (!_seen.Add(key)) return;
                _sf++;
                HotfixLog.Info("[SMS.2.HCP][ui] v=" + v.ToString("F6") + " signs=" + signs);
            }
            catch { }
        }
    }
}
